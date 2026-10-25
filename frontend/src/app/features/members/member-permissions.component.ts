import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { MemberPermissionItem, MemberPermissions, PermissionEffect } from '../../core/api/api.models';
import { MembersApi } from '../../core/api/members.api';
import { AuthService } from '../../core/auth/auth.service';
import { toApiError } from '../../core/http/api-error';
import { ConfirmService } from '../../core/ui/confirm.service';
import { ToastService } from '../../core/ui/toast.service';

type Choice = 'Inherit' | PermissionEffect;

interface Row extends MemberPermissionItem {
  choice: Choice;
  preview: boolean;
  previewSource: string;
  changed: boolean;
}

/**
 * Member-specific overrides. The effective column previews the result with the same rules as the server
 * (Deny wins, Allow only for overridable permissions, tab access required); the saved result always comes
 * back from the API.
 */
@Component({
  selector: 'app-member-permissions',
  imports: [FormsModule],
  templateUrl: './member-permissions.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MemberPermissionsComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  readonly memberId = input.required<string>();

  protected readonly data = signal<MemberPermissions | null>(null);
  protected readonly draft = signal<Map<number, Choice>>(new Map());
  protected readonly search = signal('');
  protected readonly filter = signal<'all' | 'overridden' | 'effective'>('all');
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.permissions(this.memberId()).subscribe({
      next: (data) => this.apply(data),
      error: (error: unknown) => this.error.set(toApiError(error, 'Permissions could not be loaded.').message),
    });
  }

  protected readonly rows = computed<Row[]>(() => {
    const data = this.data();
    if (!data) return [];
    const draft = this.draft();
    const granted = new Set<string>();
    for (const item of data.items) {
      const choice = draft.get(item.permissionId) ?? 'Inherit';
      const allowed = data.isAdministrator || (choice === 'Deny' ? false : choice === 'Allow' ? item.roleDefault || item.allowMemberOverride : item.roleDefault);
      if (allowed) granted.add(item.key);
    }
    return data.items.map((item) => {
      const choice = draft.get(item.permissionId) ?? 'Inherit';
      const access = `${item.moduleKey}.access`;
      const preview = granted.has(item.key) && (item.key === access || granted.has(access));
      const previewSource = data.isAdministrator ? 'Administrator role'
        : choice === 'Deny' ? 'Explicitly denied'
        : preview ? (choice === 'Allow' && !item.roleDefault ? 'Explicitly allowed' : 'Inherited from role')
        : granted.has(item.key) ? 'Requires tab access' : 'Not granted';
      return { ...item, choice, preview, previewSource, changed: choice !== (item.override ?? 'Inherit') };
    });
  });

  protected readonly visibleGroups = computed(() => {
    const term = this.search().trim().toLowerCase();
    const filter = this.filter();
    const rows = this.rows().filter((r) =>
      (!term || `${r.displayName} ${r.key} ${r.moduleName}`.toLowerCase().includes(term))
      && (filter === 'all' || (filter === 'overridden' ? r.choice !== 'Inherit' : r.preview)));
    const groups = new Map<string, Row[]>();
    for (const row of rows) groups.set(row.moduleName, [...(groups.get(row.moduleName) ?? []), row]);
    return [...groups.entries()].map(([name, items]) => ({ name, items }));
  });

  protected readonly changes = computed(() => this.rows().filter((r) => r.changed));
  protected readonly overrideCount = computed(() => this.rows().filter((r) => r.choice !== 'Inherit').length);
  protected readonly effectiveCount = computed(() => this.rows().filter((r) => r.preview).length);

  /** Allow is offered only for overridable permissions the current user holds (delegation scope). */
  protected canAllow(row: Row): boolean {
    return row.allowMemberOverride && (this.auth.currentUser()?.isAdministrator === true || this.auth.can(row.key));
  }

  protected setChoice(row: Row, choice: Choice): void {
    this.draft.update((map) => {
      const next = new Map(map);
      if (choice === 'Inherit') next.delete(row.permissionId);
      else next.set(row.permissionId, choice);
      return next;
    });
  }

  protected reset(): void {
    const data = this.data();
    if (data) this.apply(data);
  }

  protected async save(): Promise<void> {
    const details = this.changes().map((r) => `${r.key}: ${r.override ?? 'Inherit'} → ${r.choice}`);
    const ok = await this.confirm.confirm({
      title: 'Save member permissions',
      message: 'These overrides apply only to this member and take effect immediately.',
      details,
      confirmText: 'Save overrides',
    });
    if (!ok) return;
    const overrides = [...this.draft().entries()]
      .filter(([, effect]) => effect !== 'Inherit')
      .map(([permissionId, effect]) => ({ permissionId, effect: effect as PermissionEffect }));
    this.saving.set(true);
    this.api
      .savePermissions(this.memberId(), overrides)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (data) => {
          this.apply(data);
          this.toasts.success('Member permissions saved.');
        },
        error: (error: unknown) => this.toasts.error(toApiError(error, 'The permissions could not be saved.').message),
      });
  }

  private apply(data: MemberPermissions): void {
    this.data.set(data);
    this.draft.set(new Map(data.items.filter((i) => i.override).map((i) => [i.permissionId, i.override as Choice])));
  }
}
