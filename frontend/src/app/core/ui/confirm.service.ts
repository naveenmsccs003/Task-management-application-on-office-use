import { Injectable, signal } from '@angular/core';

export interface ConfirmOptions {
  title: string;
  message: string;
  details?: string[];
  confirmText?: string;
  danger?: boolean;
}

interface PendingConfirm extends ConfirmOptions {
  resolve: (confirmed: boolean) => void;
}

/** Promise-based confirmation dialog rendered by ConfirmHostComponent. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  readonly pending = signal<PendingConfirm | null>(null);

  confirm(options: ConfirmOptions): Promise<boolean> {
    this.pending()?.resolve(false);
    return new Promise((resolve) => this.pending.set({ ...options, resolve }));
  }

  close(confirmed: boolean): void {
    const current = this.pending();
    this.pending.set(null);
    current?.resolve(confirmed);
  }
}
