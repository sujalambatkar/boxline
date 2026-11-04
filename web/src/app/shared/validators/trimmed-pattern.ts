import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Like `Validators.pattern`, but ignores leading and trailing spaces (people paste values with them) and
 * leaves empty values to `Validators.required`. Error key: `pattern`.
 */
export function trimmedPattern(pattern: RegExp): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = typeof control.value === 'string' ? control.value.trim() : '';
    return value === '' || pattern.test(value) ? null : { pattern: { requiredPattern: String(pattern), actualValue: control.value } };
  };
}

/** Like `Validators.required`, but a value of only spaces counts as missing. Error key: `required`. */
export function notBlank(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  return value === null || value === undefined || (typeof value === 'string' && value.trim() === '') ? { required: true } : null;
}
