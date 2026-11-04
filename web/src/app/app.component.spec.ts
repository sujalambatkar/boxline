import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { RouterTestingModule } from '@angular/router/testing';

import { environment } from '../environments/environment';
import { AppComponent } from './app.component';

describe('AppComponent', () => {
  let fixture: ComponentFixture<AppComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        RouterTestingModule,
        HttpClientTestingModule,
        NoopAnimationsModule,
        MatToolbarModule,
        MatSidenavModule,
        MatListModule,
        MatIconModule,
      ],
      declarations: [AppComponent],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  function createComponent(): void {
    fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
  }

  it('lists the four main navigation entries', () => {
    createComponent();
    const links = (fixture.nativeElement as HTMLElement).querySelectorAll('mat-nav-list a');

    expect(Array.from(links).map(a => a.textContent?.trim())).toEqual([
      'inventory_2Bookings',
      'location_searchingTracking',
      'directions_boatVessels',
      'request_quoteTariffs',
    ]);
  });

  it('shows the API as online when the health endpoint answers Healthy', fakeAsync(() => {
    createComponent();
    tick(0);
    http.expectOne(environment.healthUrl).flush('Healthy');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.status')?.textContent).toContain('API online');
    discardPeriodicTasks();
  }));

  it('shows the API as offline when the health endpoint fails', fakeAsync(() => {
    createComponent();
    tick(0);
    http.expectOne(environment.healthUrl).flush('down', { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.status')?.textContent).toContain('API offline');
    discardPeriodicTasks();
  }));
});
