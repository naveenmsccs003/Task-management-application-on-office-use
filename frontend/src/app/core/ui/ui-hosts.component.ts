import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ConfirmService } from './confirm.service';
import { ToastService } from './toast.service';
import { ModalComponent } from './modal.component';

/** Renders global toasts and confirmation dialogs. Placed once in the app root. */
@Component({
  selector: 'app-ui-hosts',
  imports: [ModalComponent],
  template: `
    <div class="toast-stack" aria-live="polite" aria-atomic="false">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="app-toast" [class]="'app-toast ' + toast.kind" role="status" data-testid="toast">
          <i class="bi" [class.bi-check-circle-fill]="toast.kind === 'success'"
             [class.bi-exclamation-octagon-fill]="toast.kind === 'error'"
             [class.bi-info-circle-fill]="toast.kind === 'info'" aria-hidden="true"></i>
          <span class="flex-grow-1">{{ toast.message }}</span>
          <button type="button" class="btn-close btn-close-sm" aria-label="Dismiss" (click)="toasts.dismiss(toast.id)"></button>
        </div>
      }
    </div>

    @if (confirm.pending(); as dialog) {
      <app-modal [title]="dialog.title" [icon]="dialog.danger ? 'bi-exclamation-triangle' : 'bi-question-circle'"
                 [danger]="!!dialog.danger" (closed)="confirm.close(false)">
        <p class="mb-2">{{ dialog.message }}</p>
        @if (dialog.details?.length) {
          <ul class="confirm-details mb-0">
            @for (line of dialog.details; track line) {
              <li>{{ line }}</li>
            }
          </ul>
        }
        <ng-container footer>
          <button type="button" class="btn btn-light border" (click)="confirm.close(false)">Cancel</button>
          <button type="button" class="btn" data-testid="confirm-ok" [class.btn-danger]="dialog.danger"
                  [class.btn-gradient]="!dialog.danger" (click)="confirm.close(true)">
            {{ dialog.confirmText ?? 'Confirm' }}
          </button>
        </ng-container>
      </app-modal>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UiHostsComponent {
  protected readonly toasts = inject(ToastService);
  protected readonly confirm = inject(ConfirmService);
}
