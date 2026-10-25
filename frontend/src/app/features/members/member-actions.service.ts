import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MemberDetail, MemberSummary } from '../../core/api/api.models';
import { MembersApi } from '../../core/api/members.api';
import { AuthService } from '../../core/auth/auth.service';
import { Perm } from '../../core/auth/permissions';
import { toApiError } from '../../core/http/api-error';
import { ConfirmService } from '../../core/ui/confirm.service';
import { ToastService } from '../../core/ui/toast.service';

/** Minimal member shape needed for row/detail actions. */
export type MemberRef = Pick<MemberSummary, 'id' | 'displayName' | 'role' | 'isActive'> & { hasPassword?: boolean };

export interface MemberActionSet {
  changeRole: boolean;
  resetPassword: boolean;
  activate: boolean;
  deactivate: boolean;
  delete: boolean;
}

/**
 * Confirmed member actions shared by the list and the detail page. Visibility mirrors the API rules
 * (permission, not yourself, administrators only by administrators); the API enforces them regardless.
 */
@Injectable({ providedIn: 'root' })
export class MemberActionsService {
  private readonly api = inject(MembersApi);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  available(member: MemberRef): MemberActionSet {
    const me = this.auth.currentUser();
    const manageable = !!me && me.id !== member.id && (me.isAdministrator || !member.role?.isSystem);
    return {
      changeRole: manageable && this.auth.can(Perm.members.changeRole),
      resetPassword: manageable && member.isActive && this.auth.can(Perm.members.resetPassword),
      activate: manageable && !member.isActive && this.auth.can(Perm.members.activate),
      deactivate: manageable && member.isActive && this.auth.can(Perm.members.deactivate),
      delete: manageable && !member.isActive && this.auth.can(Perm.members.delete),
    };
  }

  /** Returns the updated member, or null when cancelled or rejected. */
  async setStatus(member: MemberRef, active: boolean): Promise<MemberDetail | null> {
    const ok = await this.confirm.confirm({
      title: active ? 'Activate member' : 'Deactivate member',
      message: active
        ? `Activate ${member.displayName}? They will be able to sign in again.`
        : `Deactivate ${member.displayName}? They will be signed out everywhere immediately and cannot sign in until reactivated.`,
      confirmText: active ? 'Activate' : 'Deactivate',
      danger: !active,
    });
    if (!ok) return null;
    try {
      const updated = await firstValueFrom(this.api.setStatus(member.id, active));
      this.toasts.success(`${updated.displayName} ${active ? 'activated' : 'deactivated'}.`);
      return updated;
    } catch (error) {
      this.toasts.error(toApiError(error, 'The status could not be changed.').message);
      return null;
    }
  }

  async resetPassword(member: MemberRef): Promise<boolean> {
    const invite = member.hasPassword === false;
    const ok = await this.confirm.confirm({
      title: invite ? 'Resend invitation' : 'Send password link',
      message: invite
        ? `Email ${member.displayName} a new invitation link to set their password?`
        : `Email ${member.displayName} a secure link to set a new password? (If they were invited and have no password yet, the invitation is sent again.)`,
      confirmText: 'Send link',
    });
    if (!ok) return false;
    try {
      const result = await firstValueFrom(this.api.resetPassword(member.id));
      this.toasts.success(result.message);
      return true;
    } catch (error) {
      this.toasts.error(toApiError(error, 'The link could not be sent.').message);
      return false;
    }
  }

  async remove(member: MemberRef): Promise<boolean> {
    const ok = await this.confirm.confirm({
      title: 'Delete member',
      message: `Permanently delete ${member.displayName}'s account? This cannot be undone. The audit history is kept.`,
      confirmText: 'Delete permanently',
      danger: true,
    });
    if (!ok) return false;
    try {
      await firstValueFrom(this.api.delete(member.id));
      this.toasts.success(`${member.displayName} deleted.`);
      return true;
    } catch (error) {
      this.toasts.error(toApiError(error, 'The member could not be deleted.').message);
      return false;
    }
  }
}
