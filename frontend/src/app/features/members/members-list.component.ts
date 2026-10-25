import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import { MemberListQuery, MemberSummary, PagedResult, RoleOption } from '../../core/api/api.models';
import { MembersApi } from '../../core/api/members.api';
import { RolesApi } from '../../core/api/roles.api';
import { AuthService } from '../../core/auth/auth.service';
import { Perm } from '../../core/auth/permissions';
import { toApiError } from '../../core/http/api-error';
import { downloadBlob, initials } from '../../core/ui/format';
import { PaginationComponent } from '../../core/ui/pagination.component';
import { ToastService } from '../../core/ui/toast.service';
import { ChangeRoleDialogComponent } from './change-role-dialog.component';
import { MemberActionsService } from './member-actions.service';

@Component({
  selector: 'app-members-list',
  imports: [RouterLink, FormsModule, DatePipe, PaginationComponent, ChangeRoleDialogComponent],
  templateUrl: './members-list.component.html',
  styleUrl: './members-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'openMenu.set(null)',
    '(window:scroll)': 'openMenu.set(null)',
    '(window:resize)': 'openMenu.set(null)',
  },
})
export class MembersListComponent {
  private readonly api = inject(MembersApi);
  private readonly toasts = inject(ToastService);
  protected readonly auth = inject(AuthService);
  protected readonly Perm = Perm;
  protected readonly initials = initials;

  protected readonly query = signal<MemberListQuery>({
    search: '',
    roleId: inject(ActivatedRoute).snapshot.queryParamMap.get('roleId') ?? '',
    status: '',
    sort: 'name',
    direction: 'asc',
    page: 1,
    pageSize: 10,
  });
  protected readonly result = signal<PagedResult<MemberSummary> | null>(null);
  protected readonly roles = signal<RoleOption[]>([]);
  protected readonly loading = signal(true);
  protected readonly exporting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly openMenu = signal<string | null>(null);
  /** Viewport position of the open row menu (fixed, so the scrollable table never clips it). */
  protected readonly menuPosition = signal({ top: 0, right: 0 });
  protected readonly changingRole = signal<MemberSummary | null>(null);
  protected readonly actions = inject(MemberActionsService);

  constructor() {
    inject(RolesApi).assignable().subscribe({ next: (roles) => this.roles.set(roles) });
    toObservable(this.query)
      .pipe(
        debounceTime(200),
        tap(() => this.loading.set(true)),
        switchMap((query) =>
          this.api.list(query).pipe(
            catchError((error: unknown) => {
              this.error.set(toApiError(error, 'Members could not be loaded.').message);
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

  protected update(patch: Partial<MemberListQuery>): void {
    this.query.update((q) => ({ ...q, page: 1, ...patch }));
  }

  protected sortBy(column: string): void {
    this.query.update((q) => ({ ...q, sort: column, direction: q.sort === column && q.direction === 'asc' ? 'desc' : 'asc', page: 1 }));
  }

  protected sortIcon(column: string): string {
    const q = this.query();
    return q.sort !== column ? 'bi-arrow-down-up' : q.direction === 'asc' ? 'bi-sort-up' : 'bi-sort-down';
  }

  /** Short, readable form of the member id; the full id is in the title and on the detail page. */
  protected shortId(id: string): string {
    return id.slice(0, 8).toUpperCase();
  }

  protected hasAnyAction(member: MemberSummary): boolean {
    return Object.values(this.actions.available(member)).some(Boolean);
  }

  protected toggleMenu(id: string, event: Event): void {
    event.stopPropagation();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.menuPosition.set({ top: rect.bottom + 4, right: window.innerWidth - rect.right });
    this.openMenu.update((open) => (open === id ? null : id));
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.openMenu() && !(event.target as HTMLElement).closest('.row-menu')) {
      this.openMenu.set(null);
    }
  }

  protected async run(member: MemberSummary, action: 'activate' | 'deactivate' | 'reset' | 'delete' | 'role'): Promise<void> {
    this.openMenu.set(null);
    let changed = false;
    switch (action) {
      case 'role':
        this.changingRole.set(member);
        return;
      case 'reset':
        await this.actions.resetPassword(member);
        return;
      case 'activate':
      case 'deactivate':
        changed = !!(await this.actions.setStatus(member, action === 'activate'));
        break;
      case 'delete':
        changed = await this.actions.remove(member);
        break;
    }
    if (changed) this.reload();
  }

  protected onRoleChanged(): void {
    this.toasts.success(`Role of ${this.changingRole()?.displayName} changed.`);
    this.changingRole.set(null);
    this.reload();
  }

  private reload(): void {
    this.query.update((q) => ({ ...q }));
  }

  protected exportCsv(): void {
    this.exporting.set(true);
    const { page: _page, pageSize: _size, ...filters } = this.query();
    this.api.export(filters).subscribe({
      next: (blob) => {
        downloadBlob(blob, 'members.csv');
        this.exporting.set(false);
      },
      error: (error: unknown) => {
        this.exporting.set(false);
        this.toasts.error(toApiError(error, 'The export failed.').message);
      },
    });
  }
}
