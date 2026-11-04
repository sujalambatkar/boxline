import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, map, Observable, of, switchMap, timer } from 'rxjs';

import { environment } from '../environments/environment';
import { UserRole } from './core/models/auth.models';
import { AuthService } from './core/services/auth.service';

interface NavItem {
  label: string;
  icon: string;
  path: string;
  /** Roles that see this entry; omitted means everyone signed in. */
  roles?: UserRole[];
}

const NAV_ITEMS: NavItem[] = [
  { label: 'Bookings', icon: 'inventory_2', path: '/bookings' },
  { label: 'Containers', icon: 'view_in_ar', path: '/containers', roles: ['Ops'] },
  { label: 'Tracking', icon: 'location_searching', path: '/tracking' },
  { label: 'Vessels', icon: 'directions_boat', path: '/vessels', roles: ['Ops'] },
  { label: 'Tariffs', icon: 'request_quote', path: '/tariffs', roles: ['Ops'] },
];

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
})
export class AppComponent implements OnInit {
  readonly user$ = this.auth.user$;

  readonly navItems$: Observable<NavItem[]> = this.auth.user$.pipe(
    map(user => (user ? NAV_ITEMS.filter(item => !item.roles || item.roles.includes(user.role)) : [])),
  );

  apiOnline$!: Observable<boolean>;

  constructor(
    private readonly http: HttpClient,
    private readonly auth: AuthService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.apiOnline$ = timer(0, 15000).pipe(
      switchMap(() =>
        this.http.get(environment.healthUrl, { responseType: 'text' }).pipe(
          map(status => status === 'Healthy'),
          catchError(() => of(false)),
        ),
      ),
    );
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
