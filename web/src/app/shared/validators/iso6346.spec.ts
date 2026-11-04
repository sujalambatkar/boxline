import { FormControl } from '@angular/forms';

import {
  computeCheckDigit,
  iso6346Validator,
  normalizeContainerNumber,
  validateContainerNumber,
} from './iso6346';

// Reference values match the backend tests (worked by hand and cross-checked independently):
//   CSQU305438 -> sum 6185, 6185 mod 11 = 3
//   MSKU123456 -> sum 5560, 5560 mod 11 = 5
//   BXLU000003 -> sum 1968, 1968 mod 11 = 10, which becomes 0
describe('ISO 6346 validation', () => {
  it('accepts known valid numbers', () => {
    for (const number of ['CSQU3054383', 'MSKU1234565', 'BXLU0000030']) {
      expect(validateContainerNumber(number)).withContext(number).toBeNull();
    }
  });

  it('computes check digits, turning a remainder of 10 into 0', () => {
    expect(computeCheckDigit('CSQU305438')).toBe(3);
    expect(computeCheckDigit('MSKU123456')).toBe(5);
    expect(computeCheckDigit('BXLU000003')).toBe(0);
  });

  it('returns null for a malformed check-digit body', () => {
    expect(computeCheckDigit('CSQU30543')).toBeNull();
    expect(computeCheckDigit('CS1U305438')).toBeNull();
  });

  it('accepts lowercase and formatted input', () => {
    expect(validateContainerNumber('csqu 305438-3')).toBeNull();
    expect(normalizeContainerNumber(' csqu 305438-3 ')).toBe('CSQU3054383');
  });

  it('rejects a wrong check digit and says what it should be', () => {
    expect(validateContainerNumber('CSQU3054380')).toBe(
      'Check digit is wrong: CSQU305438 should end in 3, not 0.',
    );
  });

  it('rejects wrong length, bad category letter and misplaced characters', () => {
    expect(validateContainerNumber('')).toBe('Container number is required.');
    expect(validateContainerNumber('CSQU305438')).toContain('11 characters');
    expect(validateContainerNumber('CSQA3054383')).toContain('category letter U, J or Z');
    expect(validateContainerNumber('CS1U3054383')).toContain('first four characters must be letters');
    expect(validateContainerNumber('CSQU30543X3')).toContain('must be digits');
  });

  describe('iso6346Validator', () => {
    it('ignores empty values so that required can report them', () => {
      expect(iso6346Validator()(new FormControl(''))).toBeNull();
    });

    it('exposes the message under the iso6346 error key', () => {
      const errors = iso6346Validator()(new FormControl('CSQU3054380'));

      expect(errors?.['iso6346']).toContain('Check digit is wrong');
    });

    it('passes valid numbers', () => {
      expect(iso6346Validator()(new FormControl('CSQU3054383'))).toBeNull();
    });
  });
});
