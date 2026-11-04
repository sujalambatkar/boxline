import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { ContainerTracking } from '../../../core/models/tracking.models';
import { SharedModule } from '../../../shared/shared.module';
import { RecordMovementComponent } from './record-movement.component';

describe('RecordMovementComponent', () => {
  let fixture: ComponentFixture<RecordMovementComponent>;
  let component: RecordMovementComponent;
  let http: HttpTestingController;

  const container = (overrides: Partial<ContainerTracking> = {}): ContainerTracking => ({
    containerNumber: 'CSQU3054383', containerType: '20GP', current: null, events: [], allowedNextEvents: ['GateOutEmpty'], ...overrides,
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [RecordMovementComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RecordMovementComponent);
    component = fixture.componentInstance;
    component.container = container();
    component.ngOnChanges();
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  const fill = (overrides: Record<string, string> = {}) =>
    component.form.patchValue({ locationUnLocode: 'innsa', occurredAt: '2026-11-04T09:30', ...overrides });

  it('preselects the one event the state machine allows next', () => {
    expect(component.form.controls.eventType.value).toBe('GateOutEmpty');

    component.container = container({ allowedNextEvents: ['Stuffed'] });
    component.ngOnChanges();

    expect(component.form.controls.eventType.value).toBe('Stuffed');
  });

  it('requires a well-formed UN/LOCODE', () => {
    const location = component.form.controls.locationUnLocode;

    for (const bad of ['', '   ', 'IN', 'INNSAX', '1NNSA', 'IN1SA']) {
      location.setValue(bad);
      expect(location.valid).withContext(bad).toBeFalse();
    }
    for (const good of ['INNSA', 'nlrtm', 'GB4AB', ' INNSA ']) {
      location.setValue(good);
      expect(location.valid).withContext(good).toBeTrue();
    }
  });

  it('accepts an empty booking number but rejects a malformed one', () => {
    const booking = component.form.controls.bookingNumber;

    booking.setValue('');
    expect(booking.valid).toBeTrue();
    booking.setValue('BL-202611-0001');
    expect(booking.valid).toBeTrue();
    booking.setValue('bl-202611-0001');
    expect(booking.valid).toBeTrue();
    booking.setValue('BOOK-1');
    expect(booking.valid).toBeFalse();
  });

  it('does not submit an invalid form', () => {
    fill({ locationUnLocode: '' });

    component.save();

    http.expectNone(() => true);
    expect(component.form.touched).toBeTrue();
  });

  it('sends a normalised request with the local time converted to UTC', () => {
    fill({ bookingNumber: ' bl-202611-0001 ', remarks: ' Seal 123 ' });

    component.save();

    const request = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/movements'));
    expect(request.request.body).toEqual({
      containerNumber: 'CSQU3054383',
      eventType: 'GateOutEmpty',
      locationUnLocode: 'INNSA',
      occurredAt: new Date('2026-11-04T09:30').toISOString(),
      bookingNumber: 'BL-202611-0001',
      remarks: 'Seal 123',
    });
    request.flush({ id: 1, containerNumber: 'CSQU3054383', eventType: 'GateOutEmpty' });
  });

  it('leaves out the optional fields when they are blank', () => {
    fill();

    component.save();

    const body = http.expectOne(r => r.method === 'POST').request.body;
    expect('bookingNumber' in body).toBeFalse();
    expect('remarks' in body).toBeFalse();
  });

  it('announces success, clears the entry fields and asks for a refresh', () => {
    let emitted = 0;
    component.recorded.subscribe(() => emitted++);
    fill({ bookingNumber: 'BL-202611-0001', remarks: 'x' });

    component.save();
    http.expectOne(r => r.method === 'POST').flush({ id: 1, containerNumber: 'CSQU3054383', eventType: 'GateOutEmpty' });

    expect(emitted).toBe(1);
    expect(component.saving).toBeFalse();
    expect(component.form.controls.locationUnLocode.value).toBe('');
    expect(component.form.controls.bookingNumber.value).toBe('');
    expect(component.form.controls.remarks.value).toBe('');
  });

  it('lists the API\'s validation messages', () => {
    fill();

    component.save();
    http.expectOne(r => r.method === 'POST').flush(
      { errors: { OccurredAt: ['The event time cannot be in the future.'], BookingNumber: ['Booking does not exist.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(component.serverErrors).toEqual(['The event time cannot be in the future.', 'Booking does not exist.']);
    expect(component.saving).toBeFalse();
  });

  it('explains an out-of-order conflict', () => {
    fill();

    component.save();
    http.expectOne(r => r.method === 'POST').flush(
      { detail: 'Stuffed cannot follow GateOutEmpty. Expected: GateOutEmpty.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.serverErrors).toEqual(['Stuffed cannot follow GateOutEmpty. Expected: GateOutEmpty.']);
  });
});
