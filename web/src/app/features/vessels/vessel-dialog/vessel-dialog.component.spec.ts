import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { Vessel } from '../../../core/models/voyage.models';
import { SharedModule } from '../../../shared/shared.module';
import { VesselDialogComponent } from './vessel-dialog.component';

describe('VesselDialogComponent', () => {
  let component: VesselDialogComponent;
  let http: HttpTestingController;
  const close = jasmine.createSpy('close');

  function create(vessel?: Vessel): ComponentFixture<VesselDialogComponent> {
    TestBed.configureTestingModule({
      declarations: [VesselDialogComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { vessel } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(VesselDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => close.calls.reset());
  afterEach(() => http.verify());

  it('flags a wrong check digit and tells the user what it should be', () => {
    create();

    component.form.controls.imoNumber.setValue('9074728');

    expect(component.form.controls.imoNumber.valid).toBeFalse();
    expect(component.imoError).toBe('Check digit is wrong: 907472 should end in 9, not 8.');
  });

  it('hints at the check digit while the number is incomplete', () => {
    create();

    component.form.controls.imoNumber.setValue('IMO 907472');

    expect(component.checkDigitHint).toBe('Check digit for 907472 is 9');
  });

  it('requires a name and a valid number before saving', () => {
    create();
    component.form.patchValue({ name: '   ', imoNumber: '123' });

    component.save();

    http.expectNone(() => true);
    expect(component.form.controls.name.hasError('required')).toBeTrue();
  });

  it('creates a vessel with a trimmed name and a normalised IMO number', () => {
    create();
    component.form.patchValue({ name: '  MV Aurora ', imoNumber: 'IMO 9074-729' });

    component.save();

    const request = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/vessels'));
    expect(request.request.body).toEqual({ name: 'MV Aurora', imoNumber: '9074729' });
    request.flush({ id: 1, name: 'MV Aurora', imoNumber: '9074729' });
    expect(close).toHaveBeenCalledWith({ id: 1, name: 'MV Aurora', imoNumber: '9074729' });
  });

  it('updates an existing vessel', () => {
    create({ id: 7, name: 'MV Old', imoNumber: '9074729' });
    expect(component.isEdit).toBeTrue();
    expect(component.form.controls.name.value).toBe('MV Old');

    component.form.patchValue({ name: 'MV Renamed' });
    component.save();

    const request = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/vessels/7'));
    expect(request.request.body.name).toBe('MV Renamed');
    request.flush({ id: 7, name: 'MV Renamed', imoNumber: '9074729' });
  });

  it('shows a duplicate IMO number as a message and stays open', () => {
    create();
    component.form.patchValue({ name: 'MV Twin', imoNumber: '9074729' });

    component.save();
    http.expectOne(r => r.method === 'POST').flush(
      { detail: 'A vessel with IMO number 9074729 already exists.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.serverError).toBe('A vessel with IMO number 9074729 already exists.');
    expect(component.saving).toBeFalse();
    expect(close).not.toHaveBeenCalled();
  });

  it('shows the API\'s IMO validation message on the field', () => {
    create();
    component.form.patchValue({ name: 'MV X', imoNumber: '9074729' });

    component.save();
    http.expectOne(r => r.method === 'POST').flush(
      { errors: { ImoNumber: ['Check digit is wrong.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(component.imoError).toBe('Check digit is wrong.');
  });
});
