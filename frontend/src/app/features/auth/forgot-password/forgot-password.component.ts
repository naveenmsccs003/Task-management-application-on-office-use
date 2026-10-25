import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { toApiError } from '../../../core/http/api-error';
import { AuthBrandComponent } from '../auth-brand/auth-brand.component';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, AuthBrandComponent],
  templateUrl: './forgot-password.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
  });

  protected readonly submitting = signal(false);
  protected readonly confirmation = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected get emailInvalid(): boolean {
    const control = this.form.controls.email;
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

    this.error.set(null);
    this.submitting.set(true);
    this.auth
      .forgotPassword(this.form.getRawValue())
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (message) => this.confirmation.set(message),
        error: (error: unknown) => this.error.set(toApiError(error, 'The request could not be sent. Please try again.').message),
      });
  }
}
