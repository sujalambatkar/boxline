import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';

import { Voyage, VoyageCall, VoyageStatus } from '../../../core/models/voyage.models';
import { localInputToIso } from '../../../core/utils/date-time';
import { SharedModule } from '../../../shared/shared.module';
import { VoyageFormComponent } from './voyage-form.component';

describe('VoyageFormComponent', () => {
  const ports = [
    { id: 1, unLocode: 'INNSA', name: 'Nhava Sheva', country: 'IN' },
    { id: 2, unLocode: 'AEJEA', name: 'Jebel Ali', country: 'AE' },
    { id: 3, unLocode: 'NLRTM', name: 'Rotterdam', country: 'NL' },
  ];
  const vessels = [{ id: 10, name: 'MV Aurora', imoNumber: '9900019' }];

  const at = (day: number, hour = 8): string => `2026-12-${String(day).padStart(2, '0')}T${String(hour).padStart(2, '0')}:00`;
  const iso = (day: number, hour = 8): string => localInputToIso(at(day, hour));

  const call = (id: number, portIndex: number, eta: string | null, etd: string | null, ata: string | null = null, atd: string | null = null): VoyageCall => ({
    id,
    sequence: id,
    port: { id: ports[portIndex].id, unLocode: ports[portIndex].unLocode, name: ports[portIndex].name },
    eta, etd, ata, atd,
  });

  const voyage = (status: VoyageStatus = 'Scheduled', calls?: VoyageCall[]): Voyage => ({
    id: 5,
    voyageNumber: 'BXL2612A1',
    status,
    allowedTransitions: status === 'Scheduled' ? ['Underway', 'Cancelled'] : status === 'Underway' ? ['Completed', 'Cancelled'] : [],
    vessel: vessels[0],
    calls: calls ?? [call(101, 0, null, iso(1)), call(102, 1, iso(4), iso(4, 20)), call(103, 2, iso(10), null)],
  });

  let fixture: ComponentFixture<VoyageFormComponent>;
  let component: VoyageFormComponent;
  let http: HttpTestingController;
  let router: Router;
  let dialogAnswer: boolean;

  function create(id: string | null = null, loaded?: Voyage): void {
    TestBed.configureTestingModule({
      declarations: [VoyageFormComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [{ provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } }],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    spyOn(TestBed.inject(MatDialog), 'open').and.callFake(
      () => ({ afterClosed: () => of(dialogAnswer) }) as unknown as ReturnType<MatDialog['open']>,
    );

    fixture = TestBed.createComponent(VoyageFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    http.expectOne(r => r.url.endsWith('/ports')).flush(ports);
    http.expectOne(r => r.url.endsWith('/vessels')).flush(vessels);
    if (id) {
      http.expectOne(r => r.url.endsWith(`/voyages/${id}`)).flush(loaded ?? voyage());
    }
    fixture.detectChanges();
  }

  /** Fills a new voyage with a valid three-port rotation. */
  function fillValid(): void {
    component.addCall();
    component.form.patchValue({ vesselId: 10, voyageNumber: 'bxl2612a1' });
    component.calls.at(0).patchValue({ portId: 1, eta: '', etd: at(1) });
    component.calls.at(1).patchValue({ portId: 2, eta: at(4), etd: at(4, 20) });
    component.calls.at(2).patchValue({ portId: 3, eta: at(10), etd: '' });
  }

  beforeEach(() => (dialogAnswer = true));
  afterEach(() => http.verify());

  describe('new voyage', () => {
    beforeEach(() => create());

    it('starts with two empty calls and is not yet valid', () => {
      expect(component.isNew).toBeTrue();
      expect(component.calls.length).toBe(2);
      expect(component.form.invalid).toBeTrue();
      expect(component.canEdit).toBeTrue();
    });

    it('adds, removes and reorders calls', () => {
      component.addCall(1);
      component.calls.at(0).patchValue({ portId: 2 });
      component.calls.at(1).patchValue({ portId: 3 });
      component.calls.at(2).patchValue({ portId: 1 });

      component.moveCall(2, -1);
      expect(component.calls.controls.map(c => c.controls.portId.value)).toEqual([2, 1, 3]);

      component.moveCall(0, 1);
      expect(component.calls.controls.map(c => c.controls.portId.value)).toEqual([1, 2, 3]);

      component.moveCall(0, -1);
      component.moveCall(2, 1);
      expect(component.calls.controls.map(c => c.controls.portId.value)).toEqual([1, 2, 3]);

      component.removeCall(1);
      expect(component.calls.controls.map(c => c.controls.portId.value)).toEqual([1, 3]);
    });

    it('knows the first and last calls', () => {
      component.addCall();

      expect(component.isFirst(0)).toBeTrue();
      expect(component.isLast(2)).toBeTrue();
      expect(component.isFirst(1) || component.isLast(1)).toBeFalse();
    });

    it('lists every rotation problem as the user edits', () => {
      component.calls.at(0).patchValue({ portId: 1, etd: '' });
      component.calls.at(1).patchValue({ portId: 1, eta: '' });
      fixture.detectChanges();

      expect(component.rotationProblems).toContain('Call 1 (first port): ETD is required.');
      expect(component.rotationProblems).toContain('Call 2 (last port): ETA is required.');
      expect(component.rotationProblems.some(p => p.includes('can appear only once'))).toBeTrue();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Call 1 (first port): ETD is required.');
    });

    it('does not call the API while the rotation is invalid', () => {
      component.form.patchValue({ vesselId: 10, voyageNumber: 'X' });

      component.save();

      http.expectNone(r => r.method === 'POST');
      expect(component.form.touched).toBeTrue();
    });

    it('creates the voyage with sequences by position, UTC times and an upper-cased number', () => {
      fillValid();
      expect(component.form.valid).toBeTrue();

      component.save();

      const request = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/voyages'));
      expect(request.request.body).toEqual({
        vesselId: 10,
        voyageNumber: 'bxl2612a1',
        calls: [
          { sequence: 1, portId: 1, eta: null, etd: iso(1) },
          { sequence: 2, portId: 2, eta: iso(4), etd: iso(4, 20) },
          { sequence: 3, portId: 3, eta: iso(10), etd: null },
        ],
      });
      request.flush(voyage());
      expect(router.navigate).toHaveBeenCalledWith(['/voyages', 5], { replaceUrl: true });
    });

    it('lists the API\'s validation messages', () => {
      fillValid();

      component.save();
      http.expectOne(r => r.method === 'POST').flush(
        { errors: { Calls: ['Port 2 does not exist.'], VesselId: ['Vessel does not exist.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

      expect(component.serverErrors).toEqual(['Port 2 does not exist.', 'Vessel does not exist.']);
      expect(component.saving).toBeFalse();
    });

    it('shows a duplicate voyage number as a conflict', () => {
      fillValid();

      component.save();
      http.expectOne(r => r.method === 'POST').flush(
        { detail: 'A voyage numbered BXL2612A1 already exists.' },
        { status: 409, statusText: 'Conflict' },
      );

      expect(component.conflictMessage).toBe('A voyage numbered BXL2612A1 already exists.');
    });
  });

  describe('scheduled voyage', () => {
    beforeEach(() => create('5'));

    it('fills the form from the voyage and offers the right actions', () => {
      expect(component.form.getRawValue().vesselId).toBe(10);
      expect(component.calls.controls.map(c => c.controls.portId.value)).toEqual([1, 2, 3]);
      expect(component.calls.at(1).controls.eta.value).toBe(at(4));
      expect(component.canEdit).toBeTrue();
      expect(component.can('Underway')).toBeTrue();
      expect(component.can('Completed')).toBeFalse();
      expect(component.canDelete).toBeTrue();
      expect(component.form.pristine).toBeTrue();
    });

    it('saves changes with a PUT and adopts the response', () => {
      component.calls.at(1).patchValue({ etd: at(5) });
      component.form.markAsDirty();

      component.save();

      const request = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/voyages/5'));
      expect(request.request.body.calls[1].etd).toBe(iso(5));
      request.flush(voyage());
      expect(component.form.pristine).toBeTrue();
    });

    it('starts the voyage after confirmation, then locks the rotation', () => {
      component.changeStatus('Underway');

      const request = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/voyages/5/status'));
      expect(request.request.body).toEqual({ status: 'Underway' });
      request.flush(voyage('Underway'));

      expect(component.canEdit).toBeFalse();
      expect(component.canRecordActuals).toBeTrue();
      expect(component.form.disabled).toBeTrue();
      expect(component.can('Completed')).toBeTrue();
    });

    it('does nothing when the user declines', () => {
      dialogAnswer = false;

      component.changeStatus('Cancelled');

      http.expectNone(r => r.url.endsWith('/status'));
      expect(component.voyage?.status).toBe('Scheduled');
    });

    it('explains why a status change was refused', () => {
      component.changeStatus('Cancelled');
      http.expectOne(r => r.url.endsWith('/status')).flush(
        { detail: 'Voyage BXL2612A1 still carries BL-202612-0001. Move or cancel those bookings first.' },
        { status: 409, statusText: 'Conflict' },
      );

      expect(component.conflictMessage).toContain('BL-202612-0001');
    });

    it('deletes after confirmation and goes back to the list', () => {
      component.remove();

      http.expectOne(r => r.method === 'DELETE' && r.url.endsWith('/voyages/5')).flush(null, { status: 204, statusText: 'No Content' });

      expect(router.navigate).toHaveBeenCalledWith(['/voyages']);
    });

    it('reloads after a conflict', () => {
      component.reload();

      http.expectOne(r => r.method === 'GET' && r.url.endsWith('/voyages/5')).flush(voyage());
      expect(component.conflictMessage).toBeNull();
    });
  });

  describe('underway voyage', () => {
    const underway = () => voyage('Underway', [
      call(101, 0, null, iso(1), null, iso(1)),
      call(102, 1, iso(4), iso(4, 20)),
      call(103, 2, iso(10), null),
    ]);

    beforeEach(() => create('5', underway()));

    it('is read-only, with actual-time inputs and no edit actions', () => {
      expect(component.canEdit).toBeFalse();
      expect(component.canRecordActuals).toBeTrue();
      expect((fixture.nativeElement as HTMLElement).querySelectorAll('input[type="datetime-local"]').length).toBe(6);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Enter actual arrival and departure times');
    });

    it('has no arrival at the first port and no departure at the last', () => {
      expect(component.actuals.at(0).controls.ata.disabled).toBeTrue();
      expect(component.actuals.at(0).controls.atd.disabled).toBeFalse();
      expect(component.actuals.at(2).controls.atd.disabled).toBeTrue();
      expect(component.actuals.at(0).controls.atd.value).toBe(at(1));
    });

    it('records the actual arrival and departure of a port', () => {
      component.actuals.at(1).patchValue({ ata: at(4, 6), atd: at(4, 22) });

      component.saveActuals(1);

      const request = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/voyages/5/calls/102/actuals'));
      expect(request.request.body).toEqual({ ata: iso(4, 6), atd: iso(4, 22) });
      request.flush(voyage('Underway', [
        call(101, 0, null, iso(1), null, iso(1)),
        call(102, 1, iso(4), iso(4, 20), iso(4, 6), iso(4, 22)),
        call(103, 2, iso(10), null),
      ]));
      expect(component.voyage?.calls[1].ata).toBe(iso(4, 6));
      expect(component.savingActualsFor).toBeNull();
    });

    it('sends only the fields that were entered', () => {
      component.actuals.at(1).patchValue({ ata: at(4, 6) });

      component.saveActuals(1);

      expect(http.expectOne(r => r.method === 'PUT').request.body).toEqual({ ata: iso(4, 6) });
    });

    it('asks for a time before calling the API', () => {
      component.saveActuals(1);

      http.expectNone(r => r.method === 'PUT');
      expect(component.serverErrors).toEqual(['Enter an actual arrival, an actual departure, or both.']);
    });

    it('shows why the actual time was refused', () => {
      component.actuals.at(2).patchValue({ ata: at(10) });

      component.saveActuals(2);
      http.expectOne(r => r.method === 'PUT').flush(
        { errors: { Actuals: ['Record the departure from the previous port (call 2) first.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

      expect(component.serverErrors).toEqual(['Record the departure from the previous port (call 2) first.']);
      expect(component.savingActualsFor).toBeNull();
    });

    it('offers to complete or cancel, but not to start or delete', () => {
      expect(component.can('Completed')).toBeTrue();
      expect(component.can('Cancelled')).toBeTrue();
      expect(component.can('Underway')).toBeFalse();
      expect(component.canDelete).toBeFalse();
    });
  });

  describe('completed voyage', () => {
    it('shows recorded times without inputs or actions', () => {
      create('5', voyage('Completed', [
        call(101, 0, null, iso(1), null, iso(1)),
        call(102, 1, iso(10), null, iso(10, 2), null),
      ]));

      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelectorAll('input[type="datetime-local"]').length).toBe(0);
      expect(host.textContent).toContain('01 Dec, 08:00');
      expect(component.canRecordActuals).toBeFalse();
      expect(component.can('Cancelled')).toBeFalse();
    });
  });

  it('goes back to the list when the voyage does not exist', () => {
    TestBed.configureTestingModule({
      declarations: [VoyageFormComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [{ provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '404' }) } } }],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(VoyageFormComponent);
    fixture.detectChanges();

    http.expectOne(r => r.url.endsWith('/ports')).flush(ports);
    http.expectOne(r => r.url.endsWith('/vessels')).flush(vessels);
    http.expectOne(r => r.url.endsWith('/voyages/404')).flush({}, { status: 404, statusText: 'Not Found' });

    expect(router.navigate).toHaveBeenCalledWith(['/voyages']);
  });
});
