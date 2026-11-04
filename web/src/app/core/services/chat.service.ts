import { HttpClient } from '@angular/common/http';
import { Inject, Injectable, InjectionToken } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { BehaviorSubject, firstValueFrom, Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ChatConnectionState, ChatMessage } from '../models/chat.models';
import { AuthService } from './auth.service';

/** Builds the SignalR connection. Replaced in tests so no network is needed. */
export const HUB_CONNECTION_FACTORY = new InjectionToken<(accessToken: () => string) => HubConnection>(
  'HUB_CONNECTION_FACTORY',
  {
    providedIn: 'root',
    factory: () => accessToken =>
      new HubConnectionBuilder()
        .withUrl(environment.hubUrl, { accessTokenFactory: accessToken })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
        .build(),
  },
);

/**
 * One booking's live conversation. Provide it per component (not in root) so each chat panel owns its own
 * connection and closes it when the panel is destroyed.
 *
 * It joins the booking's group first and loads the stored history second, so nothing sent in between is
 * missed, and merges the two by message id. After an automatic reconnect it joins again (groups do not
 * survive a dropped connection) and reloads history to fill any gap.
 */
@Injectable()
export class ChatService {
  private readonly messagesSubject = new BehaviorSubject<ChatMessage[]>([]);
  private readonly stateSubject = new BehaviorSubject<ChatConnectionState>('disconnected');
  private readonly errorSubject = new BehaviorSubject<string | null>(null);

  private connection: HubConnection | null = null;
  private bookingId = 0;

  readonly messages$: Observable<ChatMessage[]> = this.messagesSubject.asObservable();
  readonly state$: Observable<ChatConnectionState> = this.stateSubject.asObservable();
  readonly error$: Observable<string | null> = this.errorSubject.asObservable();

  constructor(
    private readonly http: HttpClient,
    private readonly auth: AuthService,
    @Inject(HUB_CONNECTION_FACTORY) private readonly hubFactory: (accessToken: () => string) => HubConnection,
  ) {}

  /** Connects, joins the booking's group and loads history. Failures surface through `error$` and `state$`. */
  async connect(bookingId: number): Promise<void> {
    await this.closeCurrent();
    this.bookingId = bookingId;
    this.messagesSubject.next([]);
    this.errorSubject.next(null);
    this.stateSubject.next('connecting');

    const connection = this.hubFactory(() => this.auth.token ?? '');
    connection.on('MessageReceived', (message: ChatMessage) => this.append(message));
    connection.onreconnecting(() => this.stateSubject.next('reconnecting'));
    connection.onreconnected(async () => {
      try {
        await this.joinAndLoad();
        this.stateSubject.next('connected');
      } catch (error) {
        this.fail('Reconnected, but could not rejoin the conversation.', error);
      }
    });
    connection.onclose(() => this.stateSubject.next('disconnected'));
    this.connection = connection;

    try {
      await connection.start();
      await this.joinAndLoad();
      this.stateSubject.next('connected');
    } catch (error) {
      this.fail('Could not connect to the chat.', error);
    }
  }

  /** Sends a message. Returns false when nothing was sent (blank text, not connected, or the server refused it). */
  async send(text: string): Promise<boolean> {
    const trimmed = text.trim();
    if (!trimmed || !this.connection || this.stateSubject.value !== 'connected') {
      return false;
    }

    try {
      await this.connection.invoke('SendMessage', this.bookingId, trimmed);
      this.errorSubject.next(null);
      return true;
    } catch (error) {
      this.errorSubject.next(ChatService.serverMessage(error) ?? 'Your message could not be sent.');
      return false;
    }
  }

  async disconnect(): Promise<void> {
    await this.closeCurrent();
    this.stateSubject.next('disconnected');
  }

  /** Stops the current connection, if any, without announcing "offline" (a new one is usually about to start). */
  private async closeCurrent(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    if (connection) {
      await connection.stop();
    }
  }

  private async joinAndLoad(): Promise<void> {
    await this.connection!.invoke('JoinBooking', this.bookingId);
    const history = await firstValueFrom(
      this.http.get<ChatMessage[]>(`${environment.apiUrl}/bookings/${this.bookingId}/messages`),
    );
    history.forEach(message => this.append(message));
  }

  private append(message: ChatMessage): void {
    const current = this.messagesSubject.value;
    if (message.bookingId !== this.bookingId || current.some(m => m.id === message.id)) {
      return;
    }
    this.messagesSubject.next([...current, message].sort((a, b) => a.id - b.id));
  }

  private fail(summary: string, error: unknown): void {
    this.errorSubject.next(ChatService.serverMessage(error) ?? summary);
    this.stateSubject.next('disconnected');
  }

  /** SignalR wraps a hub's message as "... HubException: <message>"; show only the message. */
  private static serverMessage(error: unknown): string | null {
    const text = error instanceof Error ? error.message : '';
    const marker = 'HubException: ';
    const index = text.indexOf(marker);
    return index >= 0 ? text.slice(index + marker.length) : null;
  }
}
