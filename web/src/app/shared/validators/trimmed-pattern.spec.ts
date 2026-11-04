import { FormControl } from '@angular/forms';

import { notBlank, trimmedPattern } from './trimmed-pattern';

describe('trimmedPattern', () => {
  const validate = (value: unknown) => trimmedPattern(/^[A-Z]{2}\d{3}$/)(new FormControl(value));

  it('accepts a matching value, with or without surrounding spaces', () => {
    expect(validate('AB123')).toBeNull();
    expect(validate('  AB123 ')).toBeNull();
  });

  it('rejects a value that does not match', () => {
    expect(validate('ab123')?.['pattern']).toBeDefined();
    expect(validate('AB 123')?.['pattern']).toBeDefined();
  });

  it('leaves empty and whitespace-only values to the required validator', () => {
    expect(validate('')).toBeNull();
    expect(validate(null)).toBeNull();
    expect(validate('   ')).toBeNull();
  });
});

describe('notBlank', () => {
  it('flags empty, missing and whitespace-only values as required', () => {
    for (const value of ['', '   ', null, undefined]) {
      expect(notBlank(new FormControl(value))).withContext(String(value)).toEqual({ required: true });
    }
  });

  it('accepts any real text', () => {
    expect(notBlank(new FormControl('x'))).toBeNull();
    expect(notBlank(new FormControl(' x '))).toBeNull();
  });
});
