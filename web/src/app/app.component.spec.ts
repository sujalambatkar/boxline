import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { BehaviorSubject } from 'rxjs';

import { environment } from '../environments/environment';
import { AppComponent } from './app.component';
import { User } from './core/models/auth.models';
import { AuthService } from './core/services/auth.service';

describe('AppComponent', () => {
  let fixture: ComponentFixture<AppComponent>;
  let http: HttpTestingController;
  let user$: BehaviorSubject<User | null>;
  let auth: { user$: BehaviorSubject<User | null>; logout: jasmine.Spy };

  const ops: User = { id: 1, email: 'ops@boxline.test', role: 'Ops', shipperCompany: null };
  const shipper: User = { id: 2, email: 's@acme.test', role: 'Shipper', shipperCompany: 'Acme Exports' };

  const el = () => fixture.nativeElement as HTMLElement;
  /** Each entry is the icon ligature followed by the label, e.g. "inventory_2Bookings". */
  const navLabels = () => Array.from(el().querySelectorAll('mat-nav-list a')).map(a => a.textContent?.trim());

  beforeEach(async () => {
    user$ = new BehaviorSubject<User | null>(null);
    auth = { user$, logout: jasmine.createSpy('logout') };

    await TestBed.configureTestingModule({
      imports: [
        RouterTestingModule,
        HttpClientTestingModule,
        NoopAnimationsModule,
        MatToolbarModule,
        MatSidenavModule,
        MatListModule,
        MatIconModule,
        MatButtonModule,
      ],
      declarations: [AppComponent],
      providers: [{ provide: AuthService, useValue: auth }],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  function create(): void {
    fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
  }

  it('shows no navigation and no user when signed out', () => {
    create();

    expect(navLabels()).toEqual([]);
    expect(el().querySelector('[data-testid="user"]')).toBeNull();
  });

  it('shows Ops every section', () => {
    create();
    user$.next(ops);
    fixture.detectChanges();

    expect(navLabels()).toEqual([
      'inventory_2Bookings',
      'view_in_arContainers',
      'location_searchingTracking',
      'directions_boatVessels',
      'request_quoteTariffs',
    ]);
    expect(el().querySelector('[data-testid="user"]')?.textContent).toContain('ops@boxline.test · Ops');
  });

  it('shows shippers only bookings and tracking, labelled with their company', () => {
    create();
    user$.next(shipper);
    fixture.detectChanges();

    expect(navLabels()).toEqual(['inventory_2Bookings', 'location_searchingTracking']);
    expect(el().querySelector('[data-testid="user"]')?.textContent).toContain('Acme Exports · Shipper');
  });

  it('signs out and returns to the login page', () => {
    create();
    user$.next(ops);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);

    (Array.from(el().querySelectorAll('button')).find(b => b.textContent?.includes('Sign out')) as HTMLButtonElement).click();

    expect(auth.logout).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('shows the API as online when the health endpoint answers Healthy', fakeAsync(() => {
    create();
    tick(0);
    http.expectOne(environment.healthUrl).flush('Healthy');
    fixture.detectChanges();

    expect(el().querySelector('.status')?.textContent).toContain('API online');
    discardPeriodicTasks();
  }));

  it('shows the API as offline when the health endpoint fails', fakeAsync(() => {
    create();
    tick(0);
    http.expectOne(environment.healthUrl).flush('down', { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();

    expect(el().querySelector('.status')?.textContent).toContain('API offline');
    discardPeriodicTasks();
  }));
});
