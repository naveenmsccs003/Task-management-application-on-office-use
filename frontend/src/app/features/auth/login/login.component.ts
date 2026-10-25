import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, map } from 'rxjs';
import { PASSWORD_MAX_LENGTH } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { safeReturnUrl } from '../../../core/auth/return-url';
import { toApiError } from '../../../core/http/api-error';

const NOTICES: Record<string, string> = {
  'session-expired': 'Your session has expired. Please sign in again.',
  'logged-out': 'You have been signed out.',
  'password-reset': 'Your password has been reset. Sign in with your new password.',
};

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: '../auth-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    userNameOrEmail: ['', [Validators.required, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.maxLength(PASSWORD_MAX_LENGTH)]],
    rememberMe: [false],
  });

  protected readonly submitting = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly reason = toSignal(this.route.queryParamMap.pipe(map((params) => params.get('reason'))));
  protected readonly notice = computed(() => {
    const reason = this.reason();
    return !this.error() && reason ? (NOTICES[reason] ?? null) : null;
  });

  protected togglePasswordVisibility(): void {
    this.showPassword.update((visible) => !visible);
  }

  protected isInvalid(name: 'userNameOrEmail' | 'password'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const request = this.form.getRawValue();
    this.error.set(null);
    this.submitting.set(true);
    this.form.disable();

    this.auth
      .login(request)
      .pipe(
        finalize(() => {
          this.submitting.set(false);
          this.form.enable();
        }),
      )
      .subscribe({
        next: () => {
          const returnUrl = safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
          void this.router.navigateByUrl(returnUrl);
        },
        error: (error: unknown) => {
          this.error.set(toApiError(error, 'Sign-in failed. Please try again.').message);
          this.form.controls.password.reset('');
        },
      });
  }
}
