import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { RoleDetail } from '../../core/api/api.models';
import { RolesApi } from '../../core/api/roles.api';
import { toApiError } from '../../core/http/api-error';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'app-role-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './role-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleFormComponent implements OnInit {
  private readonly api = inject(RolesApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  /** Route parameter (bound by the router); absent when creating. */
  readonly id = input<string>();

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50)]],
    description: ['', [Validators.maxLength(250)]],
    isActive: [true],
  });
  protected readonly role = signal<RoleDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Partial<Record<string, string[]>>>({});

  ngOnInit(): void {
    const id = this.id();
    if (!id) return;
    this.loading.set(true);
    this.api
      .get(id)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (role) => {
          this.role.set(role);
          this.form.setValue({ name: role.name, description: role.description ?? '', isActive: role.isActive });
          if (role.isSystem) {
            this.form.controls.name.disable();
            this.form.controls.isActive.disable();
          }
        },
        error: (error: unknown) => this.error.set(toApiError(error, 'The role could not be loaded.').message),
      });
  }

  protected invalid(name: 'name' | 'description'): boolean {
    const control = this.form.controls[name];
    return (control.invalid && (control.touched || control.dirty)) || !!this.serverErrors()[name]?.length;
  }

  protected submit(): void {
    if (this.saving()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const request = { name: value.name, description: value.description || null, isActive: value.isActive };
    const id = this.id();
    this.saving.set(true);
    this.error.set(null);
    this.serverErrors.set({});
    (id ? this.api.update(id, request) : this.api.create(request))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (role) => {
          this.toasts.success(id ? `Role "${role.name}" updated.` : `Role "${role.name}" created. Now choose its permissions.`);
          void this.router.navigate(['/roles', role.id], { fragment: id ? undefined : 'permissions' });
        },
        error: (error: unknown) => {
          const apiError = toApiError(error, 'The role could not be saved.');
          this.serverErrors.set(apiError.fieldErrors);
          this.error.set(apiError.message);
        },
      });
  }
}
