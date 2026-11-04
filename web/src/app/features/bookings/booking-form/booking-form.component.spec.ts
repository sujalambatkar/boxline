import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';

import { Booking } from '../../../core/models/booking.models';
import { AuthService } from '../../../core/services/auth.service';
import { SharedModule } from '../../../shared/shared.module';
import { BookingFormComponent } from './booking-form.component';

/** Stands in for the real chat panel so these tests never open a connection. */
@Component({ selector: 'app-chat-panel', template: '' })
class ChatPanelStubComponent {
  @Input() bookingId!: number;
}

/** Stands in for the sailing picker; its own behaviour is tested separately. */
@Component({ selector: 'app-sailing-select', template: '' })
class SailingSelectStubComponent {
  @Input() polId: number | null = null;
  @Input() podId: number | null = null;
  @Input() value: number | null = null;
  @Input() current: unknown = null;
  @Input() disabled = false;
  @Output() valueChange = new EventEmitter<number | null>();
}

describe('BookingFormComponent', () => {
  const ports = [
    { id: 1, unLocode: 'INNSA', name: 'Nhava Sheva', country: 'IN' },
    { id: 2, unLocode: 'AEJEA', name: 'Jebel Ali', country: 'AE' },
  ];
  const types = [
    { id: 10, code: '20GP', lengthFt: 20, description: "20' general purpose" },
    { id: 11, code: '40HC', lengthFt: 40, description: "40' high cube" },
  ];

  const existing = (overrides: Partial<Booking> = {}): Booking => ({
    id: 5,
    bookingNumber: 'BL-202611-0005',
    shipperId: 1,
    shipperName: 'Acme Exports',
    pol: { id: 1, unLocode: 'INNSA', name: 'Nhava Sheva' },
    pod: { id: 2, unLocode: 'AEJEA', name: 'Jebel Ali' },
    voyageId: null,
    voyage: null,
    cargoDescription: 'Cotton',
    status: 'Draft',
    allowedTransitions: ['Confirmed'],
    createdAt: '2026-11-04T09:30:00Z',
    updatedAt: '2026-11-04T09:30:00Z',
    rowVersion: 'AAAA',
    totalContainers: 2,
    lines: [{ id: 1, containerTypeId: 10, containerTypeCode: '20GP', quantity: 2 }],
    ...overrides,
  });

  let fixture: ComponentFixture<BookingFormComponent>;
  let component: BookingFormComponent;
  let http: HttpTestingController;
  let router: Router;
  let dialogAnswer: boolean;

  /** Creates the component for a new booking (no id) or an existing one, answering the three loading calls. */
  function create(id: string | null = null, loaded?: Booking, ops = false): void {
    TestBed.configureTestingModule({
      declarations: [BookingFormComponent, ChatPanelStubComponent, SailingSelectStubComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [
        { provide: AuthService, useValue: { hasRole: (...roles: string[]) => ops && roles.includes('Ops') } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    spyOn(TestBed.inject(MatDialog), 'open').and.callFake(
      () => ({ afterClosed: () => of(dialogAnswer) }) as unknown as ReturnType<MatDialog['open']>,
    );

    fixture = TestBed.createComponent(BookingFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    http.expectOne(r => r.url.endsWith('/ports')).flush(ports);
    http.expectOne(r => r.url.endsWith('/container-types')).flush(types);
    if (id) {
      http.expectOne(r => r.url.endsWith(`/bookings/${id}`)).flush(loaded ?? existing());
    }
    fixture.detectChanges();
  }

  beforeEach(() => (dialogAnswer = true));
  afterEach(() => http.verify());

  describe('new booking', () => {
    beforeEach(() => create());

    it('sends the sailing the user picked and marks the form as changed', () => {
      component.form.patchValue({ polId: 1, podId: 2, cargoDescription: 'x' });
      component.lines.at(0).patchValue({ containerTypeId: 10, quantity: 1 });
      component.form.markAsPristine();

      component.setVoyage(42);
      expect(component.form.dirty).toBeTrue();
      component.save();

      expect(http.expectOne(r => r.method === 'POST').request.body.voyageId).toBe(42);
    });

    it('offers the sailing picker for a draft', () => {
      expect(fixture.nativeElement.querySelector('app-sailing-select')).not.toBeNull();
    });

    it('has no conversation until the booking exists', () => {
      expect(fixture.nativeElement.querySelector('app-chat-panel')).toBeNull();
    });

    it('starts with one empty container line and is not yet valid', () => {
      expect(component.lines.length).toBe(1);
      expect(component.form.invalid).toBeTrue();
      expect(component.isNew).toBeTrue();
    });

    it('adds and removes container lines and totals the quantities', () => {
      component.addLine(10, 4);
      component.addLine(11, 6);
      expect(component.lines.length).toBe(3);
      expect(component.totalContainers).toBe(11);

      component.removeLine(0);
      expect(component.lines.length).toBe(2);
      expect(component.totalContainers).toBe(10);
    });

    it('requires at least one line', () => {
      component.removeLine(0);

      expect(component.lines.hasError('noLines')).toBeTrue();
      expect(component.form.invalid).toBeTrue();
    });

    it('rejects quantities outside 1 to 50 and fractions', () => {
      const quantity = component.lines.at(0).controls.quantity;

      for (const bad of [0, -2, 51, 1.5]) {
        quantity.setValue(bad);
        expect(quantity.valid).withContext(String(bad)).toBeFalse();
      }
      for (const good of [1, 25, 50]) {
        quantity.setValue(good);
        expect(quantity.valid).withContext(String(good)).toBeTrue();
      }
    });

    it('rejects the same port on both ends', () => {
      component.form.patchValue({ polId: 1, podId: 1 });

      expect(component.form.hasError('samePorts')).toBeTrue();
    });

    it('rejects the same container type on two lines', () => {
      component.addLine(10, 1);
      component.lines.at(0).controls.containerTypeId.setValue(10);

      expect(component.lines.hasError('duplicateTypes')).toBeTrue();
    });

    it('shows the "add a line" message once the user has removed the last line', () => {
      component.removeLine(0);
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Add at least one container line.');
    });

    it('does not call the API while invalid', () => {
      component.save();

      http.expectNone(r => r.method === 'POST');
      expect(component.form.touched).toBeTrue();
    });

    it('creates the draft and opens it', () => {
      component.form.patchValue({ polId: 1, podId: 2, cargoDescription: '  Cotton garments  ' });
      component.lines.at(0).patchValue({ containerTypeId: 10, quantity: 3 });

      component.save();

      const request = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/bookings'));
      expect(request.request.body).toEqual({
        polId: 1,
        podId: 2,
        cargoDescription: 'Cotton garments',
        voyageId: null,
        lines: [{ containerTypeId: 10, quantity: 3 }],
      });
      request.flush(existing({ id: 9, bookingNumber: 'BL-202611-0009' }));

      expect(router.navigate).toHaveBeenCalledWith(['/bookings', 9], { replaceUrl: true });
      expect(component.booking?.bookingNumber).toBe('BL-202611-0009');
    });

    it('lists the messages when the API rejects the booking', () => {
      component.form.patchValue({ polId: 1, podId: 2, cargoDescription: 'x' });
      component.lines.at(0).patchValue({ containerTypeId: 10, quantity: 3 });

      component.save();
      http.expectOne(r => r.method === 'POST').flush(
        { errors: { PodId: ['Port of discharge does not exist.'], Lines: ['One or more container types do not exist.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

      expect(component.serverErrors).toEqual([
        'Port of discharge does not exist.',
        'One or more container types do not exist.',
      ]);
      expect(component.saving).toBeFalse();
    });
  });

  describe('existing draft', () => {
    beforeEach(() => create('5'));

    it('fills the form from the booking and starts clean', () => {
      expect(component.form.getRawValue().polId).toBe(1);
      expect(component.form.getRawValue().cargoDescription).toBe('Cotton');
      expect(component.lines.length).toBe(1);
      expect(component.form.pristine).toBeTrue();
      expect(component.canEdit).toBeTrue();
      expect(component.canConfirm).toBeTrue();
      expect(component.canCancel).toBeFalse();
    });

    it('shows the conversation for the booking and links to its tracking', () => {
      const panel = fixture.nativeElement.querySelector('app-chat-panel');
      expect(panel).not.toBeNull();
      expect(fixture.nativeElement.textContent).toContain('Track this booking');
      expect(fixture.nativeElement.querySelector('a[href*="/tracking"]')?.getAttribute('href')).toContain('BL-202611-0005');
    });

    it('sends the row version with a save and adopts the new one', () => {
      component.form.patchValue({ cargoDescription: 'Cotton, rolled' });
      component.form.markAsDirty();

      component.save();

      const request = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/bookings/5'));
      expect(request.request.body.rowVersion).toBe('AAAA');
      request.flush(existing({ cargoDescription: 'Cotton, rolled', rowVersion: 'AAAB' }));
      expect(component.booking?.rowVersion).toBe('AAAB');
      expect(component.form.pristine).toBeTrue();
    });

    it('shows a conflict with a reload option when someone else changed the booking', () => {
      component.save();
      http.expectOne(r => r.method === 'PUT').flush(
        { detail: 'This booking was changed by someone else. Reload it and try again.' },
        { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();

      expect(component.conflictMessage).toContain('changed by someone else');

      component.reload();
      http.expectOne(r => r.method === 'GET' && r.url.endsWith('/bookings/5')).flush(existing({ rowVersion: 'ZZZZ' }));
      expect(component.conflictMessage).toBeNull();
      expect(component.booking?.rowVersion).toBe('ZZZZ');
    });

    it('confirms after the user agrees and then becomes read-only', () => {
      component.confirm();

      http.expectOne(r => r.method === 'POST' && r.url.endsWith('/bookings/5/confirm')).flush(
        existing({ status: 'Confirmed', allowedTransitions: ['Cancelled'] }),
      );

      expect(component.canEdit).toBeFalse();
      expect(component.canConfirm).toBeFalse();
      expect(component.canCancel).toBeTrue();
      expect(component.form.disabled).toBeTrue();
    });

    it('does nothing when the user declines the confirmation', () => {
      dialogAnswer = false;

      component.confirm();

      http.expectNone(r => r.url.endsWith('/confirm'));
      expect(component.booking?.status).toBe('Draft');
    });
  });

  describe('confirmed booking', () => {
    beforeEach(() => create('5', existing({ status: 'Confirmed', allowedTransitions: ['Cancelled'] })));

    it('is read-only and offers only cancel', () => {
      expect(component.canEdit).toBeFalse();
      expect(component.form.disabled).toBeTrue();
      expect(component.canConfirm).toBeFalse();
      expect(component.canCancel).toBeTrue();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('can no longer be edited');
    });

    it('cancels after the user agrees', () => {
      component.cancelBooking();

      http.expectOne(r => r.method === 'POST' && r.url.endsWith('/bookings/5/cancel')).flush(
        existing({ status: 'Cancelled', allowedTransitions: [] }),
      );

      expect(component.booking?.status).toBe('Cancelled');
      expect(component.canCancel).toBeFalse();
    });
  });

  describe('sailing on an existing booking', () => {
    const voyage = { id: 9, voyageNumber: 'BXL2612E', vesselName: 'MV Aurora', departure: '2026-12-01T08:00:00Z', arrival: '2026-12-20T08:00:00Z' };

    it('shows the sailing a booking is on, with its dates', () => {
      create('5', existing({ voyageId: 9, voyage }));

      const text = (fixture.nativeElement.querySelector('[data-testid="voyage"]') as HTMLElement).textContent!;
      expect(text).toContain('MV Aurora · BXL2612E');
      expect(text).toContain('departs 01 Dec 2026');
      expect(text).toContain('arrives 20 Dec 2026');
      expect(component.form.controls.voyageId.value).toBe(9);
    });

    it('keeps the sailing when a draft is saved unchanged', () => {
      create('5', existing({ voyageId: 9, voyage }));
      component.form.markAsDirty();

      component.save();

      expect(http.expectOne(r => r.method === 'PUT').request.body.voyageId).toBe(9);
    });

    it('can remove the sailing from a draft', () => {
      create('5', existing({ voyageId: 9, voyage }));

      component.setVoyage(null);
      component.save();

      expect(http.expectOne(r => r.method === 'PUT').request.body.voyageId).toBeNull();
    });

    it('does not offer the assign panel to a shipper', () => {
      create('5', existing({ status: 'Confirmed', allowedTransitions: ['Cancelled'] }));

      expect(component.canAssignVoyage).toBeFalse();
      expect(fixture.nativeElement.querySelector('.assign')).toBeNull();
    });

    it('does not offer the assign panel to Ops for a draft or a cancelled booking', () => {
      create('5', existing(), true);
      expect(component.canAssignVoyage).toBeFalse();
    });

    describe('as Ops on a confirmed booking', () => {
      beforeEach(() => create('5', existing({ status: 'Confirmed', allowedTransitions: ['Cancelled'] }), true));

      it('shows the assign panel and the booking itself stays read-only', () => {
        expect(component.canAssignVoyage).toBeTrue();
        expect(component.canEdit).toBeFalse();
        expect(fixture.nativeElement.querySelector('.assign')).not.toBeNull();
        expect(component.assignChanged).toBeFalse();
      });

      it('assigns the chosen sailing and shows it', () => {
        component.assignVoyageId = 9;
        expect(component.assignChanged).toBeTrue();

        component.assignVoyage();

        const request = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/bookings/5/voyage'));
        expect(request.request.body).toEqual({ voyageId: 9 });
        request.flush(existing({ status: 'Confirmed', allowedTransitions: ['Cancelled'], voyageId: 9, voyage }));

        expect(component.booking?.voyage?.voyageNumber).toBe('BXL2612E');
        expect(component.assignChanged).toBeFalse();
        expect(component.assigning).toBeFalse();
      });

      it('can take the booking off its sailing', () => {
        component.assignVoyageId = null;
        component.booking = { ...component.booking!, voyageId: 9, voyage };
        expect(component.assignChanged).toBeTrue();

        component.assignVoyage();

        expect(http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/voyage')).request.body).toEqual({ voyageId: null });
      });

      it('explains why a sailing was refused', () => {
        component.assignVoyageId = 9;

        component.assignVoyage();
        http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/voyage')).flush(
          { errors: { VoyageId: ['Voyage BXL2612E does not sail from INNSA to AEJEA in that order.'] } },
          { status: 400, statusText: 'Bad Request' },
        );

        expect(component.serverErrors).toEqual(['Voyage BXL2612E does not sail from INNSA to AEJEA in that order.']);
        expect(component.assigning).toBeFalse();
      });
    });
  });

  it('goes back to the list when the booking does not exist', () => {
    TestBed.configureTestingModule({
      declarations: [BookingFormComponent, ChatPanelStubComponent, SailingSelectStubComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [
        { provide: AuthService, useValue: { hasRole: () => false } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '404' }) } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(BookingFormComponent);
    fixture.detectChanges();

    http.expectOne(r => r.url.endsWith('/ports')).flush(ports);
    http.expectOne(r => r.url.endsWith('/container-types')).flush(types);
    http.expectOne(r => r.url.endsWith('/bookings/404')).flush({}, { status: 404, statusText: 'Not Found' });

    expect(router.navigate).toHaveBeenCalledWith(['/bookings']);
  });
});
