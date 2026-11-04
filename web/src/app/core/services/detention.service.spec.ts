import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { DetentionService } from './detention.service';
import { TariffService } from './tariff.service';

describe('detention services', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('DetentionService', () => {
    it('posts a what-if calculation', () => {
      const request = { containerTypeId: 3, outDate: '2026-11-02', returnDate: '2026-11-17', countWeekendsAsChargeable: false };
      TestBed.inject(DetentionService).calculate(request).subscribe();

      const call = http.expectOne(r => r.method === 'POST' && r.url.endsWith('/detention/calculate'));
      expect(call.request.body).toEqual(request);
    });

    it('prices a container, encoding the number and sending the weekend choice', () => {
      TestBed.inject(DetentionService).forContainer('CSQU3054383', false).subscribe();

      const call = http.expectOne(r => r.url.endsWith('/detention/containers/CSQU3054383'));
      expect(call.request.params.get('countWeekendsAsChargeable')).toBe('false');
      expect(call.request.params.has('bookingNumber')).toBeFalse();
    });

    it('limits the clock to a booking when one is given', () => {
      TestBed.inject(DetentionService).forContainer('CSQU3054383', true, ' BL-202611-0001 ').subscribe();

      expect(http.expectOne(r => r.url.includes('/detention/containers/')).request.params.get('bookingNumber')).toBe('BL-202611-0001');
    });
  });

  describe('TariffService', () => {
    it('lists everything, or one container type', () => {
      const service = TestBed.inject(TariffService);

      service.list().subscribe();
      service.list(4).subscribe();

      const [all, one] = http.match(r => r.url.endsWith('/tariffs'));
      expect(all.request.params.has('containerTypeId')).toBeFalse();
      expect(one.request.params.get('containerTypeId')).toBe('4');
    });

    it('creates, updates and deletes through the right endpoints', () => {
      const service = TestBed.inject(TariffService);
      const body = { containerTypeId: 1, freeDays: 5, validFrom: '2026-01-01', validTo: null, slabs: [{ fromDay: 6, toDay: null, ratePerDayUsd: 40 }] };

      service.create(body).subscribe();
      service.update(7, body).subscribe();
      service.delete(7).subscribe();

      expect(http.expectOne(r => r.method === 'POST' && r.url.endsWith('/tariffs')).request.body).toEqual(body);
      expect(http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/tariffs/7')).request.body).toEqual(body);
      http.expectOne(r => r.method === 'DELETE' && r.url.endsWith('/tariffs/7'));
    });
  });
});
