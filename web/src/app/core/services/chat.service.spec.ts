import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HubConnection } from '@microsoft/signalr';

import { ChatMessage, ChatConnectionState } from '../models/chat.models';
import { AuthService } from './auth.service';
import { ChatService, HUB_CONNECTION_FACTORY } from './chat.service';

/** Just enough of a SignalR connection for the service, with hooks to drive it from tests. */
class FakeHub {
  private readonly handlers = new Map<string, (message: ChatMessage) => void>();
  reconnecting: () => void = () => undefined;
  reconnected: () => Promise<void> | void = () => undefined;
  closed: () => void = () => undefined;
  invocations: { method: string; args: unknown[] }[] = [];
  startError: Error | null = null;
  invokeErrors = new Map<string, Error>();
  stopped = false;

  on(name: string, handler: (message: ChatMessage) => void): void {
    this.handlers.set(name, handler);
  }
  onreconnecting(callback: () => void): void {
    this.reconnecting = callback;
  }
  onreconnected(callback: () => Promise<void> | void): void {
    this.reconnected = callback;
  }
  onclose(callback: () => void): void {
    this.closed = callback;
  }
  start(): Promise<void> {
    return this.startError ? Promise.reject(this.startError) : Promise.resolve();
  }
  stop(): Promise<void> {
    this.stopped = true;
    return Promise.resolve();
  }
  invoke(method: string, ...args: unknown[]): Promise<void> {
    this.invocations.push({ method, args });
    const error = this.invokeErrors.get(method);
    return error ? Promise.reject(error) : Promise.resolve();
  }
  receive(message: ChatMessage): void {
    this.handlers.get('MessageReceived')!(message);
  }
  asConnection(): HubConnection {
    return this as unknown as HubConnection;
  }
}

