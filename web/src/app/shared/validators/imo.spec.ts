import { FormControl } from '@angular/forms';

import { computeImoCheckDigit, imoValidator, normalizeImoNumber, validateImoNumber } from './imo';

// Reference values match the backend tests (worked by hand and cross-checked independently):
//   907472 -> 63 + 0 + 35 + 16 + 21 + 4 = 139 -> 9      932148 -> 123 -> 3      900012 -> 70 -> 0
describe('IMO number validation', () => {
  it('accepts known valid numbers, including a check digit of 0', () => {
    for (const imo of ['9074729', '9321483', '9000120']) {
      expect(validateImoNumber(imo)).withContext(imo).toBeNull();
    }
  });

  it('computes check digits', () => {
    expect(computeImoCheckDigit('907472')).toBe(9);
    expect(computeImoCheckDigit('932148')).toBe(3);
    expect(computeImoCheckDigit('900012')).toBe(0);
    expect(computeImoCheckDigit('90747')).toBeNull();
    expect(computeImoCheckDigit('90747X')).toBeNull();
  });

  it('says what the check digit should be when it is wrong', () => {
    expect(validateImoNumber('9074728')).toBe('Check digit is wrong: 907472 should end in 9, not 8.');
    expect(validateImoNumber('9000121')).toBe('Check digit is wrong: 900012 should end in 0, not 1.');
  });

  it('rejects the wrong length and non-digits', () => {
    for (const bad of ['907472', '90747290', '90747AB', '9O74729']) {
      expect(validateImoNumber(bad)).withContext(bad).toBe('IMO number must be exactly 7 digits.');
    }
    expect(validateImoNumber('')).toBe('IMO number is required.');
    expect(validateImoNumber(null)).toBe('IMO number is required.');
  });

  it('accepts an IMO prefix, spaces and hyphens', () => {
    for (const input of ['IMO 9074729', 'imo9074729', 'IMO-9074-729', ' 9074 729 ']) {
      expect(normalizeImoNumber(input)).withContext(input).toBe('9074729');
      expect(validateImoNumber(input)).withContext(input).toBeNull();
    }
  });

  describe('imoValidator', () => {
    it('leaves empty values to required', () => {
      expect(imoValidator()(new FormControl(''))).toBeNull();
    });

    it('reports the message under the imo error key', () => {
      expect(imoValidator()(new FormControl('9074728'))?.['imo']).toContain('Check digit is wrong');
      expect(imoValidator()(new FormControl('9074729'))).toBeNull();
    });
  });
});
