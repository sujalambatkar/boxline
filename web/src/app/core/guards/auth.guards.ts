import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { UserRole } from '../models/auth.models';
import { AuthService } from '../services/auth.service';

/** Sends anonymous visitors to the login page, remembering where they were going. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isAuthenticated() || router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Allows the route only for the given roles; anyone else lands on the bookings page. */
export const roleGuard =
  (...roles: UserRole[]): CanActivateFn =>
  (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isAuthenticated()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }
    return auth.hasRole(...roles) || router.createUrlTree(['/bookings']);
  };
