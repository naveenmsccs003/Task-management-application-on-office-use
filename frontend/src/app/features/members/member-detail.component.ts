import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuditEntry, MemberDetail } from '../../core/api/api.models';
import { MembersApi } from '../../core/api/members.api';
import { AuthService } from '../../core/auth/auth.service';
import { Perm } from '../../core/auth/permissions';
import { toApiError } from '../../core/http/api-error';
import { initials } from '../../core/ui/format';
import { ToastService } from '../../core/ui/toast.service';
import { ChangeRoleDialogComponent } from './change-role-dialog.component';
import { MemberActionsService } from './member-actions.service';
import { MemberPermissionsComponent } from './member-permissions.component';

type Tab = 'overview' | 'permissions' | 'history';

@Component({
  selector: 'app-member-detail',
  imports: [RouterLink, DatePipe, ChangeRoleDialogComponent, MemberPermissionsComponent],
  templateUrl: './member-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MemberDetailComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  protected readonly auth = inject(AuthService);
  protected readonly Perm = Perm;
  protected readonly initials = initials;

  readonly id = input.required<string>();

  protected readonly member = signal<MemberDetail | null>(null);
  protected readonly audit = signal<AuditEntry[] | null>(null);
  protected readonly tab = signal<Tab>('overview');
  protected readonly error = signal<string | null>(null);
  protected readonly changingRole = signal(false);
  protected readonly busy = signal(false);
  protected readonly photoVersion = Date.now();

  private readonly memberActions = inject(MemberActionsService);

  /** Actions the current user may take on this member (the API enforces the same rules). */
  protected readonly actions = computed(() => {
    const m = this.member();
    if (!m) return { edit: false, changeRole: false, resetPassword: false, activate: false, deactivate: false, delete: false };
    return {
      edit: m.canManage && this.auth.canAny(Perm.members.edit, Perm.members.changeEmail, Perm.members.editPersonal),
      ...this.memberActions.available(m),
    };
  });

  constructor() {
    const fragment = inject(ActivatedRoute).snapshot.fragment;
    if (fragment === 'permissions' || fragment === 'history') this.tab.set(fragment);
  }

  ngOnInit(): void {
    this.api.get(this.id()).subscribe({
      next: (member) => {
        this.member.set(member);
        if (this.tab() === 'history') this.loadAudit();
      },
      error: (error: unknown) => this.error.set(toApiError(error, 'The member could not be loaded.').message),
    });
  }

  protected select(tab: Tab): void {
    this.tab.set(tab);
    if (tab === 'history' && this.audit() === null) this.loadAudit();
  }

  protected photoUrl(): string {
    return this.api.photoUrl(this.id(), this.photoVersion);
  }

  protected onRoleChanged(member: MemberDetail): void {
    this.member.set(member);
    this.changingRole.set(false);
    this.audit.set(null);
    this.toasts.success(`Role changed to ${member.role?.name}.`);
  }

  protected async setStatus(active: boolean): Promise<void> {
    this.busy.set(true);
    const updated = await this.memberActions.setStatus(this.member()!, active);
    this.busy.set(false);
    if (updated) {
      this.member.set(updated);
      this.audit.set(null);
    }
  }

  protected async resetPassword(): Promise<void> {
    if (await this.memberActions.resetPassword(this.member()!)) this.audit.set(null);
  }

  protected async remove(): Promise<void> {
    if (await this.memberActions.remove(this.member()!)) void this.router.navigate(['/members']);
  }

  private loadAudit(): void {
    this.api.audit(this.id()).subscribe({ next: (entries) => this.audit.set(entries), error: () => this.audit.set([]) });
  }
}
