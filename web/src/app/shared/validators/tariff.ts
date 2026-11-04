import { AbstractControl, FormArray, ValidationErrors, ValidatorFn } from '@angular/forms';

import { MAX_FREE_DAYS, MAX_RATE_PER_DAY, MAX_SLABS } from '../../core/models/detention.models';

/** A slab as edited: the start day is derived from the free days and the previous slab, so only the end and rate are typed. */
export interface SlabRow {
  toDay: number | null;
  ratePerDayUsd: number | null;
}

/** The start day of each slab: right after the free days, then right after the previous slab ends. */
export function slabStartDays(freeDays: number, rows: SlabRow[]): number[] {
  const starts: number[] = [];
  rows.forEach((row, i) => starts.push(i === 0 ? freeDays + 1 : (rows[i - 1].toDay ?? starts[i - 1]) + 1));
  return starts;
}

/**
 * Checks a tariff's slabs and returns every problem, worded like the backend's `DetentionTariffRules`:
 * contiguous slabs starting right after the free days, only the last open-ended, a sensible daily rate.
 */
export function validateSlabs(freeDays: number, rows: SlabRow[]): string[] {
  const problems: string[] = [];

  if (rows.length === 0) {
    problems.push('Add at least one slab.');
  }
  if (rows.length > MAX_SLABS) {
    problems.push(`A tariff can have at most ${MAX_SLABS} slabs.`);
  }

  const starts = slabStartDays(freeDays, rows);
  rows.forEach((row, i) => {
    const n = i + 1;
    const isLast = i === rows.length - 1;

    if (isLast) {
      if (row.toDay !== null) {
        problems.push('The last slab must be open-ended so that long stays are always priced.');
      }
    } else if (row.toDay === null) {
      problems.push(`Only the last slab can be open-ended (slab ${n} has no end day).`);
    }

    if (row.toDay !== null && row.toDay < starts[i]) {
      problems.push(`Slab ${n}: the end day cannot be before the start day (day ${starts[i]}).`);
    }

    const rate = row.ratePerDayUsd;
    if (rate === null || !(rate > 0) || rate > MAX_RATE_PER_DAY || Math.round(rate * 100) / 100 !== rate) {
      problems.push(`Slab ${n}: the daily rate must be above 0 and at most ${MAX_RATE_PER_DAY} USD, with at most two decimals.`);
    }
  });

  return problems;
}

/**
 * Form-group validator for a tariff with `freeDays`, `validFrom`, `validTo` and a `slabs` FormArray of
 * `{ toDay, ratePerDayUsd }`. Errors: `{ freeDays }`, `{ validTo }` (messages) and `{ slabs: string[] }`.
 */
export function tariffValidator(): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const freeDays = Number(group.get('freeDays')?.value);
    const validFrom = (group.get('validFrom')?.value ?? '') as string;
    const validTo = (group.get('validTo')?.value ?? '') as string;
    const rows = ((group.get('slabs') as FormArray).controls ?? []).map(slab => ({
      toDay: numberOrNull(slab.get('toDay')?.value),
      ratePerDayUsd: numberOrNull(slab.get('ratePerDayUsd')?.value),
    }));

    const errors: ValidationErrors = {};

    if (!Number.isInteger(freeDays) || freeDays < 0 || freeDays > MAX_FREE_DAYS) {
      errors['freeDays'] = `Free days must be a whole number between 0 and ${MAX_FREE_DAYS}.`;
    }
    if (validFrom && validTo && validTo < validFrom) {
      errors['validTo'] = 'The end date cannot be before the start date.';
    }

    const slabProblems = Number.isInteger(freeDays) ? validateSlabs(freeDays, rows) : [];
    if (slabProblems.length > 0) {
      errors['slabs'] = slabProblems;
    }

    return Object.keys(errors).length > 0 ? errors : null;
  };
}

function numberOrNull(value: unknown): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}
