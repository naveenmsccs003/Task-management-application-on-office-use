import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { passwordsMatchValidator } from '../../../core/auth/password-match.validator';
import { toApiError } from '../../../core/http/api-error';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.component.html',
  styleUrl: '../auth-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly minLength = PASSWORD_MIN_LENGTH;
  // The link's parameters are read once, then removed from the address bar and history.
  private readonly userId: string | null;
  private readonly token: string | null;

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH)]],
      confirmNewPassword: ['', [Validators.required]],
    },
    { validators: passwordsMatchValidator },
  );

  protected readonly submitting = signal(false);
  protected readonly linkInvalid = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverPasswordErrors = signal<string[]>([]);

  constructor() {
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    this.userId = params.get('uid');
    this.token = params.get('token');
    this.linkInvalid.set(!this.userId || !this.token);
    inject(Location).replaceState('/reset-password');
  }

  protected invalid(name: 'newPassword' | 'confirmNewPassword'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected get mismatch(): boolean {
    return this.form.hasError('passwordMismatch') && this.form.controls.confirmNewPassword.touched;
  }

  protected submit(): void {
    if (this.submitting() || this.linkInvalid()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.serverPasswordErrors.set([]);
    this.submitting.set(true);
    this.auth
      .resetPassword({ userId: this.userId!, token: this.token!, ...this.form.getRawValue() })
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => void this.router.navigate(['/login'], { queryParams: { reason: 'password-reset' } }),
        error: (error: unknown) => {
          const apiError = toApiError(error, 'Your password could not be reset. Please try again.');
          if (apiError.fieldErrors['token']) {
            this.linkInvalid.set(true);
            return;
          }
          this.serverPasswordErrors.set(apiError.fieldErrors['newPassword'] ?? []);
          this.error.set(apiError.message);
        },
      });
  }
}
