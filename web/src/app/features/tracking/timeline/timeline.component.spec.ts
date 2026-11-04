import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatIconModule } from '@angular/material/icon';

import { Movement } from '../../../core/models/tracking.models';
import { TimelineComponent } from './timeline.component';

describe('TimelineComponent', () => {
  let fixture: ComponentFixture<TimelineComponent>;
  const el = () => fixture.nativeElement as HTMLElement;

  const movement = (id: number, eventType: Movement['eventType'], extra: Partial<Movement> = {}): Movement => ({
    id, containerNumber: 'CSQU3054383', eventType, locationUnLocode: 'INNSA', occurredAt: '2026-11-04T09:30:00Z',
    bookingNumber: null, remarks: null, ...extra,
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({ declarations: [TimelineComponent], imports: [MatIconModule] }).compileComponents();
    fixture = TestBed.createComponent(TimelineComponent);
  });

  it('says so when there are no movements', () => {
    fixture.detectChanges();

    expect(el().textContent).toContain('No movements recorded yet.');
    expect(el().querySelector('ol')).toBeNull();
  });

  it('lists events in the order given, with readable labels and locations', () => {
    fixture.componentInstance.events = [
      movement(1, 'GateOutEmpty'),
      movement(2, 'LoadedOnVessel', { locationUnLocode: 'INMUN' }),
    ];
    fixture.detectChanges();

    const titles = Array.from(el().querySelectorAll('.event')).map(e => e.querySelector('.title')?.textContent?.replace(/\s+/g, ' ').trim());
    expect(titles[0]).toContain('Gate out empty');
    expect(titles[0]).toContain('INNSA');
    expect(titles[1]).toContain('Loaded on vessel');
    expect(titles[1]).toContain('INMUN');
  });

  it('marks only the last event as the latest', () => {
    fixture.componentInstance.events = [movement(1, 'GateOutEmpty'), movement(2, 'Stuffed')];
    fixture.detectChanges();

    const events = Array.from(el().querySelectorAll('.event'));
    expect(events[0].classList).not.toContain('latest');
    expect(events[1].classList).toContain('latest');
    expect(events[1].textContent).toContain('Latest');
  });

  it('shows the booking number and remarks when present', () => {
    fixture.componentInstance.events = [movement(1, 'Stuffed', { bookingNumber: 'BL-202611-0001', remarks: 'Seal 12345' })];
    fixture.detectChanges();

    expect(el().textContent).toContain('BL-202611-0001');
    expect(el().textContent).toContain('Seal 12345');
  });

  it('exposes the time as a machine-readable datetime', () => {
    fixture.componentInstance.events = [movement(1, 'Stuffed')];
    fixture.detectChanges();

    expect(el().querySelector('time')?.getAttribute('datetime')).toBe('2026-11-04T09:30:00Z');
  });
});
