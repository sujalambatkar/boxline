import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, ParamMap } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { BehaviorSubject } from 'rxjs';

import { DetentionResult } from '../../core/models/detention.models';
import { SharedModule } from '../../shared/shared.module';
import { DetentionResultComponent } from './detention-result/detention-result.component';
import { DetentionComponent } from './detention.component';

describe('DetentionComponent', () => {
  let fixture: ComponentFixture<DetentionComponent>;
  let component: DetentionComponent;
  let http: HttpTestingController;
  let params$: BehaviorSubject<ParamMap>;

  const el = () => fixture.nativeElement as HTMLElement;

  const result = (overrides: Partial<DetentionResult> = {}): DetentionResult => ({
    containerType: '20GP', containerNumber: 'CSQU3054383', bookingNumber: null, startedBy: 'GateOutFull',
    tariff: { id: 1, containerTypeId: 1, containerType: '20GP', freeDays: 3, validFrom: '2026-01-01', validTo: null, slabs: [{ fromDay: 4, toDay: null, ratePerDayUsd: 10 }] },
    outDate: '2026-11-02', returnDate: '2026-11-12', isOngoing: false, countWeekendsAsChargeable: true,
    totalDays: 11, countedDays: 11, weekendDaysSkipped: 0, freeDaysUsed: 3, chargeableDays: 8,
    slabs: [{ fromDay: 4, toDay: null, days: 8, ratePerDayUsd: 10, amountUsd: 80 }], totalAmountUsd: 80, currency: 'USD',
    ...overrides,
  });

  async function create(query: Record<string, string> = {}): Promise<void> {
    params$ = new BehaviorSubject(convertToParamMap(query));
    await TestBed.configureTestingModule({
      declarations: [DetentionComponent, DetentionResultComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [{ provide: ActivatedRoute, useValue: { queryParamMap: params$ } }],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DetentionComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne(r => r.url.endsWith('/container-types')).flush([
      { id: 1, code: '20GP', lengthFt: 20, description: "20' general purpose" },
      { id: 2, code: '40HC', lengthFt: 40, description: "40' high cube" },
    ]);
  }

  afterEach(() => http.verify());

  describe('from a container', () => {
    beforeEach(() => create());

    it('validates the number as ISO 6346 before calling the API', () => {
      component.containerForm.patchValue({ containerNumber: 'CSQU3054380' });

      component.priceContainer();

      http.expectNone(() => true);
      expect(component.containerNumberError).toContain('Check digit is wrong');
    });

    it('requires a number', () => {
      component.priceContainer();

      http.expectNone(() => true);
      expect(component.containerNumberError).toBe('Enter a container number.');
    });

    it('prices the container with the weekend choice and shows the breakdown', () => {
      component.containerForm.patchValue({ containerNumber: ' csqu 305438-3 ', countWeekends: false });

      component.priceContainer();

      const call = http.expectOne(r => r.url.endsWith('/detention/containers/CSQU3054383'));
      expect(call.request.params.get('countWeekendsAsChargeable')).toBe('false');
      call.flush(result());
      fixture.detectChanges();

      expect(el().querySelector('app-detention-result')).not.toBeNull();
      expect(el().textContent).toContain('$80.00');
      expect(component.loading).toBeFalse();
    });

    it('limits the clock to a booking when one is entered, upper-cased', () => {
      component.containerForm.patchValue({ containerNumber: 'CSQU3054383', bookingNumber: ' bl-202611-0001 ' });

      component.priceContainer();

      expect(http.expectOne(r => r.url.includes('/detention/containers/')).request.params.get('bookingNumber')).toBe('BL-202611-0001');
    });

    it('rejects a malformed booking number', () => {
      component.containerForm.patchValue({ containerNumber: 'CSQU3054383', bookingNumber: 'BOOK-1' });

      component.priceContainer();

      http.expectNone(() => true);
      expect(component.containerForm.controls.bookingNumber.invalid).toBeTrue();
    });

    it('explains why no clock has started', () => {
      component.containerForm.patchValue({ containerNumber: 'CSQU3054383' });

      component.priceContainer();
      http.expectOne(r => r.url.includes('/detention/containers/')).flush(
        { detail: 'Container CSQU3054383 has not been gated out full or delivered yet, so no detention clock has started.' },
        { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();

      expect(component.result).toBeNull();
      expect(el().textContent).toContain('no detention clock has started');
    });

    it('reports a container that cannot be found, and a service that is down', () => {
      component.containerForm.patchValue({ containerNumber: 'MSKU1234565' });

      component.priceContainer();
      http.expectOne(r => r.url.includes('/detention/containers/')).flush({ detail: 'Container MSKU1234565 was not found.' }, { status: 404, statusText: 'Not Found' });
      expect(component.message).toBe('Container MSKU1234565 was not found.');

      component.priceContainer();
      http.expectOne(r => r.url.includes('/detention/containers/')).flush({}, { status: 500, statusText: 'Server Error' });
      expect(component.message).toContain('unavailable right now');
    });
  });

  describe('what-if', () => {
    beforeEach(async () => {
      await create();
      component.onTabChange(1);
      fixture.detectChanges();
    });

    it('needs a type and both dates', () => {
      component.priceWhatIf();

      http.expectNone(() => true);
      expect(component.whatIfForm.controls.containerTypeId.invalid).toBeTrue();
      expect(component.whatIfForm.controls.outDate.invalid).toBeTrue();
    });

    it('rejects a return date before the out date', () => {
      component.whatIfForm.patchValue({ containerTypeId: 2, outDate: '2026-11-10', returnDate: '2026-11-09' });
      fixture.detectChanges();

      component.priceWhatIf();

      http.expectNone(() => true);
      expect(component.whatIfForm.hasError('returnBeforeOut')).toBeTrue();
      expect(el().textContent).toContain('The return date cannot be before the out date.');
    });

    it('accepts the same day for out and return', () => {
      component.whatIfForm.patchValue({ containerTypeId: 2, outDate: '2026-11-10', returnDate: '2026-11-10' });

      expect(component.whatIfForm.valid).toBeTrue();
    });

    it('sends the period and weekend choice, and shows the result', () => {
      component.whatIfForm.patchValue({ containerTypeId: 2, outDate: '2026-11-06', returnDate: '2026-11-16', countWeekends: false });

      component.priceWhatIf();

      const call = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/detention/calculate'));
      expect(call.request.body).toEqual({ containerTypeId: 2, outDate: '2026-11-06', returnDate: '2026-11-16', countWeekendsAsChargeable: false });
      call.flush(result({ containerNumber: null, containerType: '40HC', startedBy: null }));
      fixture.detectChanges();

      expect(el().textContent).toContain('40HC (what-if)');
    });

    it('shows the API\'s validation message, and a missing tariff', () => {
      component.whatIfForm.patchValue({ containerTypeId: 2, outDate: '2026-11-06', returnDate: '2026-11-16' });

      component.priceWhatIf();
      http.expectOne(r => r.url.endsWith('/detention/calculate')).flush(
        { errors: { ReturnDate: ['Return date cannot be before the out date.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      expect(component.message).toBe('Return date cannot be before the out date.');

      component.priceWhatIf();
      http.expectOne(r => r.url.endsWith('/detention/calculate')).flush(
        { detail: 'No detention tariff is valid for 40HC on 2026-11-06.' },
        { status: 404, statusText: 'Not Found' },
      );
      expect(component.message).toBe('No detention tariff is valid for 40HC on 2026-11-06.');
    });
  });

  it('clears the previous answer when the tab changes, so the two modes are never mixed', async () => {
    await create();
    component.containerForm.patchValue({ containerNumber: 'CSQU3054383' });
    component.priceContainer();
    http.expectOne(r => r.url.includes('/detention/containers/')).flush(result());
    expect(component.result).not.toBeNull();

    component.onTabChange(1);

    expect(component.result).toBeNull();
    expect(component.message).toBeNull();
  });

  it('prices straight away when opened from a container link', async () => {
    await create({ container: 'csqu3054383' });

    const call = http.expectOne(r => r.url.endsWith('/detention/containers/CSQU3054383'));
    call.flush(result());

    expect(component.containerForm.controls.containerNumber.value).toBe('csqu3054383');
    expect(component.tabIndex).toBe(0);
    expect(component.result?.containerNumber).toBe('CSQU3054383');
  });
});
