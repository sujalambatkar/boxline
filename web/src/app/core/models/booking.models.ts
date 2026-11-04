export type BookingStatus = 'Draft' | 'Confirmed' | 'Cancelled';

export const MAX_QUANTITY_PER_LINE = 50;
export const MAX_CARGO_DESCRIPTION_LENGTH = 500;

export const BOOKING_STATUS_LABELS: Record<BookingStatus, string> = {
  Draft: 'Draft',
  Confirmed: 'Confirmed',
  Cancelled: 'Cancelled',
};

export interface Port {
  id: number;
  unLocode: string;
  name: string;
  country: string;
}

export interface PortSummary {
  id: number;
  unLocode: string;
  name: string;
}

export interface BookingLine {
  id: number;
  containerTypeId: number;
  containerTypeCode: string;
  quantity: number;
}

export interface Booking {
  id: number;
  bookingNumber: string;
  shipperId: number;
  shipperName: string;
  pol: PortSummary;
  pod: PortSummary;
  voyageId: number | null;
  cargoDescription: string;
  status: BookingStatus;
  allowedTransitions: BookingStatus[];
  createdAt: string;
  updatedAt: string;
  /** Opaque concurrency token; send it back unchanged when updating. */
  rowVersion: string;
  totalContainers: number;
  lines: BookingLine[];
}

export interface BookingLineRequest {
  containerTypeId: number;
  quantity: number;
}

export interface SaveBookingRequest {
  polId: number;
  podId: number;
  cargoDescription: string;
  lines: BookingLineRequest[];
}

export interface BookingListQuery {
  search: string;
  status: BookingStatus | '';
  polId: number | null;
  podId: number | null;
  /** yyyy-MM-dd, inclusive; empty for no limit. */
  createdFrom: string;
  createdTo: string;
  page: number;
  pageSize: number;
}
