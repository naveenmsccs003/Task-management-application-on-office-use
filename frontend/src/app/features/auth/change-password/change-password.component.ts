import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { passwordsMatchValidator } from '../../../core/auth/password-match.validator';
import { toApiError } from '../../../core/http/api-error';

type Field = 'currentPassword' | 'newPassword' | 'confirmNewPassword';

@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule],
  templateUrl: './change-password.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangePasswordComponent {
  private readonly auth = inject(AuthService);

  protected readonly minLength = PASSWORD_MIN_LENGTH;
  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(PASSWORD_MAX_LENGTH)]],
      confirmNewPassword: ['', [Validators.required]],
    },
    { validators: passwordsMatchValidator },
  );

  protected readonly submitting = signal(false);
  protected readonly success = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Partial<Record<Field, string[]>>>({});

  protected invalid(name: Field): boolean {
    const control = this.form.controls[name];
    return (control.invalid && (control.touched || control.dirty)) || (this.serverErrors()[name]?.length ?? 0) > 0;
  }

  protected get mismatch(): boolean {
    return this.form.hasError('passwordMismatch') && this.form.controls.confirmNewPassword.touched;
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.success.set(false);
    this.error.set(null);
    this.serverErrors.set({});
    this.submitting.set(true);
    this.auth
      .changePassword(this.form.getRawValue())
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => {
          this.success.set(true);
          this.form.reset();
        },
        error: (error: unknown) => {
          const apiError = toApiError(error, 'Your password could not be changed. Please try again.');
          this.serverErrors.set(apiError.fieldErrors);
          this.error.set(apiError.message);
        },
      });
  }
}
