import { AbstractControl, FormArray, ValidationErrors, ValidatorFn } from '@angular/forms';

/** One planned call as edited in the form: times are `datetime-local` strings, empty when not set. */
export interface RotationCall {
  portId: number | null;
  eta: string;
  etd: string;
}

const time = (value: string): number | null => (value ? new Date(value).getTime() : null);

/**
 * Checks a planned rotation and returns every problem, worded like the backend's `VoyageRotation.ValidatePlan`.
 * The first port needs an ETD, the last an ETA, intermediate ports both; ETD cannot precede ETA; a port cannot
 * appear twice; and each call must arrive after the previous one departs.
 */
export function validateRotation(calls: RotationCall[]): string[] {
  const problems: string[] = [];

  if (calls.length < 2) {
    problems.push('A rotation needs at least two port calls.');
  }

  calls.forEach((call, i) => {
    const n = i + 1;
    const first = i === 0;
    const last = i === calls.length - 1;
    const eta = time(call.eta);
    const etd = time(call.etd);

    if (first && etd === null) {
      problems.push(`Call ${n} (first port): ETD is required.`);
    } else if (last && eta === null) {
      problems.push(`Call ${n} (last port): ETA is required.`);
    } else if (!first && !last && (eta === null || etd === null)) {
      problems.push(`Call ${n}: both ETA and ETD are required for an intermediate port.`);
    }

    if (eta !== null && etd !== null && etd < eta) {
      problems.push(`Call ${n}: ETD cannot be before ETA.`);
    }

    if (i > 0) {
      const previous = calls[i - 1];
      const previousDeparture = time(previous.etd) ?? time(previous.eta);
      const arrival = eta ?? etd;
      if (previousDeparture !== null && arrival !== null && arrival < previousDeparture) {
        problems.push(`Call ${n}: arrival is before call ${n - 1} departs.`);
      }
    }
  });

  const seen = new Map<number, number[]>();
  calls.forEach((call, i) => {
    if (call.portId !== null) {
      seen.set(call.portId, [...(seen.get(call.portId) ?? []), i + 1]);
    }
  });
  seen.forEach(numbers => {
    if (numbers.length > 1) {
      problems.push(`A port can appear only once in a rotation (calls ${numbers.join(' and ')}).`);
    }
  });

  return problems;
}

/** FormArray-level validator for a voyage's calls. The error is `{ rotation: string[] }`. */
export function rotationValidator(): ValidatorFn {
  return (array: AbstractControl): ValidationErrors | null => {
    const calls = (array as FormArray).controls.map(group => ({
      portId: (group.get('portId')?.value ?? null) as number | null,
      eta: (group.get('eta')?.value ?? '') as string,
      etd: (group.get('etd')?.value ?? '') as string,
    }));
    const problems = validateRotation(calls);
    return problems.length > 0 ? { rotation: problems } : null;
  };
}
