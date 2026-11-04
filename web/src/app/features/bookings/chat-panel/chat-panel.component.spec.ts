import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { BehaviorSubject } from 'rxjs';

import { ChatConnectionState, ChatMessage } from '../../../core/models/chat.models';
import { AuthService } from '../../../core/services/auth.service';
import { ChatService } from '../../../core/services/chat.service';
import { SharedModule } from '../../../shared/shared.module';
import { ChatPanelComponent } from './chat-panel.component';

describe('ChatPanelComponent', () => {
  const message = (id: number, senderUserId: number, text: string, role: 'Ops' | 'Shipper' = 'Shipper'): ChatMessage => ({
    id, bookingId: 5, senderUserId, senderName: role === 'Ops' ? 'ops@boxline.test' : 'Acme Exports', senderRole: role, text,
    sentAt: '2026-11-04T09:30:00Z',
  });

  let fixture: ComponentFixture<ChatPanelComponent>;
  let component: ChatPanelComponent;
  let messages$: BehaviorSubject<ChatMessage[]>;
  let state$: BehaviorSubject<ChatConnectionState>;
  let error$: BehaviorSubject<string | null>;
  let chat: {
    messages$: BehaviorSubject<ChatMessage[]>;
    state$: BehaviorSubject<ChatConnectionState>;
    error$: BehaviorSubject<string | null>;
    connect: jasmine.Spy;
    send: jasmine.Spy;
    disconnect: jasmine.Spy;
  };

  const el = () => fixture.nativeElement as HTMLElement;
  const sendButton = () => el().querySelector('button[type="submit"]') as HTMLButtonElement;
  const textarea = () => el().querySelector('textarea') as HTMLTextAreaElement;
  const type = (value: string) => {
    component.text.setValue(value);
    fixture.detectChanges();
  };

  beforeEach(async () => {
    messages$ = new BehaviorSubject<ChatMessage[]>([]);
    state$ = new BehaviorSubject<ChatConnectionState>('connected');
    error$ = new BehaviorSubject<string | null>(null);
    chat = {
      messages$, state$, error$,
      connect: jasmine.createSpy('connect').and.resolveTo(),
      send: jasmine.createSpy('send').and.resolveTo(true),
      disconnect: jasmine.createSpy('disconnect').and.resolveTo(),
    };

    await TestBed.configureTestingModule({
      declarations: [ChatPanelComponent],
      imports: [SharedModule, NoopAnimationsModule],
      providers: [{ provide: AuthService, useValue: { user: { id: 1 } } }],
    })
      .overrideComponent(ChatPanelComponent, { set: { providers: [{ provide: ChatService, useValue: chat }] } })
      .compileComponents();

    fixture = TestBed.createComponent(ChatPanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('bookingId', 5);
    fixture.detectChanges();
  });

  it('opens the conversation for its booking', () => {
    expect(chat.connect).toHaveBeenCalledOnceWith(5);
  });

  it('shows messages, marks the user\'s own, and badges Ops', () => {
    messages$.next([message(1, 1, 'Is it ready?'), message(2, 99, 'Tomorrow.', 'Ops')]);
    fixture.detectChanges();

    const rows = Array.from(el().querySelectorAll('.message'));
    expect(rows.map(r => r.querySelector('.text')?.textContent)).toEqual(['Is it ready?', 'Tomorrow.']);
    expect(rows[0].classList).toContain('mine');
    expect(rows[1].classList).not.toContain('mine');
    expect(rows[1].textContent).toContain('BoxLine');
  });

  it('invites the first message when connected and empty', () => {
    expect(el().textContent).toContain('No messages yet');
  });

  it('shows the connection state', () => {
    expect(el().querySelector('.state')?.textContent).toContain('Live');

    state$.next('reconnecting');
    fixture.detectChanges();

    expect(el().querySelector('.state')?.textContent).toContain('Reconnecting');
  });

  it('keeps Send disabled until there is text and a live connection', () => {
    expect(sendButton().disabled).toBeTrue();

    type('   ');
    expect(sendButton().disabled).toBeTrue();

    type('hello');
    expect(sendButton().disabled).toBeFalse();

    state$.next('reconnecting');
    fixture.detectChanges();
    expect(sendButton().disabled).toBeTrue();
  });

  it('sends the text and clears the box', async () => {
    type('hello');

    await component.send();

    expect(chat.send).toHaveBeenCalledOnceWith('hello');
    expect(component.text.value).toBe('');
  });

  it('keeps the text when the server refuses the message so it can be fixed', async () => {
    chat.send.and.resolveTo(false);
    type('hello');

    await component.send();

    expect(component.text.value).toBe('hello');
  });

  it('sends on Enter but not on Shift+Enter', () => {
    type('hello');

    textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, cancelable: true }));
    expect(chat.send).not.toHaveBeenCalled();

    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    textarea().dispatchEvent(enter);
    expect(chat.send).toHaveBeenCalledOnceWith('hello');
    expect(enter.defaultPrevented).toBeTrue();
  });

  it('offers a retry when offline', () => {
    state$.next('disconnected');
    fixture.detectChanges();
    chat.connect.calls.reset();

    (Array.from(el().querySelectorAll('button')).find(b => b.textContent?.includes('Retry')) as HTMLButtonElement).click();

    expect(chat.connect).toHaveBeenCalledOnceWith(5);
  });

  it('shows errors from the service', () => {
    error$.next('Message cannot be empty.');
    fixture.detectChanges();

    expect(el().querySelector('.error')?.textContent).toContain('Message cannot be empty.');
  });

  it('closes the connection when the panel is destroyed', () => {
    fixture.destroy();

    expect(chat.disconnect).toHaveBeenCalled();
  });
});
