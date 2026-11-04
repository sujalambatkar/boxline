import { HttpClientTestingModule, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { ScheduleOption } from '../../../core/models/voyage.models';
import { SharedModule } from '../../../shared/shared.module';
import { SailingSelectComponent } from './sailing-select.component';

describe('SailingSelectComponent', () => {
  let fixture: ComponentFixture<SailingSelectComponent>;
  let component: SailingSelectComponent;
  let http: HttpTestingController;
  let emitted: (number | null)[];

  const option = (voyageId: number, extra: Partial<ScheduleOption> = {}): ScheduleOption => ({
    voyageId,
    voyageNumber: `BXL26${voyageId}E`,
    vesselName: 'MV Aurora',
    imoNumber: '9900019',
    pol: { id: 1, unLocode: 'INNSA', name: 'Nhava Sheva' },
    pod: { id: 2, unLocode: 'NLRTM', name: 'Rotterdam' },
    departure: '2026-12-01T08:00:00Z',
    arrival: '2026-12-20T08:00:00Z',
    transitDays: 19,
    ...extra,
  });

  const request = (): TestRequest => http.expectOne(r => r.url.endsWith('/schedules'));
  const el = () => fixture.nativeElement as HTMLElement;

  const setRoute = (pol: number | null, pod: number | null) => {
    fixture.componentRef.setInput('polId', pol);
    fixture.componentRef.setInput('podId', pod);
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [SailingSelectComponent],
      imports: [SharedModule, HttpClientTestingModule, NoopAnimationsModule],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SailingSelectComponent);
    component = fixture.componentInstance;
    emitted = [];
    component.valueChange.subscribe(v => emitted.push(v));
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('asks for two different ports before looking anything up', () => {
    http.expectNone(() => true);
    expect(el().textContent).toContain('Choose two different ports');

    setRoute(1, null);
    setRoute(1, 1);

    http.expectNone(() => true);
    expect(component.hasRoute).toBeFalse();
  });

  it('looks up sailings once both ports are chosen and offers them with their dates', () => {
    setRoute(1, 2);

    const lookup = request();
    expect(lookup.request.params.get('polId')).toBe('1');
    expect(lookup.request.params.get('podId')).toBe('2');
    lookup.flush([option(3), option(4, { departure: '2026-12-08T08:00:00Z', arrival: '2026-12-27T08:00:00Z' })]);

    expect(component.choices.map(c => c.id)).toEqual([3, 4]);
    expect(component.choices[0].label).toBe('MV Aurora · BXL263E: departs 01 Dec, arrives 20 Dec (19 days)');
    expect(component.choices[1].label).toContain('departs 08 Dec');
  });

  it('looks again when the route changes, and ignores a superseded answer', () => {
    setRoute(1, 2);
    const first = request();

    setRoute(1, 3);
    const second = request();

    expect(first.cancelled).toBeTrue();
    expect(second.request.params.get('podId')).toBe('3');
    second.flush([option(9)]);
    expect(component.choices.map(c => c.id)).toEqual([9]);
  });

  it('clears the choices when the route becomes invalid', () => {
    setRoute(1, 2);
    request().flush([option(3)]);

    setRoute(2, 2);

    http.expectNone(() => true);
    expect(component.choices).toEqual([]);
  });

  it('clears a selected sailing that does not serve the new route, but keeps one that does', () => {
    fixture.componentRef.setInput('value', 3);
    setRoute(1, 2);
    request().flush([option(3), option(4)]);
    expect(emitted).toEqual([]);

    setRoute(1, 5);
    request().flush([option(7)]);

    expect(emitted).toEqual([null]);
  });

  it('keeps the booking\'s current sailing selectable even when it is no longer offered', () => {
    fixture.componentRef.setInput('value', 9);
    fixture.componentRef.setInput('current', { id: 9, voyageNumber: 'BXL2699E', vesselName: 'MV Old', departure: null, arrival: null });
    setRoute(1, 2);
    request().flush([option(3)]);

    expect(component.choices.map(c => c.id)).toEqual([9, 3]);
    expect(component.choices[0].label).toBe('MV Old · BXL2699E (current sailing)');
    expect(emitted).toEqual([]);
  });

  it('says so when nothing is on offer', () => {
    setRoute(1, 2);
    request().flush([]);
    fixture.detectChanges();

    expect(el().textContent).toContain('No open sailings on this route');
  });

  it('shows that sailings could not be loaded, without blocking the booking', () => {
    setRoute(1, 2);
    request().flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.failed).toBeTrue();
    expect(component.choices).toEqual([]);
    expect(el().textContent).toContain('Could not load sailings');
  });

  it('reports the user\'s choice, including "no sailing yet"', () => {
    component.select(4);
    component.select(null);

    expect(emitted).toEqual([4, null]);
  });
});
