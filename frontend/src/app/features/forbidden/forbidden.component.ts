import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink],
  template: `
    <section class="app-card p-5 text-center animate-in" style="max-width: 32rem; margin: 2rem auto">
      <span class="icon-tile tile-danger mb-3" style="width: 3.5rem; height: 3.5rem; font-size: 1.5rem"><i class="bi bi-shield-x" aria-hidden="true"></i></span>
      <h1 class="h4">Access denied</h1>
      <p class="text-muted-app">You do not have permission to open this page. If you think you should, ask an administrator to update your role.</p>
      <a routerLink="/" class="btn btn-gradient"><i class="bi bi-house me-1" aria-hidden="true"></i>Back to home</a>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForbiddenComponent {}
