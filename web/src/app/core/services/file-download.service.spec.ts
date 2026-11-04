import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { fakeAsync, flush, TestBed, tick } from '@angular/core/testing';

import { FileDownloadService } from './file-download.service';

describe('FileDownloadService', () => {
  describe('fileName', () => {
    it('reads a quoted file name', () => {
      expect(FileDownloadService.fileName('attachment; filename="BL-202611-0001.pdf"')).toBe('BL-202611-0001.pdf');
    });

    it('reads an unquoted file name', () => {
      expect(FileDownloadService.fileName('attachment; filename=bookings-20261105-1430.csv')).toBe('bookings-20261105-1430.csv');
    });

    it('prefers the UTF-8 form over the plain one', () => {
      const header = "attachment; filename=BL-202611-0001.pdf; filename*=UTF-8''B%C3%BCcher%20Liste.pdf";

      expect(FileDownloadService.fileName(header)).toBe('Bücher Liste.pdf');
    });

    it('falls back to the plain form when the UTF-8 form is malformed', () => {
      expect(FileDownloadService.fileName("attachment; filename=ok.pdf; filename*=UTF-8''%E0%A4%A")).toBe('ok.pdf');
    });

    it('strips path separators so a hostile header cannot choose a folder', () => {
      expect(FileDownloadService.fileName('attachment; filename="../../etc/passwd"')).toBe('.._.._etc_passwd');
    });

    it('returns null when there is no file name', () => {
      expect(FileDownloadService.fileName(null)).toBeNull();
      expect(FileDownloadService.fileName('inline')).toBeNull();
    });
  });

  describe('save', () => {
    let service: FileDownloadService;
    let clicked: HTMLAnchorElement[];
    let created: Blob[];
    let revoked: string[];

    beforeEach(() => {
      service = TestBed.inject(FileDownloadService);
      clicked = [];
      created = [];
      revoked = [];
      spyOn(URL, 'createObjectURL').and.callFake((blob: Blob | MediaSource) => {
        created.push(blob as Blob);
        return 'blob:test-url';
      });
      spyOn(URL, 'revokeObjectURL').and.callFake((url: string) => void revoked.push(url));
      spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
        clicked.push(this);
      });
    });

    const response = (headers: Record<string, string> = {}, body: Blob | null = new Blob(['%PDF-'], { type: 'application/pdf' })) =>
      new HttpResponse<Blob>({ body, headers: new HttpHeaders(headers), status: 200 });

    it('downloads the file under the name the server gave', fakeAsync(() => {
      service.save(response({ 'Content-Disposition': 'attachment; filename="BL-202611-0001.pdf"' }), 'fallback.pdf');

      expect(clicked).toHaveSize(1);
      expect(clicked[0].download).toBe('BL-202611-0001.pdf');
      expect(clicked[0].href).toBe('blob:test-url');
      expect(created[0].type).toBe('application/pdf');
      flush();
    }));

    it('uses the fallback name when the server sent none', fakeAsync(() => {
      service.save(response(), 'bookings.csv');

      expect(clicked[0].download).toBe('bookings.csv');
      flush();
    }));

    it('does not leave the temporary link in the page, and releases the object URL a moment later', fakeAsync(() => {
      service.save(response(), 'x.pdf');

      expect(document.querySelectorAll('a[download]').length).toBe(0);
      expect(revoked).toEqual([]);

      tick(1000);

      expect(revoked).toEqual(['blob:test-url']);
    }));

    it('does nothing for an empty body', fakeAsync(() => {
      service.save(response({}, null), 'x.pdf');

      expect(clicked).toHaveSize(0);
      expect(created).toHaveSize(0);
      flush();
    }));
  });

  describe('problemMessage', () => {
    const service = () => TestBed.inject(FileDownloadService);
    const failed = (body: unknown, status = 409) =>
      new HttpErrorResponse({ status, error: typeof body === 'string' ? new Blob([body]) : body });

    it('reads the detail out of a problem document returned as a Blob', async () => {
      const message = await service().problemMessage(failed(JSON.stringify({ detail: 'Booking BL-1 is cancelled, so it has no bill of lading.' })), 'fallback');

      expect(message).toBe('Booking BL-1 is cancelled, so it has no bill of lading.');
    });

    it('joins validation messages', async () => {
      const message = await service().problemMessage(
        failed(JSON.stringify({ errors: { Export: ['More than 10,000 bookings match.'], format: ['format must be csv or xlsx.'] } }), 400),
        'fallback',
      );

      expect(message).toBe('More than 10,000 bookings match. format must be csv or xlsx.');
    });

    it('uses the title when there is no detail', async () => {
      expect(await service().problemMessage(failed(JSON.stringify({ title: 'Not found.' }), 404), 'fallback')).toBe('Not found.');
    });

    it('falls back when the body is not JSON, or not an HTTP error', async () => {
      expect(await service().problemMessage(failed('<html>oops</html>', 500), 'fallback')).toBe('fallback');
      expect(await service().problemMessage(new Error('network'), 'fallback')).toBe('fallback');
    });

    it('also reads an ordinary (non-Blob) error body', async () => {
      expect(await service().problemMessage(failed({ detail: 'Plain body.' }), 'fallback')).toBe('Plain body.');
    });
  });
});
