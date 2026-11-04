import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { Tariff } from '../../core/models/detention.models';
import { SharedModule } from '../../shared/shared.module';
import { TariffDialogComponent } from './tariff-dialog/tariff-dialog.component';
import { TariffsComponent } from './tariffs.component';

describe('TariffsComponent', () => {
  let fixture: ComponentFixture<TariffsComponent>;
  let component: TariffsComponent;
  let http: HttpTestingController;
  let dialogResult: unknown;
  let opened: { component: unknown; data: unknown }[];

  const tariff = (id: number, code: string, validTo: string | null = null): Tariff => ({
    id, containerTypeId: id, containerType: code, freeDays: 5, validFrom: '2026-01-01', validTo,
    slabs: [{ fromDay: 6, toDay: 10, ratePerDayUsd: 40 }, { fromDay: 11, toDay: null, ratePerDayUsd: 80 }],
  });

  const listRequest = () => http.expectOne(r => r.method === 'GET' && r.url.endsWith('/tariffs'));

  beforeEach(async () => {
    opened = [];
    await TestBed.configureTestingModule({
      declarations: [TariffsComponent, TariffDialogComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    spyOn(TestBed.inject(MatDialog), 'open').and.callFake(((dialogComponent: unknown, config: { data: unknown }) => {
      opened.push({ component: dialogComponent, data: config.data });
      return { afterClosed: () => of(dialogResult) };
    }) as never);

    fixture = TestBed.createComponent(TariffsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne(r => r.url.endsWith('/container-types')).flush([{ id: 1, code: '20GP', lengthFt: 20, description: '20 GP' }]);
  });

  afterEach(() => http.verify());

  it('lists tariffs with their validity, free days and a summary of the daily rates', () => {
    listRequest().flush([tariff(1, '20GP'), tariff(2, '40HC', '2026-12-31')]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('20GP');
    expect(text).toContain('no end date');
    expect(text).toContain('31 Dec 2026');
    expect(text).toContain('6–10: $40 · 11+: $80');
  });

  it('summarises slabs with an open-ended last slab', () => {
    listRequest().flush([]);

    expect(component.summary(tariff(1, '20GP'))).toBe('6–10: $40 · 11+: $80');
  });

  it('filters by container type on the server', () => {
    listRequest().flush([]);

    component.typeFilter.setValue(1);

    expect(listRequest().request.params.get('containerTypeId')).toBe('1');
  });

  it('opens the dialog to add and reloads afterwards', () => {
    listRequest().flush([]);
    dialogResult = tariff(5, '20GP');

    component.add();

    expect(opened[0].component).toBe(TariffDialogComponent);
    listRequest().flush([tariff(5, '20GP')]);
    expect(component.tariffs).toHaveSize(1);
  });

  it('opens the dialog with the tariff to edit', () => {
    listRequest().flush([tariff(1, '20GP')]);
    dialogResult = undefined;

    component.edit(tariff(1, '20GP'));

    expect((opened[0].data as { tariff: Tariff }).tariff.id).toBe(1);
    http.expectNone(r => r.method === 'GET');
  });

  it('deletes after confirmation, and shows the API\'s reason if it refuses', () => {
    listRequest().flush([tariff(1, '20GP')]);
    dialogResult = true;

    component.remove(tariff(1, '20GP'));
    http.expectOne(r => r.method === 'DELETE' && r.url.endsWith('/tariffs/1')).flush(null, { status: 204, statusText: 'No Content' });
    listRequest().flush([]);
  });

  it('does nothing when the user declines to delete', () => {
    listRequest().flush([tariff(1, '20GP')]);
    dialogResult = false;

    component.remove(tariff(1, '20GP'));

    http.expectNone(r => r.method === 'DELETE');
  });
});
