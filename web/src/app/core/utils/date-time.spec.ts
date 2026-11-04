import { localInputToIso, toLocalInputValue } from './date-time';

describe('date-time helpers', () => {
  it('formats local time for a datetime-local input, padding every part', () => {
    expect(toLocalInputValue(new Date(2026, 0, 5, 7, 4))).toBe('2026-01-05T07:04');
    expect(toLocalInputValue(new Date(2026, 10, 24, 18, 45))).toBe('2026-11-24T18:45');
  });

  it('converts local input back to the same instant in UTC', () => {
    const local = new Date(2026, 10, 4, 9, 30);

    expect(localInputToIso(toLocalInputValue(local))).toBe(local.toISOString());
  });

  it('round-trips through the input format', () => {
    const iso = localInputToIso('2026-11-04T09:30');

    expect(toLocalInputValue(new Date(iso))).toBe('2026-11-04T09:30');
  });
});
