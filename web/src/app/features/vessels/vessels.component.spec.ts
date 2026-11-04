import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { Vessel } from '../../core/models/voyage.models';
import { SharedModule } from '../../shared/shared.module';
import { VesselDialogComponent } from './vessel-dialog/vessel-dialog.component';
import { VesselsComponent } from './vessels.component';

describe('VesselsComponent', () => {
  let fixture: ComponentFixture<VesselsComponent>;
  let component: VesselsComponent;
  let http: HttpTestingController;
  let dialogResult: unknown;
  let opened: { component: unknown; config: unknown }[];

  const vessel = (id: number, name: string, imoNumber = '9900019'): Vessel => ({ id, name, imoNumber });
  const listRequest = () => http.expectOne(r => r.method === 'GET' && r.url.endsWith('/vessels'));

  beforeEach(async () => {
    opened = [];
    await TestBed.configureTestingModule({
      declarations: [VesselsComponent, VesselDialogComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    spyOn(TestBed.inject(MatDialog), 'open').and.callFake(((dialogComponent: unknown, config: unknown) => {
      opened.push({ component: dialogComponent, config });
      return { afterClosed: () => of(dialogResult) };
    }) as never);
    fixture = TestBed.createComponent(VesselsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => http.verify());

  it('lists the vessels with their IMO numbers', () => {
    fixture.detectChanges();
    listRequest().flush([vessel(1, 'MV Aurora', '9900019'), vessel(2, 'MV Meridian', '9900021')]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('MV Aurora');
    expect(text).toContain('9900021');
  });

  it('says so when there are none', () => {
    fixture.detectChanges();
    listRequest().flush([]);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No vessels found.');
  });

  it('debounces the search', fakeAsync(() => {
    fixture.detectChanges();
    listRequest().flush([]);

    component.search.setValue('au');
    component.search.setValue('aurora');
    tick(299);
    http.expectNone(r => r.url.endsWith('/vessels'));
    tick(1);

    expect(listRequest().request.params.get('search')).toBe('aurora');
    discardPeriodicTasks();
  }));

  it('opens the dialog to add, and reloads after a save', () => {
    fixture.detectChanges();
    listRequest().flush([]);
    dialogResult = vessel(5, 'MV New');

    component.add();

    expect(opened[0].component).toBe(VesselDialogComponent);
    listRequest().flush([vessel(5, 'MV New')]);
    expect(component.vessels).toHaveSize(1);
  });

  it('opens the dialog with the vessel to edit', () => {
    fixture.detectChanges();
    listRequest().flush([vessel(1, 'MV Aurora')]);
    dialogResult = undefined;

    component.edit(vessel(1, 'MV Aurora'));

    expect((opened[0].config as { data: { vessel: Vessel } }).data.vessel.name).toBe('MV Aurora');
    http.expectNone(r => r.method === 'GET');
  });

  it('deletes after confirmation and reloads', () => {
    fixture.detectChanges();
    listRequest().flush([vessel(1, 'MV Aurora')]);
    dialogResult = true;

    component.remove(vessel(1, 'MV Aurora'));

    http.expectOne(r => r.method === 'DELETE' && r.url.endsWith('/vessels/1')).flush(null, { status: 204, statusText: 'No Content' });
    listRequest().flush([]);
  });

  it('does not delete when the user declines', () => {
    fixture.detectChanges();
    listRequest().flush([vessel(1, 'MV Aurora')]);
    dialogResult = false;

    component.remove(vessel(1, 'MV Aurora'));

    http.expectNone(r => r.method === 'DELETE');
  });
});
