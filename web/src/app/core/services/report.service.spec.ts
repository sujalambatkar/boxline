import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ReportService } from './report.service';

describe('ReportService', () => {
  let service: ReportService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ReportService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only paging when no filter is set, leaving the default range to the server', () => {
    service.utilisation({ fromMonth: '', toMonth: '', location: '', page: 1, pageSize: 20 }).subscribe();

    const params = http.expectOne(r => r.url.endsWith('/reports/utilisation')).request.params;
    expect(params.keys().sort()).toEqual(['page', 'pageSize']);
  });

  it('turns month inputs into the first day of the month, and tidies the location', () => {
    service.utilisation({ fromMonth: '2026-03', toMonth: '2026-11', location: ' innsa ', page: 2, pageSize: 50 }).subscribe();

    const params = http.expectOne(r => r.url.endsWith('/reports/utilisation')).request.params;
    expect(params.get('fromMonth')).toBe('2026-03-01');
    expect(params.get('toMonth')).toBe('2026-11-01');
    expect(params.get('location')).toBe('INNSA');
    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('50');
  });

  it('ignores a blank location', () => {
    service.utilisation({ fromMonth: '2026-03', toMonth: '', location: '   ', page: 1, pageSize: 20 }).subscribe();

    const params = http.expectOne(r => r.url.endsWith('/reports/utilisation')).request.params;
    expect(params.has('location')).toBeFalse();
    expect(params.has('toMonth')).toBeFalse();
  });
});
