import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { Perm } from '../core/auth/permissions';
import { displayNameOf, initialsOf } from '../core/auth/user-display';

/** Layout for authenticated pages. Future modules are added as child routes. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'menuOpen.set(false)',
  },
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly user = this.auth.currentUser;
  protected readonly displayName = computed(() => displayNameOf(this.user()));
  protected readonly initials = computed(() => initialsOf(this.user()));
  protected readonly menuOpen = signal(false);
  protected readonly mobileNavOpen = signal(false);

  /** Tabs shown only when the user holds the module's tab-access permission (hiding is UX; the API enforces). */
  protected readonly tabs = computed(() => {
    this.user();
    return [
      { path: '/members', label: 'Members', icon: 'bi-people', show: this.auth.can(Perm.members.access) && this.auth.can(Perm.members.view) },
      { path: '/roles', label: 'Roles & Permissions', icon: 'bi-shield-lock', show: this.auth.can(Perm.roles.access) && this.auth.can(Perm.roles.view) },
    ].filter((tab) => tab.show);
  });
  protected readonly signingOut = signal(false);

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected onDocumentClick(event: MouseEvent): void {
    const menu = this.host.nativeElement.querySelector('.user-menu');
    if (this.menuOpen() && menu && !menu.contains(event.target as Node)) {
      this.menuOpen.set(false);
    }
  }

  protected logout(): void {
    if (this.signingOut()) {
      return;
    }
    this.signingOut.set(true);
    this.menuOpen.set(false);
    const done = () => void this.router.navigate(['/login'], { queryParams: { reason: 'logged-out' } });
    this.auth.logout().subscribe({ complete: done, error: done });
  }
}
