import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { catchError, map, Observable, of, timer, switchMap } from 'rxjs';

import { environment } from '../environments/environment';

interface NavItem {
  label: string;
  icon: string;
  path: string;
}

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss'],
})
export class AppComponent implements OnInit {
  readonly navItems: NavItem[] = [
    { label: 'Bookings', icon: 'inventory_2', path: '/bookings' },
    { label: 'Tracking', icon: 'location_searching', path: '/tracking' },
    { label: 'Vessels', icon: 'directions_boat', path: '/vessels' },
    { label: 'Tariffs', icon: 'request_quote', path: '/tariffs' },
  ];

  apiOnline$!: Observable<boolean>;

  constructor(private readonly http: HttpClient) {}

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
}
