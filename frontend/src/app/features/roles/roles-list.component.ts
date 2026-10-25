import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import { ListQuery, PagedResult, RoleSummary } from '../../core/api/api.models';
import { RolesApi } from '../../core/api/roles.api';
import { AuthService } from '../../core/auth/auth.service';
import { Perm } from '../../core/auth/permissions';
import { toApiError } from '../../core/http/api-error';
import { ConfirmService } from '../../core/ui/confirm.service';
import { PaginationComponent } from '../../core/ui/pagination.component';
import { ToastService } from '../../core/ui/toast.service';
import { DeleteRoleDialogComponent } from './delete-role-dialog.component';

@Component({
  selector: 'app-roles-list',
  imports: [RouterLink, FormsModule, DatePipe, PaginationComponent, DeleteRoleDialogComponent],
  templateUrl: './roles-list.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RolesListComponent {
  private readonly api = inject(RolesApi);
  private readonly toasts = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  protected readonly auth = inject(AuthService);
  protected readonly Perm = Perm;

  protected readonly query = signal<ListQuery>({ search: '', status: '', sort: 'name', direction: 'asc', page: 1, pageSize: 10 });
  protected readonly result = signal<PagedResult<RoleSummary> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly deleting = signal<RoleSummary | null>(null);

  constructor() {
    toObservable(this.query)
      .pipe(
        debounceTime(200),
        tap(() => this.loading.set(true)),
        switchMap((query) =>
          this.api.list(query).pipe(
            catchError((error: unknown) => {
              this.error.set(toApiError(error, 'Roles could not be loaded.').message);
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.error.set(null);
          this.result.set(result);
        }
      });
  }

  protected update(patch: Partial<ListQuery>): void {
    this.query.update((q) => ({ ...q, page: 1, ...patch }));
  }

  protected sortBy(column: string): void {
    this.query.update((q) => ({
      ...q,
      sort: column,
      direction: q.sort === column && q.direction === 'asc' ? 'desc' : 'asc',
      page: 1,
    }));
  }

  protected sortIcon(column: string): string {
    const q = this.query();
    return q.sort !== column ? 'bi-arrow-down-up' : q.direction === 'asc' ? 'bi-sort-up' : 'bi-sort-down';
  }

  protected async toggleStatus(role: RoleSummary): Promise<void> {
    const activate = !role.isActive;
    const ok = await this.confirm.confirm({
      title: activate ? 'Activate role' : 'Deactivate role',
      message: activate
        ? `Activate "${role.name}"? Its members will receive its permissions again.`
        : `Deactivate "${role.name}"? Its ${role.memberCount} member(s) will lose every permission granted by this role until it is reactivated.`,
      confirmText: activate ? 'Activate' : 'Deactivate',
      danger: !activate,
    });
    if (!ok) return;
    this.api.update(role.id, { name: role.name, description: role.description, isActive: activate }).subscribe({
      next: () => {
        this.toasts.success(`Role "${role.name}" ${activate ? 'activated' : 'deactivated'}.`);
        this.reload();
      },
      error: (error: unknown) => this.toasts.error(toApiError(error, 'The role could not be updated.').message),
    });
  }

  protected onDeleted(): void {
    this.toasts.success(`Role "${this.deleting()?.name}" deleted.`);
    this.deleting.set(null);
    this.reload();
  }

  private reload(): void {
    this.query.update((q) => ({ ...q }));
  }
}