describe('ChatService', () => {
  const message = (id: number, text = `m${id}`, bookingId = 5): ChatMessage => ({
    id, bookingId, senderUserId: 1, senderName: 'Acme', senderRole: 'Shipper', text, sentAt: '2026-11-04T09:30:00Z',
  });

  let hub: FakeHub;
  let hubs: FakeHub[];
  /** Lets a test shape the next hub before the service uses it (the factory builds one per connect). */
  let configureNextHub: (hub: FakeHub) => void;
  let tokenFactory: () => string;
  let service: ChatService;
  let http: HttpTestingController;

  /** Lets pending promises run, so the service reaches its next HTTP call. */
  const settle = () => new Promise<void>(resolve => setTimeout(resolve));
  const historyRequest = () => http.expectOne(r => r.url.endsWith('/bookings/5/messages'));
  const states = (): ChatConnectionState[] => {
    const seen: ChatConnectionState[] = [];
    service.state$.subscribe(s => seen.push(s));
    return seen;
  };
  const current = (): ChatMessage[] => {
    let value: ChatMessage[] = [];
    service.messages$.subscribe(m => (value = m)).unsubscribe();
    return value;
  };
  const currentError = (): string | null => {
    let value: string | null = null;
    service.error$.subscribe(e => (value = e)).unsubscribe();
    return value;
  };

  async function connected(history: ChatMessage[] = []): Promise<void> {
    const connecting = service.connect(5);
    await settle();
    historyRequest().flush(history);
    await connecting;
  }

  beforeEach(() => {
    hubs = [];
    configureNextHub = () => undefined;
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        ChatService,
        { provide: AuthService, useValue: { token: 'jwt-abc', user: { id: 1 } } },
        {
          provide: HUB_CONNECTION_FACTORY,
          useValue: (token: () => string) => {
            tokenFactory = token;
            hub = new FakeHub();
            configureNextHub(hub);
            hubs.push(hub);
            return hub.asConnection();
          },
        },
      ],
    });
    service = TestBed.inject(ChatService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('joins the booking group, then loads history oldest first, and reports connected', async () => {
    const seen = states();

    await connected([message(1), message(2)]);

    expect(hub.invocations).toEqual([{ method: 'JoinBooking', args: [5] }]);
    expect(current().map(m => m.id)).toEqual([1, 2]);
    expect(seen).toEqual(['disconnected', 'connecting', 'connected']);
    expect(tokenFactory()).toBe('jwt-abc');
  });

  it('appends live messages in id order and ignores duplicates and other bookings', async () => {
    await connected([message(1)]);

    hub.receive(message(3));
    hub.receive(message(2));
    hub.receive(message(2, 'again'));
    hub.receive(message(9, 'elsewhere', 77));

    expect(current().map(m => m.id)).toEqual([1, 2, 3]);
    expect(current()[1].text).toBe('m2');
  });

  it('keeps a message that arrives while history is still loading, without duplicating it', async () => {
    const connecting = service.connect(5);
    await settle();

    hub.receive(message(2));
    historyRequest().flush([message(1), message(2)]);
    await connecting;

    expect(current().map(m => m.id)).toEqual([1, 2]);
  });

  it('sends trimmed text through the hub', async () => {
    await connected();

    const sent = await service.send('  hello  ');

    expect(sent).toBeTrue();
    expect(hub.invocations.at(-1)).toEqual({ method: 'SendMessage', args: [5, 'hello'] });
    expect(currentError()).toBeNull();
  });

  it('sends nothing for blank text or when not connected', async () => {
    expect(await service.send('hi')).toBeFalse();

    await connected();
    expect(await service.send('   ')).toBeFalse();

    expect(hub.invocations.filter(i => i.method === 'SendMessage')).toEqual([]);
  });

  it('shows only the hub\'s own message when the server refuses a send', async () => {
    await connected();
    hub.invokeErrors.set(
      'SendMessage',
      new Error("An unexpected error occurred invoking 'SendMessage' on the server. HubException: Message cannot be empty."),
    );

    const sent = await service.send('x');

    expect(sent).toBeFalse();
    expect(currentError()).toBe('Message cannot be empty.');
  });

  it('falls back to a generic message for other send failures', async () => {
    await connected();
    hub.invokeErrors.set('SendMessage', new Error('socket closed'));

    await service.send('x');

    expect(currentError()).toBe('Your message could not be sent.');
  });

  it('reports a failed connection and stays offline', async () => {
    configureNextHub = h => (h.startError = new Error('negotiate failed'));
    const seen = states();

    await service.connect(5);

    expect(currentError()).toBe('Could not connect to the chat.');
    expect(seen.at(-1)).toBe('disconnected');
  });

  it('shows the server\'s reason when joining is refused', async () => {
    configureNextHub = h =>
      h.invokeErrors.set('JoinBooking', new Error('... HubException: You do not have access to this booking.'));
    const seen = states();

    await service.connect(5);

    expect(currentError()).toBe('You do not have access to this booking.');
    expect(seen.at(-1)).toBe('disconnected');
    http.expectNone(r => r.url.endsWith('/messages'));
  });

  it('rejoins and refills missed messages after an automatic reconnect', async () => {
    await connected([message(1)]);
    const seen = states();

    hub.reconnecting();
    expect(seen.at(-1)).toBe('reconnecting');

    const reconnected = Promise.resolve(hub.reconnected());
    await settle();
    historyRequest().flush([message(1), message(2), message(3)]);
    await reconnected;
    await settle();

    expect(hub.invocations.filter(i => i.method === 'JoinBooking')).toHaveSize(2);
    expect(current().map(m => m.id)).toEqual([1, 2, 3]);
    expect(seen.at(-1)).toBe('connected');
  });

  it('goes offline when the connection closes for good', async () => {
    await connected();
    const seen = states();

    hub.closed();

    expect(seen.at(-1)).toBe('disconnected');
    expect(await service.send('late')).toBeFalse();
  });

  it('closes the connection on disconnect', async () => {
    await connected();

    await service.disconnect();

    expect(hub.stopped).toBeTrue();
  });

  it('replaces the old connection and clears messages when connecting again', async () => {
    await connected([message(1)]);
    const first = hub;

    const again = service.connect(5);
    await settle();
    expect(current()).toEqual([]);
    historyRequest().flush([message(7)]);
    await again;

    expect(first.stopped).toBeTrue();
    expect(hubs).toHaveSize(2);
    expect(current().map(m => m.id)).toEqual([7]);
  });
});
