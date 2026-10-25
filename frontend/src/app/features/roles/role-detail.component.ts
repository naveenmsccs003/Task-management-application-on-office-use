import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AuditEntry, PermissionModule, RoleDetail, RolePermissions } from '../../core/api/api.models';
import { RolesApi } from '../../core/api/roles.api';
import { AuthService } from '../../core/auth/auth.service';
import { Perm } from '../../core/auth/permissions';
import { toApiError } from '../../core/http/api-error';
import { ConfirmService } from '../../core/ui/confirm.service';
import { ToastService } from '../../core/ui/toast.service';
import { DeleteRoleDialogComponent } from './delete-role-dialog.component';
import { PermissionMatrixComponent } from './permission-matrix.component';

@Component({
  selector: 'app-role-detail',
  imports: [RouterLink, DatePipe, PermissionMatrixComponent, DeleteRoleDialogComponent],
  templateUrl: './role-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleDetailComponent implements OnInit {
  private readonly api = inject(RolesApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  protected readonly auth = inject(AuthService);
  protected readonly Perm = Perm;

  readonly id = input.required<string>();

  protected readonly role = signal<RoleDetail | null>(null);
  protected readonly modules = signal<PermissionModule[]>([]);
  protected readonly permissions = signal<RolePermissions | null>(null);
  protected readonly audit = signal<AuditEntry[] | null>(null);
  protected readonly tab = signal<'permissions' | 'history'>('permissions');
  protected readonly error = signal<string | null>(null);
  protected readonly deleting = signal(false);

  constructor() {
    const fragment = inject(ActivatedRoute).snapshot.fragment;
    if (fragment === 'history') this.tab.set('history');
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    forkJoin({ role: this.api.get(this.id()), modules: this.api.catalog(), permissions: this.api.permissions(this.id()) }).subscribe({
      next: ({ role, modules, permissions }) => {
        this.role.set(role);
        this.modules.set(modules);
        this.permissions.set(permissions);
      },
      error: (error: unknown) => this.error.set(toApiError(error, 'The role could not be loaded.').message),
    });
  }

  protected showHistory(): void {
    this.tab.set('history');
    if (this.audit() === null) {
      this.api.audit(this.id()).subscribe({ next: (entries) => this.audit.set(entries), error: () => this.audit.set([]) });
    }
  }

  protected onPermissionsSaved(ids: number[]): void {
    this.permissions.update((p) => (p ? { ...p, grantedPermissionIds: ids } : p));
    this.role.update((r) => (r ? { ...r, permissionCount: ids.length } : r));
    this.audit.set(null);
  }

  protected async toggleStatus(): Promise<void> {
    const role = this.role()!;
    const activate = !role.isActive;
    const ok = await this.confirm.confirm({
      title: activate ? 'Activate role' : 'Deactivate role',
      message: activate
        ? `Activate "${role.name}"?`
        : `Deactivate "${role.name}"? Its ${role.memberCount} member(s) lose the permissions granted by this role until it is reactivated.`,
      confirmText: activate ? 'Activate' : 'Deactivate',
      danger: !activate,
    });
    if (!ok) return;
    this.api.update(role.id, { name: role.name, description: role.description, isActive: activate }).subscribe({
      next: (updated) => {
        this.role.set(updated);
        this.audit.set(null);
        this.toasts.success(`Role ${activate ? 'activated' : 'deactivated'}.`);
      },
      error: (error: unknown) => this.toasts.error(toApiError(error, 'The role could not be updated.').message),
    });
  }

  protected onDeleted(): void {
    this.toasts.success(`Role "${this.role()?.name}" deleted.`);
    void this.router.navigate(['/roles']);
  }
}
