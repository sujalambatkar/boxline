import { HttpClientTestingModule, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';

import { PagedResponse } from '../../core/models/container.models';
import { Voyage } from '../../core/models/voyage.models';
import { SharedModule } from '../../shared/shared.module';
import { VoyagesComponent } from './voyages.component';

describe('VoyagesComponent', () => {
  let fixture: ComponentFixture<VoyagesComponent>;
  let component: VoyagesComponent;
  let http: HttpTestingController;

  const call = (id: number, code: string, etd: string | null) => ({
    id, sequence: id, port: { id, unLocode: code, name: code }, eta: null, etd, ata: null, atd: null,
  });

  const voyage = (id: number, number: string, codes = ['CNSHA', 'INNSA', 'NLRTM']): Voyage => ({
    id,
    voyageNumber: number,
    status: 'Scheduled',
    allowedTransitions: ['Underway', 'Cancelled'],
    vessel: { id: 1, name: 'MV Aurora', imoNumber: '9900019' },
    calls: codes.map((code, i) => call(i + 1, code, i === 0 ? '2026-12-01T08:00:00Z' : null)),
  });

  const page = (items: Voyage[], total = items.length): PagedResponse<Voyage> => ({ items, totalCount: total, page: 1, pageSize: 20 });
  const listRequest = (): TestRequest => http.expectOne(r => r.method === 'GET' && r.url.endsWith('/voyages'));

  function create(): void {
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(VoyagesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne(r => r.url.endsWith('/vessels')).flush([{ id: 1, name: 'MV Aurora', imoNumber: '9900019' }]);
    http.expectOne(r => r.url.endsWith('/ports')).flush([{ id: 1, unLocode: 'INNSA', name: 'Nhava Sheva', country: 'IN' }]);
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [VoyagesComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
    }).compileComponents();
  });

  afterEach(() => http.verify());

  it('loads the first page and shows the rotation compactly', () => {
    create();

    const request = listRequest();
    expect(request.request.params.get('page')).toBe('1');
    request.flush(page([voyage(1, 'BXL2612A1'), voyage(2, 'BXL2612B1', ['INNSA', 'AEJEA'])], 14));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('BXL2612A1');
    expect(text).toContain('CNSHA → … → NLRTM');
    expect(text).toContain('INNSA → AEJEA');
    expect(text).toContain('01 Dec 2026');
    expect(component.totalCount).toBe(14);
  });

  it('offers the vessels and ports as filters', () => {
    create();
    listRequest().flush(page([]));

    expect(component.vessels.map(v => v.name)).toEqual(['MV Aurora']);
    expect(component.ports.map(p => p.unLocode)).toEqual(['INNSA']);
  });

  it('applies filters on the server and returns to the first page', () => {
    create();
    listRequest().flush(page([], 100));
    component.onPage({ pageIndex: 2, pageSize: 20, length: 100 });
    listRequest().flush(page([], 100));

    component.statusFilter.setValue('Underway');
    const request = listRequest();
    expect(request.request.params.get('status')).toBe('Underway');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(page([]));

    component.vesselFilter.setValue(1);
    const vessel = listRequest();
    expect(vessel.request.params.get('vesselId')).toBe('1');
    vessel.flush(page([]));

    component.portFilter.setValue(1);
    expect(listRequest().request.params.get('portId')).toBe('1');
  });

  it('debounces the search box', fakeAsync(() => {
    create();
    listRequest().flush(page([]));

    component.search.setValue('BX');
    component.search.setValue('BXL26');
    tick(299);
    http.expectNone(r => r.url.endsWith('/voyages'));
    tick(1);

    expect(listRequest().request.params.get('search')).toBe('BXL26');
    discardPeriodicTasks();
  }));

  it('clears all filters', () => {
    create();
    listRequest().flush(page([]));
    component.statusFilter.setValue('Completed');
    http.match(r => r.url.endsWith('/voyages')).filter(r => !r.cancelled).forEach(r => r.flush(page([])));
    expect(component.hasFilters).toBeTrue();

    component.clearFilters();
    http.match(r => r.url.endsWith('/voyages')).filter(r => !r.cancelled).forEach(r => r.flush(page([])));

    expect(component.hasFilters).toBeFalse();
  });

  it('opens a voyage', () => {
    create();
    listRequest().flush(page([voyage(7, 'BXL2612A1')]));
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);

    component.open(component.voyages[0]);

    expect(router.navigate).toHaveBeenCalledWith(['/voyages', 7]);
  });

  it('tells the user when nothing matches', () => {
    create();
    listRequest().flush(page([]));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No voyages yet.');
  });
});
