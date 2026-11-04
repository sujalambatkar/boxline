import { HttpClientTestingModule, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';

import { UserRole } from '../../core/models/auth.models';
import { Booking } from '../../core/models/booking.models';
import { PagedResponse } from '../../core/models/container.models';
import { AuthService } from '../../core/services/auth.service';
import { FileDownloadService } from '../../core/services/file-download.service';
import { SharedModule } from '../../shared/shared.module';
import { BookingsComponent } from './bookings.component';

describe('BookingsComponent', () => {
  let fixture: ComponentFixture<BookingsComponent>;
  let component: BookingsComponent;
  let http: HttpTestingController;
  let files: { save: jasmine.Spy; problemMessage: jasmine.Spy };

  const booking = (id: number, number: string): Booking => ({
    id,
    bookingNumber: number,
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
    confirmedAt: null,
    rowVersion: 'AAAA',
    totalContainers: 3,
    lines: [],
  });

  const page = (items: Booking[], totalCount = items.length): PagedResponse<Booking> => ({
    items, totalCount, page: 1, pageSize: 20,
  });

  const listRequest = (): TestRequest => http.expectOne(r => r.url.endsWith('/bookings') && r.method === 'GET');

  beforeEach(() => {
    files = {
      save: jasmine.createSpy('save'),
      problemMessage: jasmine.createSpy('problemMessage').and.callFake(async (_: unknown, fallback: string) => fallback),
    };
  });

  async function create(role: UserRole): Promise<void> {
    await TestBed.configureTestingModule({
      declarations: [BookingsComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [
        { provide: AuthService, useValue: { hasRole: (...roles: UserRole[]) => roles.includes(role) } },
        { provide: FileDownloadService, useValue: files },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BookingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    http.expectOne(r => r.url.endsWith('/ports')).flush([
      { id: 1, unLocode: 'INNSA', name: 'Nhava Sheva', country: 'IN' },
      { id: 2, unLocode: 'AEJEA', name: 'Jebel Ali', country: 'AE' },
    ]);
  }

  afterEach(() => http.verify());

  it('loads the first page and renders bookings with their route', async () => {
    await create('Shipper');

    const request = listRequest();
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush(page([booking(1, 'BL-202611-0001')], 41));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('BL-202611-0001');
    expect(text).toContain('INNSA');
    expect(text).toContain('AEJEA');
    expect(component.totalCount).toBe(41);
  });

  it('offers "New booking" to shippers but not to Ops', async () => {
    await create('Shipper');
    listRequest().flush(page([]));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('New booking');
  });

  it('hides "New booking" and shows the shipper column for Ops', async () => {
    await create('Ops');
    listRequest().flush(page([booking(1, 'BL-202611-0001')]));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).not.toContain('New booking');
    expect(component.columns).toContain('shipper');
    expect(text).toContain('Acme Exports');
  });

  it('shows the voyage number, or a dash when there is no sailing yet', async () => {
    await create('Shipper');
    const withVoyage = { ...booking(1, 'BL-202611-0001'), voyageId: 9, voyage: { id: 9, voyageNumber: 'BXL2612E', vesselName: 'MV Aurora', departure: null, arrival: null } };
    listRequest().flush(page([withVoyage, booking(2, 'BL-202611-0002')]));
    fixture.detectChanges();

    const cells = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tr.row')).map(r => r.querySelectorAll('td')[2].textContent?.trim());
    expect(cells).toEqual(['BXL2612E', '—']);
  });

  it('hides the shipper column for shippers', async () => {
    await create('Shipper');
    listRequest().flush(page([]));

    expect(component.columns).not.toContain('shipper');
  });

  it('applies the status and port filters on the server and resets to page one', async () => {
    await create('Ops');
    listRequest().flush(page([], 100));
    component.onPage({ pageIndex: 2, pageSize: 20, length: 100 });
    listRequest().flush(page([], 100));

    component.statusFilter.setValue('Confirmed');
    const first = listRequest();
    expect(first.request.params.get('status')).toBe('Confirmed');
    expect(first.request.params.get('page')).toBe('1');
    first.flush(page([]));

    component.polFilter.setValue(1);
    component.podFilter.setValue(2);
    const requests = http.match(r => r.url.endsWith('/bookings')).filter(r => !r.cancelled);
    expect(requests.length).toBe(1);
    expect(requests[0].request.params.get('polId')).toBe('1');
    expect(requests[0].request.params.get('podId')).toBe('2');
    requests[0].flush(page([]));
  });

  it('sends the date range', async () => {
    await create('Ops');
    listRequest().flush(page([]));

    component.fromFilter.setValue('2026-11-01');
    const request = listRequest();
    expect(request.request.params.get('createdFrom')).toBe('2026-11-01');
    request.flush(page([]));

    component.toFilter.setValue('2026-11-30');
    expect(listRequest().request.params.get('createdTo')).toBe('2026-11-30');
  });

  it('debounces the search box', fakeAsync(() => {
    // fakeAsync needs the component created inside the zone, so set up synchronously here.
    TestBed.configureTestingModule({
      declarations: [BookingsComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [{ provide: AuthService, useValue: { hasRole: () => false } }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BookingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne(r => r.url.endsWith('/ports')).flush([]);
    listRequest().flush(page([]));

    component.search.setValue('co');
    component.search.setValue('cotton');
    tick(299);
    http.expectNone(r => r.url.endsWith('/bookings'));
    tick(1);

    expect(listRequest().request.params.get('search')).toBe('cotton');
    discardPeriodicTasks();
  }));

  it('clears every filter', async () => {
    await create('Ops');
    listRequest().flush(page([]));
    component.statusFilter.setValue('Draft');
    http.match(r => r.url.endsWith('/bookings')).forEach(r => !r.cancelled && r.flush(page([])));
    expect(component.hasFilters).toBeTrue();

    component.clearFilters();
    http.match(r => r.url.endsWith('/bookings')).forEach(r => !r.cancelled && r.flush(page([])));

    expect(component.hasFilters).toBeFalse();
  });

  describe('export', () => {
    it('downloads every booking matching the current filters in the chosen format', async () => {
      await create('Shipper');
      listRequest().flush(page([]));
      component.statusFilter.setValue('Confirmed');
      http.match(r => r.url.endsWith('/bookings') && r.method === 'GET').filter(r => !r.cancelled).forEach(r => r.flush(page([])));
      component.polFilter.setValue(1);
      http.match(r => r.url.endsWith('/bookings') && r.method === 'GET').filter(r => !r.cancelled).forEach(r => r.flush(page([])));
      component.pageIndex = 4;

      component.export('xlsx');

      const call = http.expectOne(r => r.url.endsWith('/bookings/export'));
      expect(call.request.params.get('format')).toBe('xlsx');
      expect(call.request.params.get('status')).toBe('Confirmed');
      expect(call.request.params.get('polId')).toBe('1');
      expect(call.request.params.has('page')).toBeFalse();
      expect(call.request.params.has('pageSize')).toBeFalse();
      call.flush(new Blob(['PK']), { headers: { 'Content-Disposition': 'attachment; filename="bookings-20261105-1430.xlsx"' } });

      expect(files.save).toHaveBeenCalledTimes(1);
      expect(files.save.calls.mostRecent().args[1]).toBe('bookings.xlsx');
      expect(component.exporting).toBeFalse();
    });

    it('offers both formats in a menu and mentions that filters apply to the whole export', async () => {
      await create('Shipper');
      listRequest().flush(page([]));
      component.statusFilter.setValue('Draft');
      http.match(r => r.url.endsWith('/bookings') && r.method === 'GET').filter(r => !r.cancelled).forEach(r => r.flush(page([])));
      fixture.detectChanges();

      (fixture.nativeElement.querySelector('[data-testid="export"]') as HTMLButtonElement).click();
      fixture.detectChanges();

      const items = Array.from(document.querySelectorAll('.mat-mdc-menu-item')).map(i => i.textContent?.trim());
      expect(items).toEqual(['CSV (.csv)', 'Excel (.xlsx)']);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('includes every booking that matches your filters');
    });

    it('ignores another click while an export is under way', async () => {
      await create('Shipper');
      listRequest().flush(page([]));

      component.export('csv');
      component.export('csv');

      expect(http.match(r => r.url.endsWith('/bookings/export'))).toHaveSize(1);
    });

    it('explains why an export failed, such as too many rows', async () => {
      await create('Shipper');
      listRequest().flush(page([]));
      files.problemMessage.and.resolveTo('More than 10,000 bookings match. Narrow the filters and try again.');
      const snack = spyOn((component as unknown as { snackBar: { open: (...args: unknown[]) => void } }).snackBar, 'open');

      component.export('csv');
      http.expectOne(r => r.url.endsWith('/bookings/export')).flush(new Blob(['{}']), { status: 400, statusText: 'Bad Request' });
      await fixture.whenStable();

      expect(component.exporting).toBeFalse();
      expect(files.save).not.toHaveBeenCalled();
      expect(snack).toHaveBeenCalledWith('More than 10,000 bookings match. Narrow the filters and try again.', 'Dismiss', jasmine.anything());
    });
  });

  it('opens a booking when its row is activated', async () => {
    await create('Shipper');
    listRequest().flush(page([booking(7, 'BL-202611-0007')]));
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);

    component.open(component.bookings[0]);

    expect(router.navigate).toHaveBeenCalledWith(['/bookings', 7]);
  });

  it('tells the user when nothing matches', async () => {
    await create('Shipper');
    listRequest().flush(page([]));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No bookings yet.');
  });
});
