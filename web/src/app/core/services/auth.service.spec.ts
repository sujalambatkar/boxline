import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthResponse } from '../models/auth.models';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  const key = 'boxline.session';
  const inAnHour = () => new Date(Date.now() + 3_600_000).toISOString();

  const response = (overrides: Partial<AuthResponse> = {}): AuthResponse => ({
    accessToken: 'token-123',
    expiresAt: inAnHour(),
    user: { id: 7, email: 'ops@boxline.test', role: 'Ops', shipperCompany: null },
    ...overrides,
  });

  function create(): { service: AuthService; http: HttpTestingController } {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    return { service: TestBed.inject(AuthService), http: TestBed.inject(HttpTestingController) };
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => sessionStorage.clear());

  it('starts signed out', () => {
    const { service } = create();

    expect(service.isAuthenticated()).toBeFalse();
    expect(service.token).toBeNull();
    expect(service.user).toBeNull();
  });

  it('logs in, keeps the token in memory and mirrors it to sessionStorage', () => {
    const { service, http } = create();
    let signedInAs: string | undefined;

    service.login('ops@boxline.test', 'secret').subscribe(user => (signedInAs = user.email));
    const request = http.expectOne(r => r.url.endsWith('/auth/login'));
    expect(request.request.body).toEqual({ email: 'ops@boxline.test', password: 'secret' });
    request.flush(response());

    expect(signedInAs).toBe('ops@boxline.test');
    expect(service.token).toBe('token-123');
    expect(service.isAuthenticated()).toBeTrue();
    expect(JSON.parse(sessionStorage.getItem(key)!).accessToken).toBe('token-123');
  });

  it('does not store anything when the login is rejected', () => {
    const { service, http } = create();

    service.login('a@b.c', 'wrong').subscribe({ error: () => undefined });
    http.expectOne(r => r.url.endsWith('/auth/login')).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(service.isAuthenticated()).toBeFalse();
    expect(sessionStorage.getItem(key)).toBeNull();
  });

  it('restores a still-valid session after a page reload', () => {
    sessionStorage.setItem(key, JSON.stringify(response()));

    const { service } = create();

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.user?.role).toBe('Ops');
  });

  it('discards a stored session that has expired', () => {
    sessionStorage.setItem(key, JSON.stringify(response({ expiresAt: new Date(Date.now() - 1000).toISOString() })));

    const { service } = create();

    expect(service.isAuthenticated()).toBeFalse();
    expect(service.token).toBeNull();
  });

  it('ignores corrupt stored data', () => {
    sessionStorage.setItem(key, '{not json');

    expect(create().service.isAuthenticated()).toBeFalse();
  });

  it('signs out when the session expires while the app is open', () => {
    const { service, http } = create();
    service.login('a@b.c', 'x').subscribe();
    http.expectOne(r => r.url.endsWith('/auth/login')).flush(response({ expiresAt: new Date(Date.now() - 1).toISOString() }));

    expect(service.token).toBeNull();
    expect(sessionStorage.getItem(key)).toBeNull();
  });

  it('clears everything on logout and tells subscribers', () => {
    sessionStorage.setItem(key, JSON.stringify(response()));
    const { service } = create();
    const seen: (string | null)[] = [];
    service.user$.subscribe(user => seen.push(user?.email ?? null));

    service.logout();

    expect(seen).toEqual(['ops@boxline.test', null]);
    expect(sessionStorage.getItem(key)).toBeNull();
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('reports roles', () => {
    sessionStorage.setItem(key, JSON.stringify(response()));
    const { service } = create();

    expect(service.hasRole('Ops')).toBeTrue();
    expect(service.hasRole('Shipper')).toBeFalse();
    expect(service.hasRole('Shipper', 'Ops')).toBeTrue();
  });
});
