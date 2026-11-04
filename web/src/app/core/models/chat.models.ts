import { UserRole } from './auth.models';

export interface ChatMessage {
  id: number;
  bookingId: number;
  senderUserId: number;
  senderName: string;
  senderRole: UserRole;
  text: string;
  /** ISO 8601 UTC. */
  sentAt: string;
}

export type ChatConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export const MAX_CHAT_LENGTH = 2000;
