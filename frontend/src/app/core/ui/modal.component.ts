import { ChangeDetectionStrategy, Component, ElementRef, afterNextRender, inject, input, output } from '@angular/core';

/** Accessible dialog shell (no Bootstrap JS): Escape and backdrop close it; focus moves into the dialog. */
@Component({
  selector: 'app-modal',
  template: `
    <div class="app-modal-backdrop" (click)="closed.emit()"></div>
    <div class="app-modal" role="dialog" aria-modal="true" [attr.aria-labelledby]="titleId" tabindex="-1">
      <div class="app-modal-header">
        <span class="icon-tile" [class.tile-danger]="danger()"><i class="bi {{ icon() }}" aria-hidden="true"></i></span>
        <h2 class="h6 mb-0 flex-grow-1" [id]="titleId">{{ title() }}</h2>
        <button type="button" class="btn-close" aria-label="Close" (click)="closed.emit()"></button>
      </div>
      <div class="app-modal-body"><ng-content /></div>
      <div class="app-modal-footer"><ng-content select="[footer]" /></div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'closed.emit()' },
})
export class ModalComponent {
  private static counter = 0;
  readonly title = input.required<string>();
  readonly icon = input('bi-window');
  readonly danger = input(false);
  readonly closed = output<void>();
  protected readonly titleId = `modal-title-${++ModalComponent.counter}`;

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef);
    afterNextRender(() => host.nativeElement.querySelector<HTMLElement>('.app-modal')?.focus());
  }
}
