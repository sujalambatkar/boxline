import { FormArray, FormControl, FormGroup } from '@angular/forms';

import { SlabRow, slabStartDays, tariffValidator, validateSlabs } from './tariff';

describe('tariff validation', () => {
  const valid = (): SlabRow[] => [
    { toDay: 10, ratePerDayUsd: 40 },
    { toDay: 15, ratePerDayUsd: 80 },
    { toDay: null, ratePerDayUsd: 120 },
  ];

  it('derives each slab\'s first day from the free days and the previous slab', () => {
    expect(slabStartDays(5, valid())).toEqual([6, 11, 16]);
    expect(slabStartDays(0, [{ toDay: 3, ratePerDayUsd: 10 }, { toDay: null, ratePerDayUsd: 25 }])).toEqual([1, 4]);
    expect(slabStartDays(5, [])).toEqual([]);
  });

  it('accepts a well-formed tariff, with one open-ended slab or several', () => {
    expect(validateSlabs(5, valid())).toEqual([]);
    expect(validateSlabs(0, [{ toDay: null, ratePerDayUsd: 25 }])).toEqual([]);
  });

  it('needs at least one slab and at most ten', () => {
    expect(validateSlabs(5, [])).toContain('Add at least one slab.');

    const many: SlabRow[] = Array.from({ length: 11 }, (_, i) => ({ toDay: i === 10 ? null : 6 + i, ratePerDayUsd: 10 + i }));
    expect(validateSlabs(5, many)).toContain('A tariff can have at most 10 slabs.');
  });

  it('requires the last slab to be open-ended, and only the last', () => {
    expect(validateSlabs(5, [{ toDay: 10, ratePerDayUsd: 40 }, { toDay: 20, ratePerDayUsd: 80 }]))
      .toContain('The last slab must be open-ended so that long stays are always priced.');
    expect(validateSlabs(5, [{ toDay: null, ratePerDayUsd: 40 }, { toDay: null, ratePerDayUsd: 80 }]))
      .toContain('Only the last slab can be open-ended (slab 1 has no end day).');
  });

  it('rejects a slab that ends before it starts', () => {
    const rows = valid();
    rows[0].toDay = 5;

    expect(validateSlabs(5, rows)).toContain('Slab 1: the end day cannot be before the start day (day 6).');
  });

  it('rejects an end day that would make a later slab end before it starts', () => {
    const rows = valid();
    rows[1].toDay = 10;

    expect(validateSlabs(5, rows)).toContain('Slab 2: the end day cannot be before the start day (day 11).');
  });

  it('rejects rates that are missing, zero, too high or have more than two decimals', () => {
    for (const rate of [null, 0, -5, 10_000.01, 12.345]) {
      const rows = valid();
      rows[0].ratePerDayUsd = rate;
      expect(validateSlabs(5, rows).some(p => p.startsWith('Slab 1: the daily rate'))).withContext(String(rate)).toBeTrue();
    }

    const rows = valid();
    rows[0].ratePerDayUsd = 10_000;
    rows[1].ratePerDayUsd = 33.33;
    expect(validateSlabs(5, rows)).toEqual([]);
  });

  describe('tariffValidator', () => {
    const form = (freeDays: unknown, validFrom: string, validTo: string, rows: SlabRow[]) =>
      new FormGroup({
        freeDays: new FormControl(freeDays),
        validFrom: new FormControl(validFrom),
        validTo: new FormControl(validTo),
        slabs: new FormArray(
          rows.map(r => new FormGroup({ toDay: new FormControl(r.toDay), ratePerDayUsd: new FormControl(r.ratePerDayUsd) })),
        ),
      });

    it('passes a valid tariff', () => {
      expect(tariffValidator()(form(5, '2026-01-01', '', valid()))).toBeNull();
      expect(tariffValidator()(form(5, '2026-01-01', '2026-12-31', valid()))).toBeNull();
    });

    it('flags free days that are out of range or not whole', () => {
      for (const bad of [-1, 61, 2.5, 'abc']) {
        expect(tariffValidator()(form(bad, '2026-01-01', '', valid()))?.['freeDays']).withContext(String(bad)).toContain('between 0 and 60');
      }
    });

    it('flags an end date before the start date', () => {
      expect(tariffValidator()(form(5, '2026-06-01', '2026-05-31', valid()))?.['validTo']).toContain('cannot be before the start date');
    });

    it('reports slab problems under the slabs key, treating blank fields as missing', () => {
      const errors = tariffValidator()(form(5, '2026-01-01', '', [{ toDay: null, ratePerDayUsd: null }, { toDay: null, ratePerDayUsd: 80 }]));

      expect(errors?.['slabs']).toContain('Only the last slab can be open-ended (slab 1 has no end day).');
      expect((errors?.['slabs'] as string[]).some(p => p.includes('daily rate'))).toBeTrue();
    });
  });
});
