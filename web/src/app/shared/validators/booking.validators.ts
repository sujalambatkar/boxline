import { AbstractControl, FormArray, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Group-level: the port of discharge must differ from the port of loading. Error key: `samePorts`. */
export function differentPorts(polKey = 'polId', podKey = 'podId'): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const pol = group.get(polKey)?.value;
    const pod = group.get(podKey)?.value;
    return pol !== null && pol !== undefined && pol === pod ? { samePorts: true } : null;
  };
}

/** FormArray-level: each container type may appear on only one line. Error key: `duplicateTypes`. */
export function uniqueContainerTypes(key = 'containerTypeId'): ValidatorFn {
  return (array: AbstractControl): ValidationErrors | null => {
    const ids = (array as FormArray).controls
      .map(line => line.get(key)?.value)
      .filter((id): id is number => id !== null && id !== undefined);
    return new Set(ids).size !== ids.length ? { duplicateTypes: true } : null;
  };
}

/**
 * FormArray-level: at least `min` lines. Angular's built-in `minLength` ignores empty values, so it
 * cannot be used to require a non-empty array. Error key: `noLines`.
 */
export function minLines(min = 1): ValidatorFn {
  return (array: AbstractControl): ValidationErrors | null =>
    (array as FormArray).length < min ? { noLines: true } : null;
}
