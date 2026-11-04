import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { convertToParamMap, ActivatedRoute, ParamMap, Router } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { RouterTestingModule } from '@angular/router/testing';
import { BehaviorSubject } from 'rxjs';

import { UserRole } from '../../core/models/auth.models';
import { BookingTracking, ContainerTracking, Movement } from '../../core/models/tracking.models';
import { AuthService } from '../../core/services/auth.service';
import { SharedModule } from '../../shared/shared.module';
import { RecordMovementComponent } from './record-movement/record-movement.component';
import { TimelineComponent } from './timeline/timeline.component';
import { TrackingComponent } from './tracking.component';

describe('TrackingComponent', () => {
  let fixture: ComponentFixture<TrackingComponent>;
  let component: TrackingComponent;
  let http: HttpTestingController;
  let router: Router;
  let params$: BehaviorSubject<ParamMap>;

  const el = () => fixture.nativeElement as HTMLElement;
  const setQuery = (q: string | null) => {
    params$.next(convertToParamMap(q === null ? {} : { q }));
    fixture.detectChanges();
  };

  const event = (id: number, eventType: Movement['eventType'], extra: Partial<Movement> = {}): Movement => ({
    id, containerNumber: 'CSQU3054383', eventType, locationUnLocode: 'INNSA', occurredAt: '2026-11-04T09:30:00Z',
    bookingNumber: null, remarks: null, ...extra,
  });

  const containerResult = (overrides: Partial<ContainerTracking> = {}): ContainerTracking => ({
    containerNumber: 'CSQU3054383',
    containerType: '20GP',
    current: event(2, 'Stuffed'),
    events: [event(1, 'GateOutEmpty'), event(2, 'Stuffed')],
    allowedNextEvents: ['GateInFull'],
    ...overrides,
  });

  const bookingResult = (): BookingTracking => ({
    bookingNumber: 'BL-202611-0001',
    status: 'Confirmed',
    pol: { id: 1, unLocode: 'INNSA', name: 'Nhava Sheva' },
    pod: { id: 2, unLocode: 'NLRTM', name: 'Rotterdam' },
    containers: [
      containerResult({ allowedNextEvents: [] }),
      containerResult({ containerNumber: 'MSKU1234565', current: null, events: [], allowedNextEvents: [] }),
    ],
  });

  async function create(role: UserRole): Promise<void> {
    params$ = new BehaviorSubject(convertToParamMap({}));
    await TestBed.configureTestingModule({
      declarations: [TrackingComponent, TimelineComponent, RecordMovementComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule, RouterTestingModule],
      providers: [
        { provide: AuthService, useValue: { hasRole: (...roles: UserRole[]) => roles.includes(role) } },
        { provide: ActivatedRoute, useValue: { queryParamMap: params$ } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(TrackingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('does nothing until there is a query', async () => {
    await create('Shipper');

    http.expectNone(() => true);
    expect(el().textContent).not.toContain('Nothing found');
  });

  it('looks up a container from the URL and shows its timeline', async () => {
    await create('Shipper');

    setQuery('CSQU3054383');
    http.expectOne(r => r.url.endsWith('/tracking/containers/CSQU3054383')).flush(containerResult());
    fixture.detectChanges();

    expect(component.query.value).toBe('CSQU3054383');
    expect(el().textContent).toContain('CSQU3054383');
    expect(el().textContent).toContain('Gate out empty');
    expect(el().textContent).toContain('Stuffed');
    expect(el().textContent).toContain('Latest: Stuffed at INNSA');
  });

  it('accepts a loosely typed container number and sends it normalised', async () => {
    await create('Shipper');

    setQuery('csqu 305438-3');

    http.expectOne(r => r.url.endsWith('/tracking/containers/CSQU3054383')).flush(containerResult());
  });

  it('looks up a booking number, uppercased, and lists its containers', async () => {
    await create('Shipper');

    setQuery('bl-202611-0001');
    http.expectOne(r => r.url.endsWith('/tracking/bookings/BL-202611-0001')).flush(bookingResult());
    fixture.detectChanges();

    const text = el().textContent!;
    expect(text).toContain('BL-202611-0001');
    expect(text).toContain('Confirmed');
    expect(text).toContain('INNSA');
    expect(text).toContain('NLRTM');
    expect(el().querySelectorAll('mat-expansion-panel')).toHaveSize(2);
    expect(text).toContain('No events');
  });

  it('explains an invalid container number without calling the API', async () => {
    await create('Shipper');

    setQuery('CSQU3054380');

    http.expectNone(() => true);
    expect(el().textContent).toContain('Check digit is wrong');
    expect(el().textContent).toContain('Booking numbers look like BL-202611-0001');
  });

  it('says nothing was found on a 404, without revealing why', async () => {
    await create('Shipper');

    setQuery('MSKU1234565');
    http.expectOne(r => r.url.endsWith('/tracking/containers/MSKU1234565')).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(el().textContent).toContain('Nothing found for MSKU1234565');
    expect(component.result).toBeNull();
  });

  it('reports an unavailable service on other errors', async () => {
    await create('Shipper');

    setQuery('MSKU1234565');
    http.expectOne(r => r.url.includes('/tracking/containers/')).flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(el().textContent).toContain('Tracking is unavailable right now');
  });

  it('clears an earlier result when the query is emptied', async () => {
    await create('Shipper');
    setQuery('CSQU3054383');
    http.expectOne(r => r.url.includes('/tracking/containers/')).flush(containerResult());

    setQuery(null);

    expect(component.result).toBeNull();
  });

  it('puts the trimmed query in the URL when searching', async () => {
    await create('Shipper');
    component.query.setValue('  CSQU3054383 ');

    component.search();

    expect(router.navigate).toHaveBeenCalledWith([], { queryParams: { q: 'CSQU3054383' }, queryParamsHandling: 'merge' });
  });

  it('links a container with history to the detention calculator', async () => {
    await create('Shipper');

    setQuery('CSQU3054383');
    http.expectOne(r => r.url.includes('/tracking/containers/')).flush(containerResult());
    fixture.detectChanges();

    const link = el().querySelector('a.detention-link') as HTMLAnchorElement;
    expect(link.textContent).toContain('Calculate detention');
    expect(link.getAttribute('href')).toContain('/detention?container=CSQU3054383');
  });

  it('does not offer the calculator for a container with no movements', async () => {
    await create('Ops');

    setQuery('CSQU3054383');
    http.expectOne(r => r.url.includes('/tracking/containers/')).flush(containerResult({ events: [], current: null, allowedNextEvents: ['GateOutEmpty'] }));
    fixture.detectChanges();

    expect(el().querySelector('a.detention-link')).toBeNull();
  });

  it('offers Ops the record form for a container that can take another event', async () => {
    await create('Ops');

    setQuery('CSQU3054383');
    http.expectOne(r => r.url.includes('/tracking/containers/')).flush(containerResult());
    fixture.detectChanges();

    expect(el().querySelector('app-record-movement')).not.toBeNull();
    expect(el().textContent).toContain('Record next event');
  });

  it('never shows the record form to shippers', async () => {
    await create('Shipper');

    setQuery('CSQU3054383');
    http.expectOne(r => r.url.includes('/tracking/containers/')).flush(containerResult({ allowedNextEvents: [] }));
    fixture.detectChanges();

    expect(el().querySelector('app-record-movement')).toBeNull();
  });

  it('reloads the timeline after an event is recorded', async () => {
    await create('Ops');
    setQuery('CSQU3054383');
    http.expectOne(r => r.url.includes('/tracking/containers/')).flush(containerResult());
    fixture.detectChanges();

    component.refresh();

    http.expectOne(r => r.url.endsWith('/tracking/containers/CSQU3054383')).flush(
      containerResult({ events: [event(1, 'GateOutEmpty'), event(2, 'Stuffed'), event(3, 'GateInFull')], allowedNextEvents: ['LoadedOnVessel'] }),
    );
    expect(component.container?.events).toHaveSize(3);
  });

  it('lets Ops jump from a booking\'s container to the container itself', async () => {
    await create('Ops');
    setQuery('BL-202611-0001');
    http.expectOne(r => r.url.includes('/tracking/bookings/')).flush(bookingResult());
    fixture.detectChanges();

    component.open('MSKU1234565');

    expect(router.navigate).toHaveBeenCalledWith([], { queryParams: { q: 'MSKU1234565' } });
  });
});
