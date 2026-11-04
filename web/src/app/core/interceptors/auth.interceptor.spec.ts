import { HTTP_INTERCEPTORS, HttpClient } from '@angular/common/http';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';

import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';
import { AuthInterceptor } from './auth.interceptor';

describe('AuthInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: { token: string | null; logout: jasmine.Spy };
  let router: Router;

  beforeEach(() => {
    auth = { token: 'jwt-abc', logout: jasmine.createSpy('logout') };
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
  });

  afterEach(() => backend.verify());

  it('adds the bearer token to API calls', () => {
    http.get(`${environment.apiUrl}/bookings`).subscribe();

    expect(backend.expectOne(`${environment.apiUrl}/bookings`).request.headers.get('Authorization')).toBe('Bearer jwt-abc');
  });

  it('does not send the token to other hosts', () => {
    http.get('https://example.com/data').subscribe();

    expect(backend.expectOne('https://example.com/data').request.headers.has('Authorization')).toBeFalse();
  });

  it('sends no header when signed out', () => {
    auth.token = null;
    http.get(`${environment.apiUrl}/ports`).subscribe();

    expect(backend.expectOne(`${environment.apiUrl}/ports`).request.headers.has('Authorization')).toBeFalse();
  });

  it('signs out and returns to the login page when the API answers 401', () => {
    http.get(`${environment.apiUrl}/bookings`).subscribe({ error: () => undefined });
    backend.expectOne(`${environment.apiUrl}/bookings`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.logout).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], jasmine.objectContaining({ queryParams: jasmine.anything() }));
  });

  it('leaves a failed login alone so the form can show its message', () => {
    http.post(`${environment.apiUrl}/auth/login`, {}).subscribe({ error: () => undefined });
    backend.expectOne(`${environment.apiUrl}/auth/login`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.logout).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('passes other errors through without signing out', () => {
    let status = 0;
    http.get(`${environment.apiUrl}/bookings`).subscribe({ error: e => (status = e.status) });
    backend.expectOne(`${environment.apiUrl}/bookings`).flush({}, { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
    expect(auth.logout).not.toHaveBeenCalled();
  });
});
