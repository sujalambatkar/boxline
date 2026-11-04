import { PortSummary } from './booking.models';

export type VoyageStatus = 'Scheduled' | 'Underway' | 'Completed' | 'Cancelled';

export const VOYAGE_STATUS_LABELS: Record<VoyageStatus, string> = {
  Scheduled: 'Scheduled',
  Underway: 'Underway',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
};

export interface Vessel {
  id: number;
  name: string;
  imoNumber: string;
}

export interface VoyageCall {
  id: number;
  sequence: number;
  port: PortSummary;
  /** ISO 8601 UTC, or null. */
  eta: string | null;
  etd: string | null;
  ata: string | null;
  atd: string | null;
}

export interface Voyage {
  id: number;
  voyageNumber: string;
  status: VoyageStatus;
  allowedTransitions: VoyageStatus[];
  vessel: Vessel;
  calls: VoyageCall[];
}

export interface CallRequest {
  sequence: number;
  portId: number;
  eta: string | null;
  etd: string | null;
}

export interface SaveVoyageRequest {
  vesselId: number;
  voyageNumber: string;
  calls: CallRequest[];
}

export interface VoyageListQuery {
  search: string;
  status: VoyageStatus | '';
  vesselId: number | null;
  portId: number | null;
  page: number;
  pageSize: number;
}

/** A sailing that connects two ports, as offered to a shipper choosing a voyage. */
export interface ScheduleOption {
  voyageId: number;
  voyageNumber: string;
  vesselName: string;
  imoNumber: string;
  pol: PortSummary;
  pod: PortSummary;
  departure: string;
  arrival: string;
  transitDays: number;
}
