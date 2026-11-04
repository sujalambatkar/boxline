import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, Observable, throwError } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';

/** Adds the bearer token to API calls and signs the user out when the API says the token is no longer valid. */
@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(
    private readonly auth: AuthService,
    private readonly router: Router,
  ) {}

  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    const isApiCall = request.url.startsWith(environment.apiUrl);
    const token = this.auth.token;

    const outgoing =
      isApiCall && token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

    return next.handle(outgoing).pipe(
      catchError((error: unknown) => {
        const isLogin = request.url.endsWith('/auth/login');
        if (error instanceof HttpErrorResponse && error.status === 401 && isApiCall && !isLogin) {
          this.auth.logout();
          void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
        }
        return throwError(() => error);
      }),
    );
  }
}
