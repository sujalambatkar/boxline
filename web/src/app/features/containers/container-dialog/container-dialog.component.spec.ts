import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { Container, ContainerType } from '../../../core/models/container.models';
import { SharedModule } from '../../../shared/shared.module';
import { ContainerDialogComponent, ContainerDialogData } from './container-dialog.component';

describe('ContainerDialogComponent', () => {
  const types: ContainerType[] = [
    { id: 1, code: '20GP', lengthFt: 20, description: "20' general purpose" },
    { id: 2, code: '40HC', lengthFt: 40, description: "40' high cube" },
  ];
  const close = jasmine.createSpy('close');
  let http: HttpTestingController;

  function create(data: ContainerDialogData): ComponentFixture<ContainerDialogComponent> {
    TestBed.configureTestingModule({
      declarations: [ContainerDialogComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ContainerDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => close.calls.reset());

  it('flags a wrong check digit while typing and tells the user the right one', () => {
    const component = create({ types }).componentInstance;

    component.form.controls.number.setValue('CSQU3054380');

    expect(component.form.controls.number.valid).toBeFalse();
    expect(component.numberError).toBe('Check digit is wrong: CSQU305438 should end in 3, not 0.');
  });

  it('accepts a valid number and offers the check digit as a hint for a partial one', () => {
    const component = create({ types }).componentInstance;

    component.form.controls.number.setValue('csqu305438');
    expect(component.checkDigitHint).toBe('Check digit for CSQU305438 is 3');

    component.form.controls.number.setValue('csqu 305438-3');
    expect(component.form.controls.number.valid).toBeTrue();
  });

  it('does not submit an invalid form', () => {
    const component = create({ types }).componentInstance;
    component.form.patchValue({ number: 'BAD', containerTypeId: 1 });

    component.save();

    http.expectNone(() => true);
    expect(component.saving).toBeFalse();
  });

  it('creates a container and closes with the saved result', () => {
    const component = create({ types }).componentInstance;
    component.form.patchValue({ number: 'CSQU3054383', containerTypeId: 2, ownershipType: 'Leased' });

    component.save();

    const request = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/containers'));
    expect(request.request.body).toEqual({
      number: 'CSQU3054383',
      containerTypeId: 2,
      ownershipType: 'Leased',
      status: 'Available',
    });
    const saved: Container = {
      id: 9, number: 'CSQU3054383', containerTypeId: 2, containerTypeCode: '40HC', ownershipType: 'Leased', status: 'Available',
    };
    request.flush(saved);
    expect(close).toHaveBeenCalledWith(saved);
  });

  it('shows the server message on the number field when the API rejects it', () => {
    const component = create({ types }).componentInstance;
    component.form.patchValue({ number: 'CSQU3054383', containerTypeId: 1 });

    component.save();
    http.expectOne(r => r.method === 'POST').flush(
      { errors: { Number: ['Check digit is wrong.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(component.numberError).toBe('Check digit is wrong.');
    expect(component.saving).toBeFalse();
  });

  it('shows a conflict message when the container already exists', () => {
    const component = create({ types }).componentInstance;
    component.form.patchValue({ number: 'CSQU3054383', containerTypeId: 1 });

    component.save();
    http.expectOne(r => r.method === 'POST').flush(
      { detail: 'Container CSQU3054383 is already registered.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.serverError).toBe('Container CSQU3054383 is already registered.');
  });

  it('locks the number when editing and sends an update without it', () => {
    const existing: Container = {
      id: 5, number: 'MSKU1234565', containerTypeId: 1, containerTypeCode: '20GP', ownershipType: 'Owned', status: 'Available',
    };
    const component = create({ types, container: existing }).componentInstance;

    expect(component.form.controls.number.disabled).toBeTrue();
    component.form.patchValue({ status: 'UnderRepair' });
    component.save();

    const request = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/containers/5'));
    expect(request.request.body).toEqual({ containerTypeId: 1, ownershipType: 'Owned', status: 'UnderRepair' });
    request.flush({ ...existing, status: 'UnderRepair' });
  });
});
