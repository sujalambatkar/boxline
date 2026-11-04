import { Component, ElementRef, Input, OnChanges, OnDestroy, SimpleChanges, ViewChild } from '@angular/core';
import { FormControl } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';

import { ChatConnectionState, ChatMessage, MAX_CHAT_LENGTH } from '../../../core/models/chat.models';
import { AuthService } from '../../../core/services/auth.service';
import { ChatService } from '../../../core/services/chat.service';

/** Live conversation for one booking. Owns its connection: it opens on creation and closes on destroy. */
@Component({
  selector: 'app-chat-panel',
  templateUrl: './chat-panel.component.html',
  styleUrls: ['./chat-panel.component.scss'],
  providers: [ChatService],
})
export class ChatPanelComponent implements OnChanges, OnDestroy {
  @Input() bookingId!: number;
  @ViewChild('scroller') private scroller?: ElementRef<HTMLElement>;

  readonly maxLength = MAX_CHAT_LENGTH;
  readonly text = new FormControl('', { nonNullable: true });
  readonly stateLabels: Record<ChatConnectionState, string> = {
    connecting: 'Connecting…',
    connected: 'Live',
    reconnecting: 'Reconnecting…',
    disconnected: 'Offline',
  };

  messages: ChatMessage[] = [];
  state: ChatConnectionState = 'disconnected';
  error: string | null = null;
  sending = false;

  private readonly destroy$ = new Subject<void>();
  private subscribed = false;

  constructor(
    readonly chat: ChatService,
    private readonly auth: AuthService,
  ) {}

  get canSend(): boolean {
    return this.state === 'connected' && !this.sending && this.text.value.trim().length > 0;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['bookingId']) {
      return;
    }
    if (!this.subscribed) {
      this.subscribed = true;
      this.chat.messages$.pipe(takeUntil(this.destroy$)).subscribe(messages => {
        this.messages = messages;
        setTimeout(() => this.scrollToEnd());
      });
      this.chat.state$.pipe(takeUntil(this.destroy$)).subscribe(state => (this.state = state));
      this.chat.error$.pipe(takeUntil(this.destroy$)).subscribe(error => (this.error = error));
    }
    void this.chat.connect(this.bookingId);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    void this.chat.disconnect();
  }

  retry(): void {
    void this.chat.connect(this.bookingId);
  }

  async send(): Promise<void> {
    if (!this.canSend) {
      return;
    }
    this.sending = true;
    const sent = await this.chat.send(this.text.value);
    this.sending = false;
    if (sent) {
      this.text.setValue('');
    }
  }

  /** Enter sends; Shift+Enter inserts a new line. */
  onEnter(event: Event): void {
    if ((event as KeyboardEvent).shiftKey) {
      return;
    }
    event.preventDefault();
    void this.send();
  }

  isMine(message: ChatMessage): boolean {
    return message.senderUserId === this.auth.user?.id;
  }

  trackById(_: number, message: ChatMessage): number {
    return message.id;
  }

  private scrollToEnd(): void {
    const el = this.scroller?.nativeElement;
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  }
}
