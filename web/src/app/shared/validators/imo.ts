import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * IMO ship numbers: seven digits, where the last is a check digit. Multiply the first six digits by
 * 7, 6, 5, 4, 3, 2; the last digit of the sum must equal the seventh digit. Mirrors the backend `ImoNumber`.
 */

/** Strips an "IMO" prefix, spaces and hyphens: "IMO 9074729" becomes "9074729". */
export function normalizeImoNumber(value: string | null | undefined): string {
  const cleaned = (value ?? '').replace(/[\s-]/g, '').toUpperCase();
  return cleaned.startsWith('IMO') ? cleaned.slice(3) : cleaned;
}

/** The check digit for the first six digits, or null when the input is not six digits. */
export function computeImoCheckDigit(firstSix: string): number | null {
  const digits = normalizeImoNumber(firstSix);
  if (!/^\d{6}$/.test(digits)) {
    return null;
  }
  const sum = [...digits].reduce((total, d, i) => total + Number(d) * (7 - i), 0);
  return sum % 10;
}

/** A human-readable reason the number is invalid, or null when it is valid. */
export function validateImoNumber(value: string | null | undefined): string | null {
  const number = normalizeImoNumber(value);

  if (number.length === 0) {
    return 'IMO number is required.';
  }
  if (!/^\d{7}$/.test(number)) {
    return 'IMO number must be exactly 7 digits.';
  }

  const expected = computeImoCheckDigit(number.slice(0, 6));
  const actual = Number(number[6]);
  return actual === expected ? null : `Check digit is wrong: ${number.slice(0, 6)} should end in ${expected}, not ${actual}.`;
}

/** Reactive-forms validator. The error is `{ imo: '<message>' }`. Empty values are left to `required`. */
export function imoValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!control.value) {
      return null;
    }
    const message = validateImoNumber(control.value);
    return message ? { imo: message } : null;
  };
}
