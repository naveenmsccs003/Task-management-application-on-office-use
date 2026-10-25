import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';

/** Layout for authenticated pages. Future modules are added as child routes. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly signingOut = signal(false);
  protected readonly displayName = computed(() => {
    const user = this.auth.currentUser();
    return user?.displayName || user?.userName || '';
  });

  protected logout(): void {
    if (this.signingOut()) {
      return;
    }
    this.signingOut.set(true);
    const done = () => void this.router.navigate(['/login'], { queryParams: { reason: 'logged-out' } });
    this.auth.logout().subscribe({ complete: done, error: done });
  }
}
