import { MovementEventType } from './tracking.models';

export interface DetentionSlab {
  fromDay: number;
  /** Null for the open-ended last slab. */
  toDay: number | null;
  ratePerDayUsd: number;
}

export interface Tariff {
  id: number;
  containerTypeId: number;
  containerType: string;
  freeDays: number;
  /** yyyy-MM-dd */
  validFrom: string;
  /** yyyy-MM-dd, or null when the tariff has no end date. */
  validTo: string | null;
  slabs: DetentionSlab[];
}

export interface SaveTariffRequest {
  containerTypeId: number;
  freeDays: number;
  validFrom: string;
  validTo: string | null;
  slabs: DetentionSlab[];
}

export interface SlabCharge {
  fromDay: number;
  toDay: number | null;
  days: number;
  ratePerDayUsd: number;
  amountUsd: number;
}

export interface DetentionResult {
  containerType: string;
  containerNumber: string | null;
  bookingNumber: string | null;
  startedBy: MovementEventType | null;
  tariff: Tariff;
  outDate: string;
  returnDate: string;
  /** True when the container has not come back, so the period runs up to today. */
  isOngoing: boolean;
  countWeekendsAsChargeable: boolean;
  totalDays: number;
  countedDays: number;
  weekendDaysSkipped: number;
  freeDaysUsed: number;
  chargeableDays: number;
  slabs: SlabCharge[];
  totalAmountUsd: number;
  currency: string;
}

export interface CalculateRequest {
  containerTypeId: number;
  outDate: string;
  returnDate: string;
  countWeekendsAsChargeable: boolean;
}

export const MAX_FREE_DAYS = 60;
export const MAX_SLABS = 10;
export const MAX_RATE_PER_DAY = 10_000;

/** "6-10" for a bounded slab, "16+" for the open-ended one. */
export function slabRange(slab: { fromDay: number; toDay: number | null }): string {
  return slab.toDay === null ? `${slab.fromDay}+` : `${slab.fromDay}–${slab.toDay}`;
}
