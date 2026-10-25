import { ChangeDetectionStrategy, Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { RoleSummary } from '../../core/api/api.models';
import { RolesApi } from '../../core/api/roles.api';
import { toApiError } from '../../core/http/api-error';
import { ModalComponent } from '../../core/ui/modal.component';

/** Deletes a role; when members still hold it, they must be moved to another role in the same step. */
@Component({
  selector: 'app-delete-role-dialog',
  imports: [ModalComponent, FormsModule],
  template: `
    <app-modal title="Delete role" icon="bi-trash3" [danger]="true" (closed)="closed.emit()">
      <p>Delete <strong>{{ role().name }}</strong>? This cannot be undone.</p>
      @if (role().memberCount > 0) {
        <div class="alert alert-warning border-warning">
          <i class="bi bi-people-fill" aria-hidden="true"></i>
          <span>{{ role().memberCount }} member(s) have this role. Choose the role they will be moved to.</span>
        </div>
        <label for="reassignRole" class="form-label">Move members to</label>
        <select id="reassignRole" class="form-select" [(ngModel)]="targetId" data-testid="reassign-select">
          <option value="">Select a role…</option>
          @for (option of targets(); track option.id) {
            <option [value]="option.id">{{ option.name }}</option>
          }
        </select>
      }
      @if (error(); as message) {
        <div class="alert alert-danger border-danger mt-3 mb-0" role="alert">
          <i class="bi bi-exclamation-octagon-fill" aria-hidden="true"></i><span>{{ message }}</span>
        </div>
      }
      <ng-container footer>
        <button type="button" class="btn btn-light border" (click)="closed.emit()">Cancel</button>
        <button type="button" class="btn btn-danger" data-testid="confirm-delete-role"
                [disabled]="busy() || (role().memberCount > 0 && !targetId)" (click)="remove()">
          @if (busy()) { <span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span> }
          Delete role
        </button>
      </ng-container>
    </app-modal>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeleteRoleDialogComponent implements OnInit {
  private readonly api = inject(RolesApi);
  readonly role = input.required<RoleSummary>();
  readonly closed = output<void>();
  readonly deleted = output<void>();

  protected targetId = '';
  protected readonly targets = signal<RoleSummary[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    if (this.role().memberCount > 0) {
      this.api.list({ status: 'active', pageSize: 100, sort: 'name' }).subscribe({
        next: (result) => this.targets.set(result.items.filter((r) => r.id !== this.role().id)),
      });
    }
  }

  protected remove(): void {
    this.busy.set(true);
    this.error.set(null);
    this.api
      .delete(this.role().id, this.targetId || undefined)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => this.deleted.emit(),
        error: (error: unknown) => this.error.set(toApiError(error, 'The role could not be deleted.').message),
      });
  }
}
