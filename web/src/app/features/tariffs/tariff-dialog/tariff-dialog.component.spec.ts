import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { Tariff } from '../../../core/models/detention.models';
import { SharedModule } from '../../../shared/shared.module';
import { TariffDialogComponent } from './tariff-dialog.component';

describe('TariffDialogComponent', () => {
  const types = [
    { id: 1, code: '20GP', lengthFt: 20, description: '20 GP' },
    { id: 2, code: '40HC', lengthFt: 40, description: '40 HC' },
  ];
  const close = jasmine.createSpy('close');
  let component: TariffDialogComponent;
  let fixture: ComponentFixture<TariffDialogComponent>;
  let http: HttpTestingController;

  const existing: Tariff = {
    id: 9, containerTypeId: 2, containerType: '40HC', freeDays: 5, validFrom: '2026-01-01', validTo: '2026-12-31',
    slabs: [{ fromDay: 6, toDay: 10, ratePerDayUsd: 40 }, { fromDay: 11, toDay: null, ratePerDayUsd: 80 }],
  };

  function create(tariff?: Tariff): void {
    TestBed.configureTestingModule({
      declarations: [TariffDialogComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { types, tariff } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TariffDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => close.calls.reset());
  afterEach(() => http.verify());

  describe('new tariff', () => {
    beforeEach(() => create());

    it('starts with five free days and one open-ended slab that starts on day 6', () => {
      expect(component.form.controls.freeDays.value).toBe(5);
      expect(component.slabs.length).toBe(1);
      expect(component.startDay(0)).toBe(6);
      expect(component.isLast(0)).toBeTrue();
      expect(component.slabs.at(0).controls.toDay.disabled).toBeTrue();
    });

    it('moves every slab\'s first day when the free days change', () => {
      component.addSlab();
      component.slabs.at(0).controls.toDay.setValue(9);

      component.form.controls.freeDays.setValue(3);

      expect(component.startDay(0)).toBe(4);
      expect(component.startDay(1)).toBe(10);
    });

    it('adding a slab gives the previous last slab a suggested five-day band and keeps only the new one open-ended', () => {
      component.addSlab();

      expect(component.slabs.length).toBe(2);
      expect(component.slabs.at(0).controls.toDay.value).toBe(10);
      expect(component.slabs.at(0).controls.toDay.enabled).toBeTrue();
      expect(component.slabs.at(1).controls.toDay.disabled).toBeTrue();
      expect(component.startDay(1)).toBe(11);
    });

    it('stops at ten slabs, and always keeps at least one', () => {
      for (let i = 0; i < 12; i++) {
        component.addSlab();
      }
      expect(component.slabs.length).toBe(10);

      for (let i = 0; i < 12; i++) {
        component.removeSlab(0);
      }
      expect(component.slabs.length).toBe(1);
      expect(component.slabs.at(0).controls.toDay.disabled).toBeTrue();
    });

    it('removing the open-ended slab makes the one before it open-ended', () => {
      component.addSlab();

      component.removeSlab(1);

      expect(component.slabs.length).toBe(1);
      expect(component.slabs.at(0).controls.toDay.value).toBeNull();
      expect(component.isLast(0)).toBeTrue();
    });

    it('does not submit until a type and every rate are filled in', () => {
      component.save();

      http.expectNone(() => true);
      expect(component.form.invalid).toBeTrue();
      expect(component.slabProblems.some(p => p.includes('daily rate'))).toBeTrue();
    });

    it('lists slab problems live, such as an end day before the start day', () => {
      component.addSlab();
      component.slabs.at(0).patchValue({ toDay: 4, ratePerDayUsd: 10 });
      component.slabs.at(1).patchValue({ ratePerDayUsd: 20 });
      fixture.detectChanges();

      expect(component.slabProblems).toContain('Slab 1: the end day cannot be before the start day (day 6).');
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('the end day cannot be before the start day');
    });

    it('sends slabs with their derived first days and an open-ended last slab', () => {
      component.form.patchValue({ containerTypeId: 1, freeDays: 4, validFrom: '2026-03-01' });
      component.addSlab();
      component.slabs.at(0).patchValue({ toDay: 8, ratePerDayUsd: 30 });
      component.slabs.at(1).patchValue({ ratePerDayUsd: 60.5 });

      component.save();

      const call = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/tariffs'));
      expect(call.request.body).toEqual({
        containerTypeId: 1,
        freeDays: 4,
        validFrom: '2026-03-01',
        validTo: null,
        slabs: [
          { fromDay: 5, toDay: 8, ratePerDayUsd: 30 },
          { fromDay: 9, toDay: null, ratePerDayUsd: 60.5 },
        ],
      });
      call.flush({ ...existing, id: 1 });
      expect(close).toHaveBeenCalled();
    });

    it('lists the API\'s validation messages', () => {
      component.form.patchValue({ containerTypeId: 1 });
      component.slabs.at(0).patchValue({ ratePerDayUsd: 20 });

      component.save();
      http.expectOne(r => r.method === 'POST').flush(
        { errors: { Slabs: ['The first slab must start on day 6, right after the free days.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

      expect(component.serverErrors).toEqual(['The first slab must start on day 6, right after the free days.']);
      expect(component.saving).toBeFalse();
    });

    it('shows an overlapping validity period as a conflict', () => {
      component.form.patchValue({ containerTypeId: 1 });
      component.slabs.at(0).patchValue({ ratePerDayUsd: 20 });

      component.save();
      http.expectOne(r => r.method === 'POST').flush(
        { detail: 'Another tariff for this container type is already valid during part of that period.' },
        { status: 409, statusText: 'Conflict' },
      );

      expect(component.serverErrors[0]).toContain('already valid during part of that period');
      expect(close).not.toHaveBeenCalled();
    });

    it('rejects an end date before the start date', () => {
      component.form.patchValue({ containerTypeId: 1, validFrom: '2026-06-01', validTo: '2026-05-31' });
      component.slabs.at(0).patchValue({ ratePerDayUsd: 20 });
      fixture.detectChanges();

      expect(component.form.errors?.['validTo']).toContain('cannot be before the start date');
      component.save();
      http.expectNone(() => true);
    });
  });

  describe('existing tariff', () => {
    beforeEach(() => create(existing));

    it('fills the form and keeps the last slab open-ended', () => {
      expect(component.isEdit).toBeTrue();
      expect(component.form.controls.containerTypeId.value).toBe(2);
      expect(component.form.controls.validTo.value).toBe('2026-12-31');
      expect(component.slabs.length).toBe(2);
      expect(component.slabs.at(0).controls.toDay.value).toBe(10);
      expect(component.slabs.at(1).controls.toDay.disabled).toBeTrue();
    });

    it('saves with a PUT to the tariff', () => {
      component.slabs.at(1).patchValue({ ratePerDayUsd: 90 });

      component.save();

      const call = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/tariffs/9'));
      expect(call.request.body.validTo).toBe('2026-12-31');
      expect(call.request.body.slabs[1]).toEqual({ fromDay: 11, toDay: null, ratePerDayUsd: 90 });
      call.flush(existing);
    });
  });
});
