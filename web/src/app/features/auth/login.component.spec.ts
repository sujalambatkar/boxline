import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatCardModule } from '@angular/material/card';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of, throwError } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';
import { SharedModule } from '../../shared/shared.module';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let auth: { login: jasmine.Spy; isAuthenticated: jasmine.Spy };
  let router: Router;

  function create(returnUrl: string | null = null, signedIn = false): void {
    auth = {
      login: jasmine.createSpy('login').and.returnValue(of({ id: 1 })),
      isAuthenticated: jasmine.createSpy('isAuthenticated').and.returnValue(signedIn),
    };
    TestBed.configureTestingModule({
      declarations: [LoginComponent],
      imports: [SharedModule, MatCardModule, NoopAnimationsModule, RouterTestingModule],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(returnUrl ? { returnUrl } : {}) } } },
      ],
    });
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('does not call the API until the form is valid', () => {
    create();

    component.form.patchValue({ email: 'not-an-email', password: '' });
    component.submit();

    expect(auth.login).not.toHaveBeenCalled();
    expect(component.form.controls.email.hasError('email')).toBeTrue();
  });

  it('signs in and goes to the bookings page by default', () => {
    create();
    component.form.setValue({ email: 'ops@boxline.test', password: 'secret' });

    component.submit();

    expect(auth.login).toHaveBeenCalledWith('ops@boxline.test', 'secret');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/bookings');
  });

  it('returns the user to the page they were heading for', () => {
    create('/bookings/42');
    component.form.setValue({ email: 'a@b.co', password: 'x' });

    component.submit();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/bookings/42');
  });

  it('ignores a return URL that points off-site', () => {
    create('//evil.example.com');
    component.form.setValue({ email: 'a@b.co', password: 'x' });

    component.submit();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/bookings');
  });

  it('shows a clear message for a wrong password and lets the user retry', () => {
    create();
    auth.login.and.returnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    component.form.setValue({ email: 'a@b.co', password: 'wrong' });

    component.submit();
    fixture.detectChanges();

    expect(component.error).toBe('Invalid e-mail or password.');
    expect(component.submitting).toBeFalse();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Invalid e-mail or password.');
  });

  it('says the server is unreachable when there is no response', () => {
    create();
    auth.login.and.returnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    component.form.setValue({ email: 'a@b.co', password: 'x' });

    component.submit();

    expect(component.error).toContain('Could not reach the server');
  });

  it('skips the form when already signed in', () => {
    create(null, true);

    expect(router.navigateByUrl).toHaveBeenCalledWith('/bookings');
  });
});
