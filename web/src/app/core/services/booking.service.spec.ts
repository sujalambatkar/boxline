import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { BookingListQuery } from '../models/booking.models';
import { BookingService } from './booking.service';

describe('BookingService', () => {
  let service: BookingService;
  let http: HttpTestingController;

  const empty: BookingListQuery = {
    search: '', status: '', polId: null, podId: null, createdFrom: '', createdTo: '', page: 1, pageSize: 20,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(BookingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only paging when nothing is filtered', () => {
    service.list(empty).subscribe();

    const params = http.expectOne(r => r.url.endsWith('/bookings')).request.params;
    expect(params.keys().sort()).toEqual(['page', 'pageSize']);
  });

  it('sends every active filter', () => {
    service
      .list({ search: ' cotton ', status: 'Confirmed', polId: 1, podId: 2, createdFrom: '2026-11-01', createdTo: '2026-11-30', page: 3, pageSize: 50 })
      .subscribe();

    const params = http.expectOne(r => r.url.endsWith('/bookings')).request.params;
    expect(params.get('search')).toBe('cotton');
    expect(params.get('status')).toBe('Confirmed');
    expect(params.get('polId')).toBe('1');
    expect(params.get('podId')).toBe('2');
    expect(params.get('createdFrom')).toBe('2026-11-01');
    expect(params.get('createdTo')).toBe('2026-11-30');
    expect(params.get('page')).toBe('3');
  });

  it('sends the row version with an update', () => {
    service.update(5, { polId: 1, podId: 2, cargoDescription: 'x', lines: [], voyageId: null }, 'AAAB').subscribe();

    const request = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/bookings/5'));
    expect(request.request.body.rowVersion).toBe('AAAB');
  });

  it('assigns and clears a voyage through its own endpoint', () => {
    service.assignVoyage(5, 12).subscribe();
    service.assignVoyage(5, null).subscribe();

    const requests = http.match(r => r.method === 'PUT' && r.url.endsWith('/bookings/5/voyage'));
    expect(requests.map(r => r.request.body)).toEqual([{ voyageId: 12 }, { voyageId: null }]);
  });

  it('asks for the bill of lading as a file, keeping the response so the file name can be read', () => {
    service.billOfLading(5).subscribe();

    const call = http.expectOne(r => r.method === 'GET' && r.url.endsWith('/bookings/5/bill-of-lading'));
    expect(call.request.responseType).toBe('blob');
    call.flush(new Blob(['%PDF-']));
  });

  it('exports with the current filters but no paging, in the chosen format', () => {
    service
      .export({ search: ' cotton ', status: 'Confirmed', polId: 1, podId: 2, createdFrom: '2026-11-01', createdTo: '2026-11-30', page: 3, pageSize: 50 }, 'xlsx')
      .subscribe();

    const call = http.expectOne(r => r.url.endsWith('/bookings/export'));
    expect(call.request.responseType).toBe('blob');
    expect(call.request.params.keys().sort()).toEqual(['createdFrom', 'createdTo', 'format', 'podId', 'polId', 'search', 'status']);
    expect(call.request.params.get('format')).toBe('xlsx');
    expect(call.request.params.get('search')).toBe('cotton');
    call.flush(new Blob(['PK']));
  });

  it('exports with only the format when nothing is filtered', () => {
    service.export(empty, 'csv').subscribe();

    const call = http.expectOne(r => r.url.endsWith('/bookings/export'));
    expect(call.request.params.keys()).toEqual(['format']);
    call.flush(new Blob(['x']));
  });

  it('posts confirm and cancel without a body', () => {
    service.confirm(5).subscribe();
    service.cancel(5).subscribe();

    expect(http.expectOne(r => r.url.endsWith('/bookings/5/confirm')).request.method).toBe('POST');
    expect(http.expectOne(r => r.url.endsWith('/bookings/5/cancel')).request.method).toBe('POST');
  });
});
