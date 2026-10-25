import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';

/** Authenticated landing page; replaced by the dashboard once that module exists. */
@Component({
  selector: 'app-home',
  template: `
    <h1 class="h4">Welcome{{ name ? ', ' + name : '' }}</h1>
    <p class="text-body-secondary mb-0">You are signed in to the Task Management System.</p>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {
  private readonly user = inject(AuthService).currentUser();
  protected readonly name = this.user?.displayName || this.user?.userName;
}
