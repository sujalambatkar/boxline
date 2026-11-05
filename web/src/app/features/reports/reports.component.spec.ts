import { HttpClientTestingModule, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { UtilisationReport } from '../../core/models/report.models';
import { SharedModule } from '../../shared/shared.module';
import { ReportsComponent } from './reports.component';
import { UtilisationChartComponent } from './utilisation-chart/utilisation-chart.component';

describe('ReportsComponent', () => {
  let fixture: ComponentFixture<ReportsComponent>;
  let component: ReportsComponent;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;

  const report = (overrides: Partial<UtilisationReport> = {}): UtilisationReport => ({
    rows: [
      { month: '2026-11-01', location: 'INNSA', portName: 'Nhava Sheva', movements: 12_345, containers: 3_000, loaded: 1_500, discharged: 1_400, utilisationPercent: 11.9 },
      { month: '2026-11-01', location: 'INTKD', portName: null, movements: 800, containers: 400, loaded: 0, discharged: 0, utilisationPercent: 1.6 },
    ],
    totalRows: 238,
    page: 1,
    pageSize: 20,
    fleetSize: 25_200,
    monthly: [
      { month: '2026-10-01', movements: 20_000, activeContainers: 10_000, utilisationPercent: 39.7 },
      { month: '2026-11-01', movements: 22_000, activeContainers: 12_000, utilisationPercent: 47.6 },
    ],
    elapsedMilliseconds: 134,
    ...overrides,
  });

  const request = (): TestRequest => http.expectOne(r => r.url.endsWith('/reports/utilisation'));

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ReportsComponent, UtilisationChartComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReportsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne(r => r.url.endsWith('/ports')).flush([{ id: 1, unLocode: 'INNSA', name: 'Nhava Sheva', country: 'IN' }]);
  });

  afterEach(() => http.verify());

  it('loads the first page on arrival, with no range so the server uses the latest twelve months', () => {
    const call = request();
    expect(call.request.params.get('page')).toBe('1');
    expect(call.request.params.get('pageSize')).toBe('20');
    expect(call.request.params.has('fromMonth')).toBeFalse();
    call.flush(report());
    fixture.detectChanges();

    expect(el().textContent).toContain('No month range chosen, so the latest 12 months are shown.');
  });

  it('shows the rows, the totals, the fleet size and how long the server took', () => {
    request().flush(report());
    fixture.detectChanges();

    const text = el().textContent!;
    expect(text).toContain('Nhava Sheva');
    expect(text).toContain('12,345');
    expect(text).toContain('238 month/location rows');
    expect(text).toContain('fleet of 25,200 containers');
    expect(el().querySelector('[data-testid="elapsed"]')?.textContent).toBe('134 ms');
    expect(el().querySelectorAll('tr.mat-mdc-row')).toHaveSize(2);
  });

  it('shows a dash for a location that is not a known port', () => {
    request().flush(report());
    fixture.detectChanges();

    const cells = Array.from(el().querySelectorAll('tr.mat-mdc-row')[1].querySelectorAll('td')).map(c => c.textContent?.trim());
    expect(cells[1]).toBe('INTKD');
    expect(cells[2]).toBe('—');
  });

  it('draws the monthly chart from the whole filter, not the current page', () => {
    request().flush(report());
    fixture.detectChanges();

    expect(el().querySelectorAll('[data-testid="bar"]')).toHaveSize(2);
    expect(el().querySelector('app-utilisation-chart')).not.toBeNull();
  });

  it('asks for the chosen months, which hides the default-range note', () => {
    request().flush(report());

    component.fromMonth.setValue('2026-03');
    component.toMonth.setValue('2026-06');
    const call = http.match(r => r.url.endsWith('/reports/utilisation')).filter(r => !r.cancelled).pop()!;
    expect(call.request.params.get('fromMonth')).toBe('2026-03-01');
    expect(call.request.params.get('toMonth')).toBe('2026-06-01');
    call.flush(report());
    fixture.detectChanges();

    expect(component.usingDefaultRange).toBeFalse();
    expect(el().textContent).not.toContain('No month range chosen');
  });

  it('waits for the typing to settle before asking about a location, and sends it upper-cased', fakeAsync(() => {
    request().flush(report());

    component.location.setValue('in');
    component.location.setValue(' innsa ');
    tick(399);
    http.expectNone(r => r.url.endsWith('/reports/utilisation'));
    tick(1);

    expect(request().request.params.get('location')).toBe('INNSA');
    discardPeriodicTasks();
  }));

  it('does not ask the server about a malformed location until it is corrected', fakeAsync(() => {
    request().flush(report());

    component.location.setValue('1NNSA');
    tick(400);
    http.expectNone(r => r.url.endsWith('/reports/utilisation'));
    expect(component.location.invalid).toBeTrue();

    component.location.setValue('INNSA');
    tick(400);
    request().flush(report());
    expect(component.location.valid).toBeTrue();
    discardPeriodicTasks();
  }));

  it('asks the server for the next page and keeps the page size', () => {
    request().flush(report());

    component.onPage({ pageIndex: 2, pageSize: 50, length: 238 });

    const call = request();
    expect(call.request.params.get('page')).toBe('3');
    expect(call.request.params.get('pageSize')).toBe('50');
    call.flush(report());
  });

  it('returns to the first page when a filter changes', () => {
    request().flush(report());
    component.onPage({ pageIndex: 3, pageSize: 20, length: 238 });
    request().flush(report());

    component.toMonth.setValue('2026-09');

    expect(request().request.params.get('page')).toBe('1');
  });

  it('clears every filter', () => {
    request().flush(report());
    component.fromMonth.setValue('2026-03');
    http.match(r => r.url.endsWith('/reports/utilisation')).filter(r => !r.cancelled).forEach(r => r.flush(report()));
    expect(component.hasFilters).toBeTrue();

    component.clearFilters();
    http.match(r => r.url.endsWith('/reports/utilisation')).filter(r => !r.cancelled).forEach(r => r.flush(report()));

    expect(component.hasFilters).toBeFalse();
    expect(component.usingDefaultRange).toBeTrue();
  });

  it('shows the API\'s validation message, and keeps the last good report on screen', () => {
    request().flush(report());
    fixture.detectChanges();

    component.toMonth.setValue('2026-01');
    request().flush(
      { errors: { ToMonth: ['The last month cannot be before the first month.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(component.message).toBe('The last month cannot be before the first month.');
    expect(component.loading).toBeFalse();
    expect(component.report?.totalRows).toBe(238);
  });

  it('explains when the API cannot be reached', () => {
    request().flush({}, { status: 500, statusText: 'Server Error' });

    expect(component.message).toContain('could not be loaded');
  });

  it('says so when nothing matches', () => {
    request().flush(report({ rows: [], totalRows: 0, monthly: [] }));
    fixture.detectChanges();

    expect(el().textContent).toContain('No movements match these filters.');
    expect(el().textContent).toContain('No movements in this period.');
  });

  it('ignores a superseded answer', () => {
    const first = request();

    component.fromMonth.setValue('2026-02');
    const second = http.match(r => r.url.endsWith('/reports/utilisation')).filter(r => !r.cancelled).pop()!;

    expect(first.cancelled).toBeTrue();
    second.flush(report({ totalRows: 7 }));
    expect(component.report?.totalRows).toBe(7);
  });

  it('keeps the little utilisation bars within their cell', () => {
    request().flush(report());

    expect(component.barWidth(-5)).toBe(0);
    expect(component.barWidth(47.6)).toBe(47.6);
    expect(component.barWidth(140)).toBe(100);
  });
});
