import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ScheduleService } from './schedule.service';
import { VesselService } from './vessel.service';
import { VoyageService } from './voyage.service';

describe('voyage-related services', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('VoyageService', () => {
    const empty = { search: '', status: '' as const, vesselId: null, portId: null, page: 1, pageSize: 20 };

    it('sends only paging when nothing is filtered', () => {
      TestBed.inject(VoyageService).list(empty).subscribe();

      expect(http.expectOne(r => r.url.endsWith('/voyages')).request.params.keys().sort()).toEqual(['page', 'pageSize']);
    });

    it('sends every active filter, trimming the search', () => {
      TestBed.inject(VoyageService).list({ search: ' bxl ', status: 'Underway', vesselId: 3, portId: 4, page: 2, pageSize: 50 }).subscribe();

      const params = http.expectOne(r => r.url.endsWith('/voyages')).request.params;
      expect(params.get('search')).toBe('bxl');
      expect(params.get('status')).toBe('Underway');
      expect(params.get('vesselId')).toBe('3');
      expect(params.get('portId')).toBe('4');
      expect(params.get('page')).toBe('2');
    });

    it('posts status changes and puts actuals on the right call', () => {
      const service = TestBed.inject(VoyageService);

      service.changeStatus(5, 'Completed').subscribe();
      service.recordActuals(5, 102, { atd: '2026-12-04T20:00:00Z' }).subscribe();

      expect(http.expectOne(r => r.method === 'POST' && r.url.endsWith('/voyages/5/status')).request.body).toEqual({ status: 'Completed' });
      expect(http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/voyages/5/calls/102/actuals')).request.body).toEqual({ atd: '2026-12-04T20:00:00Z' });
    });
  });

  describe('ScheduleService', () => {
    it('sends the ports, and the window only when given', () => {
      const service = TestBed.inject(ScheduleService);

      service.search(1, 2).subscribe();
      service.search(1, 2, '2026-12-01', '2026-12-31').subscribe();

      const [bare, windowed] = http.match(r => r.url.endsWith('/schedules'));
      expect(bare.request.params.keys().sort()).toEqual(['podId', 'polId']);
      expect(windowed.request.params.get('departFrom')).toBe('2026-12-01');
      expect(windowed.request.params.get('departTo')).toBe('2026-12-31');
    });
  });

  describe('VesselService', () => {
    it('searches with a trimmed term, or lists everything', () => {
      const service = TestBed.inject(VesselService);

      service.list().subscribe();
      service.list('  aurora ').subscribe();

      const [all, searched] = http.match(r => r.url.endsWith('/vessels'));
      expect(all.request.params.has('search')).toBeFalse();
      expect(searched.request.params.get('search')).toBe('aurora');
    });
  });
});
