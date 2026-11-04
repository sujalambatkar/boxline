import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { NonNullableFormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss'],
})
export class LoginComponent {
  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  submitting = false;
  error: string | null = null;
  hidePassword = true;

  constructor(
    private readonly fb: NonNullableFormBuilder,
    private readonly auth: AuthService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {
    if (this.auth.isAuthenticated()) {
      void this.router.navigateByUrl(this.returnUrl);
    }
  }

  submit(): void {
    if (this.form.invalid || this.submitting) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, password } = this.form.getRawValue();
    this.submitting = true;
    this.error = null;

    this.auth.login(email, password).subscribe({
      next: () => void this.router.navigateByUrl(this.returnUrl),
      error: (error: HttpErrorResponse) => {
        this.submitting = false;
        this.error =
          error.status === 401
            ? 'Invalid e-mail or password.'
            : 'Could not reach the server. Check that the API is running.';
      },
    });
  }

  /** Only same-site paths are honoured, so a crafted link cannot redirect users elsewhere. */
  private get returnUrl(): string {
    const target = this.route.snapshot.queryParamMap.get('returnUrl');
    return target && target.startsWith('/') && !target.startsWith('//') && !target.startsWith('/login') ? target : '/bookings';
  }
}
