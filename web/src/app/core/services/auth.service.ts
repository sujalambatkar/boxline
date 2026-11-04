import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, map, Observable, tap } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthResponse, User, UserRole } from '../models/auth.models';

/**
 * Holds the signed-in session. The token lives in memory and is mirrored to sessionStorage so a page
 * reload keeps the user signed in; closing the tab ends the session.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private static readonly storageKey = 'boxline.session';

  private readonly session$ = new BehaviorSubject<AuthResponse | null>(AuthService.restore());

  readonly user$: Observable<User | null> = this.session$.pipe(map(() => this.user));

  constructor(private readonly http: HttpClient) {}

  /** The bearer token, or null when signed out or expired. */
  get token(): string | null {
    return this.current()?.accessToken ?? null;
  }

  get user(): User | null {
    return this.current()?.user ?? null;
  }

  isAuthenticated(): boolean {
    return this.current() !== null;
  }

  hasRole(...roles: UserRole[]): boolean {
    const user = this.user;
    return !!user && roles.includes(user.role);
  }

  login(email: string, password: string): Observable<User> {
    return this.http.post<AuthResponse>(`${environment.apiUrl}/auth/login`, { email, password }).pipe(
      tap(response => this.start(response)),
      map(response => response.user),
    );
  }

  logout(): void {
    this.clear();
  }

  private start(response: AuthResponse): void {
    try {
      sessionStorage.setItem(AuthService.storageKey, JSON.stringify(response));
    } catch {
      // Storage can be unavailable (private mode); the in-memory session still works.
    }
    this.session$.next(response);
  }

  private clear(): void {
    try {
      sessionStorage.removeItem(AuthService.storageKey);
    } catch {
      // Nothing to clean up.
    }
    if (this.session$.value !== null) {
      this.session$.next(null);
    }
  }

  private current(): AuthResponse | null {
    const session = this.session$.value;
    if (session && Date.parse(session.expiresAt) <= Date.now()) {
      this.clear();
      return null;
    }
    return session;
  }

  private static restore(): AuthResponse | null {
    try {
      const raw = sessionStorage.getItem(AuthService.storageKey);
      const session = raw ? (JSON.parse(raw) as AuthResponse) : null;
      return session && Date.parse(session.expiresAt) > Date.now() ? session : null;
    } catch {
      return null;
    }
  }
}
