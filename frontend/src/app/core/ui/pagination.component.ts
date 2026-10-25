import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

@Component({
  selector: 'app-pagination',
  template: `
    <div class="d-flex flex-wrap align-items-center justify-content-between gap-2 pagination-bar">
      <span class="fs-xs text-muted-app" data-testid="page-summary">
        @if (total() === 0) { No results } @else { Showing {{ first() }}–{{ last() }} of {{ total() }} }
      </span>
      <nav aria-label="Pagination">
        <ul class="pagination pagination-sm mb-0">
          <li class="page-item" [class.disabled]="page() <= 1">
            <button type="button" class="page-link" aria-label="Previous page" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)">
              <i class="bi bi-chevron-left" aria-hidden="true"></i>
            </button>
          </li>
          @for (p of pages(); track p) {
            <li class="page-item" [class.active]="p === page()">
              <button type="button" class="page-link" [attr.aria-current]="p === page() ? 'page' : null" (click)="pageChange.emit(p)">{{ p }}</button>
            </li>
          }
          <li class="page-item" [class.disabled]="page() >= pageCount()">
            <button type="button" class="page-link" aria-label="Next page" [disabled]="page() >= pageCount()" (click)="pageChange.emit(page() + 1)">
              <i class="bi bi-chevron-right" aria-hidden="true"></i>
            </button>
          </li>
        </ul>
      </nav>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaginationComponent {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly pageChange = output<number>();

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly first = computed(() => (this.page() - 1) * this.pageSize() + 1);
  protected readonly last = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
  /** A window of at most five page numbers around the current page. */
  protected readonly pages = computed(() => {
    const count = this.pageCount();
    const start = Math.max(1, Math.min(this.page() - 2, count - 4));
    return Array.from({ length: Math.min(5, count) }, (_, i) => start + i);
  });
}
