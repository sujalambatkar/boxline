import { HttpClientTestingModule, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';

import { UserRole } from '../../core/models/auth.models';
import { Booking } from '../../core/models/booking.models';
import { PagedResponse } from '../../core/models/container.models';
import { AuthService } from '../../core/services/auth.service';
import { SharedModule } from '../../shared/shared.module';
import { BookingsComponent } from './bookings.component';

describe('BookingsComponent', () => {
  let fixture: ComponentFixture<BookingsComponent>;
  let component: BookingsComponent;
  let http: HttpTestingController;

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
    rowVersion: 'AAAA',
    totalContainers: 3,
    lines: [],
  });

  const page = (items: Booking[], totalCount = items.length): PagedResponse<Booking> => ({
    items, totalCount, page: 1, pageSize: 20,
  });

  const listRequest = (): TestRequest => http.expectOne(r => r.url.endsWith('/bookings') && r.method === 'GET');

  async function create(role: UserRole): Promise<void> {
    await TestBed.configureTestingModule({
      declarations: [BookingsComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [{ provide: AuthService, useValue: { hasRole: (...roles: UserRole[]) => roles.includes(role) } }],
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
