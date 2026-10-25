import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { passwordsMatchValidator } from '../../../core/auth/password-match.validator';
import { passwordStrength } from '../../../core/auth/password-strength';
import { toApiError } from '../../../core/http/api-error';

type Field = 'currentPassword' | 'newPassword' | 'confirmNewPassword';

@Component({
  selector: 'app-change-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss',
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
  protected readonly visible = signal<Record<Field, boolean>>({ currentPassword: false, newPassword: false, confirmNewPassword: false });

  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  protected readonly strength = computed(() => passwordStrength(this.values().newPassword ?? '', PASSWORD_MIN_LENGTH));

  /** Live checklist mirroring the server policy (the server remains authoritative). */
  protected readonly requirements = computed(() => {
    const { newPassword = '', confirmNewPassword = '' } = this.values();
    const user = this.auth.currentUser();
    const lower = newPassword.toLowerCase();
    const identifiers = [user?.userName, user?.email?.split('@')[0]].filter((v): v is string => !!v && v.length >= 3);
    return [
      { label: `At least ${PASSWORD_MIN_LENGTH} characters`, met: newPassword.length >= PASSWORD_MIN_LENGTH },
      { label: 'Does not contain your username or email', met: newPassword.length > 0 && !identifiers.some((id) => lower.includes(id.toLowerCase())) },
      { label: 'Different from your current password', met: newPassword.length > 0 && newPassword !== this.values().currentPassword },
      { label: 'Both new passwords match', met: newPassword.length > 0 && newPassword === confirmNewPassword },
    ];
  });

  protected invalid(name: Field): boolean {
    const control = this.form.controls[name];
    return (control.invalid && (control.touched || control.dirty)) || (this.serverErrors()[name]?.length ?? 0) > 0;
  }

  protected get mismatch(): boolean {
    return this.form.hasError('passwordMismatch') && this.form.controls.confirmNewPassword.touched;
  }

  protected toggle(field: Field): void {
    this.visible.update((state) => ({ ...state, [field]: !state[field] }));
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
