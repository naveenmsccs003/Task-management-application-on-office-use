import { ChangeDetectionStrategy, Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { MemberDetail, RoleOption } from '../../core/api/api.models';
import { MemberRef } from './member-actions.service';
import { MembersApi } from '../../core/api/members.api';
import { RolesApi } from '../../core/api/roles.api';
import { toApiError } from '../../core/http/api-error';
import { ModalComponent } from '../../core/ui/modal.component';

@Component({
  selector: 'app-change-role-dialog',
  imports: [ModalComponent, FormsModule],
  template: `
    <app-modal title="Change role" icon="bi-person-badge" (closed)="closed.emit()">
      <p class="mb-3">Choose the new role for <strong>{{ member().displayName }}</strong>. Their permissions change immediately; member-specific overrides are kept.</p>
      <label for="newRole" class="form-label">Role</label>
      <select id="newRole" class="form-select" [(ngModel)]="roleId" data-testid="role-select">
        @for (role of roles(); track role.id) {
          <option [value]="role.id" [disabled]="!role.isAssignable">{{ role.name }}{{ role.isAssignable ? '' : ' (not permitted)' }}</option>
        }
      </select>
      <p class="form-text fs-xs mb-0">You can only assign active roles whose permissions you hold yourself.</p>
      @if (error(); as message) {
        <div class="alert alert-danger border-danger mt-3 mb-0" role="alert"><i class="bi bi-exclamation-octagon-fill" aria-hidden="true"></i><span>{{ message }}</span></div>
      }
      <ng-container footer>
        <button type="button" class="btn btn-light border" (click)="closed.emit()">Cancel</button>
        <button type="button" class="btn btn-gradient" [disabled]="busy() || !roleId || roleId === member().role?.id" (click)="save()" data-testid="confirm-change-role">
          @if (busy()) { <span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span> } Change role
        </button>
      </ng-container>
    </app-modal>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangeRoleDialogComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly rolesApi = inject(RolesApi);
  readonly member = input.required<MemberRef>();
  readonly closed = output<void>();
  readonly changed = output<MemberDetail>();

  protected roleId = '';
  protected readonly roles = signal<RoleOption[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.roleId = this.member().role?.id ?? '';
    this.rolesApi.assignable().subscribe({ next: (roles) => this.roles.set(roles) });
  }

  protected save(): void {
    this.busy.set(true);
    this.error.set(null);
    this.api
      .changeRole(this.member().id, this.roleId)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: (member) => this.changed.emit(member),
        error: (error: unknown) => this.error.set(toApiError(error, 'The role could not be changed.').message),
      });
  }
}
