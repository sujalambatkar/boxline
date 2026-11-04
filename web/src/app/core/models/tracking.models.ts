import { BookingStatus, PortSummary } from './booking.models';

export type MovementEventType =
  | 'GateOutEmpty'
  | 'Stuffed'
  | 'GateInFull'
  | 'LoadedOnVessel'
  | 'Discharged'
  | 'GateOutFull'
  | 'DeliveredToConsignee'
  | 'EmptyReturned';

/** The stages of a container's journey, in the order they normally happen. */
export const MOVEMENT_EVENT_ORDER: MovementEventType[] = [
  'GateOutEmpty',
  'Stuffed',
  'GateInFull',
  'LoadedOnVessel',
  'Discharged',
  'GateOutFull',
  'DeliveredToConsignee',
  'EmptyReturned',
];

export const MOVEMENT_EVENT_LABELS: Record<MovementEventType, string> = {
  GateOutEmpty: 'Gate out empty',
  Stuffed: 'Stuffed',
  GateInFull: 'Gate in full',
  LoadedOnVessel: 'Loaded on vessel',
  Discharged: 'Discharged',
  GateOutFull: 'Gate out full',
  DeliveredToConsignee: 'Delivered to consignee',
  EmptyReturned: 'Empty returned',
};

export const MOVEMENT_EVENT_ICONS: Record<MovementEventType, string> = {
  GateOutEmpty: 'logout',
  Stuffed: 'inventory',
  GateInFull: 'login',
  LoadedOnVessel: 'directions_boat',
  Discharged: 'anchor',
  GateOutFull: 'local_shipping',
  DeliveredToConsignee: 'home',
  EmptyReturned: 'assignment_return',
};

export interface Movement {
  id: number;
  containerNumber: string;
  eventType: MovementEventType;
  locationUnLocode: string;
  /** ISO 8601 UTC. */
  occurredAt: string;
  bookingNumber: string | null;
  remarks: string | null;
}

export interface ContainerTracking {
  containerNumber: string;
  containerType: string;
  current: Movement | null;
  events: Movement[];
  /** Which events may be recorded next. Filled for Ops only. */
  allowedNextEvents: MovementEventType[];
}

export interface BookingTracking {
  bookingNumber: string;
  status: BookingStatus;
  pol: PortSummary;
  pod: PortSummary;
  containers: ContainerTracking[];
}

export interface RecordMovementRequest {
  containerNumber: string;
  eventType: MovementEventType;
  locationUnLocode: string;
  /** ISO 8601 UTC. */
  occurredAt: string;
  bookingNumber?: string;
  remarks?: string;
}

export const BOOKING_NUMBER_PATTERN = /^BL-\d{6}-\d{4}$/i;
export const UN_LOCODE_PATTERN = /^[A-Za-z]{2}[A-Za-z2-9]{3}$/;
export const MAX_REMARKS_LENGTH = 500;

export const isBookingNumber = (query: string): boolean => BOOKING_NUMBER_PATTERN.test(query.trim());
