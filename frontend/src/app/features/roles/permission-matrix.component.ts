import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { PermissionModule } from '../../core/api/api.models';
import { RolesApi } from '../../core/api/roles.api';
import { AuthService } from '../../core/auth/auth.service';
import { toApiError } from '../../core/http/api-error';
import { ConfirmService } from '../../core/ui/confirm.service';
import { ToastService } from '../../core/ui/toast.service';

/**
 * Role permission configuration. Checkbox state is only a draft: the saved configuration is validated and
 * stored by the API, which remains the source of truth.
 */
@Component({
  selector: 'app-permission-matrix',
  imports: [FormsModule],
  templateUrl: './permission-matrix.component.html',
  styleUrl: './permission-matrix.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PermissionMatrixComponent {
  private readonly api = inject(RolesApi);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  readonly roleId = input.required<string>();
  readonly modules = input.required<PermissionModule[]>();
  readonly granted = input.required<number[]>();
  readonly editable = input(false);
  readonly isSystem = input(false);
  readonly memberCount = input(0);
  readonly saved = output<number[]>();

  protected readonly selected = signal<Set<number>>(new Set());
  protected readonly search = signal('');
  protected readonly expanded = signal<Set<string>>(new Set());
  protected readonly saving = signal(false);

  constructor() {
    effect(() => {
      this.selected.set(new Set(this.granted()));
      this.expanded.set(new Set(this.modules().filter((m) => m.isImplemented).map((m) => m.key)));
    });
  }

  private readonly assignable = computed(() => this.modules().flatMap((m) => m.permissions.filter((p) => p.isActive)));
  protected readonly selectedCount = computed(() => this.assignable().filter((p) => this.selected().has(p.id)).length);
  protected readonly totalCount = computed(() => this.assignable().length);

  protected readonly visibleModules = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.modules()
      .map((module) => ({
        ...module,
        permissions: term
          ? module.permissions.filter((p) => `${p.displayName} ${p.key} ${p.description} ${module.name}`.toLowerCase().includes(term))
          : module.permissions,
      }))
      .filter((module) => module.permissions.length > 0);
  });

  protected readonly diff = computed(() => {
    const initial = new Set(this.granted());
    const current = this.selected();
    const byId = new Map(this.assignable().map((p) => [p.id, p]));
    return {
      added: [...current].filter((id) => !initial.has(id)).map((id) => byId.get(id)!),
      removed: [...initial].filter((id) => !current.has(id)).map((id) => byId.get(id)!).filter(Boolean),
    };
  });
  protected readonly dirty = computed(() => this.diff().added.length + this.diff().removed.length > 0);

  protected moduleState(module: PermissionModule): 'all' | 'some' | 'none' {
    const ids = this.fullModule(module).permissions.filter((p) => p.isActive).map((p) => p.id);
    const count = ids.filter((id) => this.selected().has(id)).length;
    return count === 0 ? 'none' : count === ids.length ? 'all' : 'some';
  }

  protected moduleSelectedCount(module: PermissionModule): number {
    return this.fullModule(module).permissions.filter((p) => this.selected().has(p.id)).length;
  }

  protected toggleExpanded(key: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  protected setExpandedAll(open: boolean): void {
    this.expanded.set(open ? new Set(this.modules().map((m) => m.key)) : new Set());
  }

  /** Convenience only: selects or clears every available permission of a module. */
  protected setModule(module: PermissionModule, on: boolean): void {
    const full = this.fullModule(module);
    this.mutate((set) => full.permissions.filter((p) => p.isActive).forEach((p) => (on ? set.add(p.id) : set.delete(p.id))));
  }

  protected setAll(on: boolean): void {
    this.mutate((set) => this.assignable().forEach((p) => (on ? set.add(p.id) : set.delete(p.id))));
  }

  /**
   * Checking an operation also checks the module's tab access (an operation is ineffective without it);
   * clearing tab access clears the module's operations.
   */
  protected toggle(visible: PermissionModule, permissionId: number, on: boolean): void {
    // Use the unfiltered module so search results never hide children from the parent rule.
    const module = this.fullModule(visible);
    const permission = module.permissions.find((p) => p.id === permissionId)!;
    this.mutate((set) => {
      if (on) {
        set.add(permissionId);
        if (permission.parentPermissionId) set.add(permission.parentPermissionId);
      } else {
        set.delete(permissionId);
        if (permission.parentPermissionId === null) {
          module.permissions.forEach((p) => set.delete(p.id));
        }
      }
    });
  }

  protected reset(): void {
    this.selected.set(new Set(this.granted()));
  }

  protected async save(): Promise<void> {
    const { added, removed } = this.diff();
    const details = [
      ...(added.length ? [`Grant ${added.length}: ${added.map((p) => p.key).join(', ')}`] : []),
      ...(removed.length ? [`Remove ${removed.length}: ${removed.map((p) => p.key).join(', ')}`] : []),
      `Applies immediately to ${this.memberCount()} member(s) with this role.`,
    ];
    const ok = await this.confirm.confirm({
      title: 'Save role permissions',
      message: 'Review the permission changes before saving.',
      details,
      confirmText: 'Save permissions',
      danger: removed.length > 0,
    });
    if (!ok) return;

    const ids = [...this.selected()].filter((id) => this.assignable().some((p) => p.id === id));
    this.saving.set(true);
    this.api
      .savePermissions(this.roleId(), ids)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (change) => {
          this.toasts.success(`Permissions saved (${change.added.length} added, ${change.removed.length} removed).`);
          this.saved.emit(ids);
          void this.auth.refreshStatus();
        },
        error: (error: unknown) => this.toasts.error(toApiError(error, 'The permissions could not be saved.').message),
      });
  }

  private fullModule(module: PermissionModule): PermissionModule {
    return this.modules().find((m) => m.key === module.key) ?? module;
  }

  private mutate(change: (set: Set<number>) => void): void {
    if (!this.editable()) return;
    const next = new Set(this.selected());
    change(next);
    this.selected.set(next);
  }
}
