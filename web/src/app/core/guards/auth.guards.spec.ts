import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';

import { UserRole } from '../models/auth.models';
import { AuthService } from '../services/auth.service';
import { authGuard, roleGuard } from './auth.guards';

describe('route guards', () => {
  let signedInAs: UserRole | null;
  let router: Router;

  const state = (url: string) => ({ url }) as RouterStateSnapshot;
  const route = {} as ActivatedRouteSnapshot;
  const run = (guard: ReturnType<typeof roleGuard>, url: string) =>
    TestBed.runInInjectionContext(() => guard(route, state(url)));

  beforeEach(() => {
    signedInAs = null;
    TestBed.configureTestingModule({
      imports: [RouterTestingModule],
      providers: [
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: () => signedInAs !== null,
            hasRole: (...roles: UserRole[]) => signedInAs !== null && roles.includes(signedInAs),
          },
        },
      ],
    });
    router = TestBed.inject(Router);
  });

  describe('authGuard', () => {
    it('lets a signed-in user through', () => {
      signedInAs = 'Shipper';

      expect(run(authGuard, '/bookings')).toBeTrue();
    });

    it('redirects an anonymous visitor to login and remembers the destination', () => {
      const result = run(authGuard, '/bookings/5') as UrlTree;

      expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fbookings%2F5');
    });
  });

  describe('roleGuard', () => {
    it('allows the listed role', () => {
      signedInAs = 'Ops';

      expect(run(roleGuard('Ops'), '/containers')).toBeTrue();
    });

    it('sends a user with the wrong role to the bookings page', () => {
      signedInAs = 'Shipper';

      const result = run(roleGuard('Ops'), '/containers') as UrlTree;

      expect(router.serializeUrl(result)).toBe('/bookings');
    });

    it('sends an anonymous visitor to login rather than bookings', () => {
      const result = run(roleGuard('Ops'), '/vessels') as UrlTree;

      expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fvessels');
    });
  });
});
