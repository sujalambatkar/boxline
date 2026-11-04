import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * ISO 6346 container number rules. This mirrors the backend `Iso6346Validator`
 * (BoxLine.Core) so users get the same answer, with the same wording, as they type.
 */

const NUMBER_LENGTH = 11;
const BODY_LENGTH = 10;

/** A=10 upwards, skipping multiples of 11 (11, 22, 33). */
const LETTER_VALUES: number[] = (() => {
  const values: number[] = [];
  let next = 10;
  for (let i = 0; i < 26; i++) {
    if (next % 11 === 0) {
      next++;
    }
    values.push(next++);
  }
  return values;
})();

const isLetter = (c: string): boolean => c >= 'A' && c <= 'Z' && c.length === 1;
const isDigit = (c: string): boolean => c >= '0' && c <= '9' && c.length === 1;

/** Upper-cases and strips whitespace, hyphens and dots. */
export function normalizeContainerNumber(value: string | null | undefined): string {
  return (value ?? '').replace(/[\s.-]/g, '').toUpperCase();
}

/**
 * Check digit for the first ten characters (4 letters + 6 digits), or `null` when the
 * input is not in that shape.
 */
export function computeCheckDigit(firstTen: string): number | null {
  const body = normalizeContainerNumber(firstTen);
  const chars = [...body];
  if (
    chars.length !== BODY_LENGTH ||
    !chars.slice(0, 4).every(isLetter) ||
    !chars.slice(4).every(isDigit)
  ) {
    return null;
  }

  const sum = chars.reduce((total, c, position) => {
    const value = isDigit(c) ? Number(c) : LETTER_VALUES[c.charCodeAt(0) - 65];
    return total + value * 2 ** position;
  }, 0);

  return (sum % 11) % 10;
}

/** A human-readable reason the number is invalid, or `null` when it is valid. */
export function validateContainerNumber(value: string | null | undefined): string | null {
  const number = normalizeContainerNumber(value);
  const chars = [...number];

  if (chars.length === 0) {
    return 'Container number is required.';
  }
  if (chars.length !== NUMBER_LENGTH) {
    return 'Container number must be 11 characters: 3 owner letters, a category letter, 6 digits and a check digit.';
  }
  if (!chars.slice(0, 4).every(isLetter)) {
    return 'The first four characters must be letters (three-letter owner code plus the category letter).';
  }
  if (!['U', 'J', 'Z'].includes(chars[3])) {
    return 'The fourth character must be the category letter U, J or Z.';
  }
  if (!chars.slice(4).every(isDigit)) {
    return 'Characters 5 to 11 must be digits (six-digit serial plus the check digit).';
  }

  const body = number.slice(0, BODY_LENGTH);
  const expected = computeCheckDigit(body);
  const actual = Number(chars[BODY_LENGTH]);

  return actual === expected ? null : `Check digit is wrong: ${body} should end in ${expected}, not ${actual}.`;
}

/** Reactive-forms validator. The error is `{ iso6346: '<message>' }`. Empty values are left to `Validators.required`. */
export function iso6346Validator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    if (!control.value) {
      return null;
    }
    const message = validateContainerNumber(control.value);
    return message ? { iso6346: message } : null;
  };
}
