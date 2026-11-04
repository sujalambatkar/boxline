import { HttpClientTestingModule, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { Container, PagedResponse } from '../../core/models/container.models';
import { SharedModule } from '../../shared/shared.module';
import { ContainerDialogComponent } from './container-dialog/container-dialog.component';
import { ContainersComponent } from './containers.component';

describe('ContainersComponent', () => {
  let fixture: ComponentFixture<ContainersComponent>;
  let component: ContainersComponent;
  let http: HttpTestingController;

  const row = (id: number, number: string): Container => ({
    id, number, containerTypeId: 1, containerTypeCode: '20GP', ownershipType: 'Owned', status: 'Available',
  });

  const page = (items: Container[], totalCount = items.length): PagedResponse<Container> => ({
    items, totalCount, page: 1, pageSize: 20,
  });

  const listRequest = (): TestRequest => http.expectOne(r => r.url.endsWith('/containers') && r.method === 'GET');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ContainersComponent, ContainerDialogComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ContainersComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    http.expectOne(r => r.url.endsWith('/container-types')).flush([
      { id: 1, code: '20GP', lengthFt: 20, description: "20' general purpose" },
    ]);
  });

  afterEach(() => http.verify());

  it('loads the first page on init and renders the rows', () => {
    const request = listRequest();
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush(page([row(1, 'CSQU3054383'), row(2, 'MSKU1234565')], 200));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('CSQU3054383');
    expect(text).toContain('MSKU1234565');
    expect(component.totalCount).toBe(200);
  });

  it('asks the server for the next page and keeps the page size', () => {
    listRequest().flush(page([row(1, 'CSQU3054383')], 200));

    component.onPage({ pageIndex: 2, pageSize: 50, length: 200 });

    const params = listRequest().request.params;
    expect(params.get('page')).toBe('3');
    expect(params.get('pageSize')).toBe('50');
  });

  it('sorts on the server and returns to the first page', () => {
    listRequest().flush(page([row(1, 'CSQU3054383')], 200));
    component.pageIndex = 3;

    component.onSort({ active: 'type', direction: 'desc' });

    const params = listRequest().request.params;
    expect(params.get('sortBy')).toBe('type');
    expect(params.get('sortDirection')).toBe('desc');
    expect(params.get('page')).toBe('1');
  });

  it('debounces the search box and resets to the first page', fakeAsync(() => {
    listRequest().flush(page([row(1, 'CSQU3054383')], 200));
    component.pageIndex = 2;

    component.search.setValue('CS');
    component.search.setValue('CSQU');
    tick(299);
    http.expectNone(r => r.url.endsWith('/containers'));
    tick(1);

    const params = listRequest().request.params;
    expect(params.get('search')).toBe('CSQU');
    expect(params.get('page')).toBe('1');
    discardPeriodicTasks();
  }));

  it('applies type and status filters, and clears them', () => {
    listRequest().flush(page([row(1, 'CSQU3054383')]));

    component.typeFilter.setValue(1);
    const typeRequest = listRequest();
    expect(typeRequest.request.params.get('containerTypeId')).toBe('1');
    typeRequest.flush(page([]));

    component.statusFilter.setValue('InUse');
    const statusRequest = listRequest();
    expect(statusRequest.request.params.get('containerTypeId')).toBe('1');
    expect(statusRequest.request.params.get('status')).toBe('InUse');
    statusRequest.flush(page([]));
    expect(component.hasFilters).toBeTrue();

    component.clearFilters();
    // Clearing changes two controls; the second request supersedes (cancels) the first.
    const live = http.match(r => r.url.endsWith('/containers')).filter(r => !r.cancelled);
    expect(live.length).toBe(1);
    expect(live[0].request.params.has('containerTypeId')).toBeFalse();
    expect(live[0].request.params.has('status')).toBeFalse();
    live[0].flush(page([]));
    expect(component.hasFilters).toBeFalse();
  });

  it('tells the user when nothing matches', () => {
    listRequest().flush(page([]));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No containers match your filters.');
  });
});
