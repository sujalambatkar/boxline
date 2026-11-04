import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { DetentionResult } from '../../../core/models/detention.models';
import { SharedModule } from '../../../shared/shared.module';
import { DetentionResultComponent } from './detention-result.component';

describe('DetentionResultComponent', () => {
  let fixture: ComponentFixture<DetentionResultComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = (id: string) => el().querySelector(`[data-testid="${id}"]`)?.textContent?.replace(/\s+/g, ' ').trim();

  const result = (overrides: Partial<DetentionResult> = {}): DetentionResult => ({
    containerType: '40HC',
    containerNumber: null,
    bookingNumber: null,
    startedBy: null,
    tariff: {
      id: 1, containerTypeId: 3, containerType: '40HC', freeDays: 5, validFrom: '2026-01-01', validTo: null,
      slabs: [
        { fromDay: 6, toDay: 10, ratePerDayUsd: 40 },
        { fromDay: 11, toDay: 15, ratePerDayUsd: 80 },
        { fromDay: 16, toDay: null, ratePerDayUsd: 120 },
      ],
    },
    outDate: '2026-11-02',
    returnDate: '2026-11-17',
    isOngoing: false,
    countWeekendsAsChargeable: true,
    totalDays: 16,
    countedDays: 16,
    weekendDaysSkipped: 0,
    freeDaysUsed: 5,
    chargeableDays: 11,
    slabs: [
      { fromDay: 6, toDay: 10, days: 5, ratePerDayUsd: 40, amountUsd: 200 },
      { fromDay: 11, toDay: 15, days: 5, ratePerDayUsd: 80, amountUsd: 400 },
      { fromDay: 16, toDay: null, days: 1, ratePerDayUsd: 120, amountUsd: 120 },
    ],
    totalAmountUsd: 720,
    currency: 'USD',
    ...overrides,
  });

  function show(value: DetentionResult): void {
    fixture = TestBed.createComponent(DetentionResultComponent);
    fixture.componentInstance.result = value;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [DetentionResultComponent],
      imports: [SharedModule, NoopAnimationsModule],
    }).compileComponents();
  });

  it('shows one row per slab with day range, days charged, rate and amount', () => {
    show(result());

    const rows = Array.from(el().querySelectorAll('[data-testid="slab-row"]')).map(r =>
      Array.from(r.querySelectorAll('td')).map(td => td.textContent?.trim()),
    );
    expect(rows).toEqual([
      ['6–10', '5', '$40.00', '$200.00'],
      ['11–15', '5', '$80.00', '$400.00'],
      ['16+', '1', '$120.00', '$120.00'],
    ]);
    expect(text('total')).toBe('$720.00');
  });

  it('shows the key figures', () => {
    show(result());

    expect(text('total-days')).toBe('16');
    expect(text('counted-days')).toBe('16');
    expect(text('free-days')).toBe('5 of 5');
    expect(text('chargeable-days')).toBe('11');
    expect(el().querySelector('[data-testid="weekend-days"]')).toBeNull();
  });

  it('says there is no charge when the period fits inside the free days', () => {
    show(result({ slabs: [], totalAmountUsd: 0, chargeableDays: 0, totalDays: 3, countedDays: 3, freeDaysUsed: 3, returnDate: '2026-11-04' }));

    expect(el().textContent).toContain('No charge: the period fits within the 5 free days.');
    expect(el().querySelectorAll('[data-testid="slab-row"]').length).toBe(0);
    expect(text('total')).toBe('$0.00');
  });

  it('explains skipped weekends and shows how many were left out', () => {
    show(result({ countWeekendsAsChargeable: false, weekendDaysSkipped: 4, countedDays: 12, totalDays: 16 }));

    expect(text('weekend-days')).toBe('4');
    expect(el().textContent).toContain('Saturdays and Sundays were not counted');
  });

  it('warns that a running charge keeps growing and labels the end date as today', () => {
    show(result({ isOngoing: true }));

    expect(el().textContent).toContain('has not been returned yet');
    expect(el().querySelector('.period')?.textContent).toContain('Today');
    expect(el().querySelector('tfoot')?.textContent).toContain('Total so far');
  });

  it('titles a container result with its number, type, booking and what started the clock', () => {
    show(result({ containerNumber: 'CSQU3054383', containerType: '20GP', bookingNumber: 'BL-202611-0001', startedBy: 'GateOutFull' }));

    expect(el().querySelector('h2')?.textContent).toContain('CSQU3054383');
    expect(el().textContent).toContain('20GP');
    expect(el().textContent).toContain('BL-202611-0001');
    expect(el().textContent).toContain('clock started at gate out full');
  });

  it('titles a what-if with the container type', () => {
    show(result());

    expect(el().querySelector('h2')?.textContent).toContain('40HC (what-if)');
  });
});
