import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Perm } from '../../core/auth/permissions';
import { displayNameOf, initialsOf } from '../../core/auth/user-display';

/** Authenticated landing page; becomes the dashboard once that module exists. */
@Component({
  selector: 'app-home',
  imports: [RouterLink, DatePipe],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {
  protected readonly auth = inject(AuthService);
  private readonly user = this.auth.currentUser;

  /** Implemented modules the user can open. */
  protected readonly liveModules = computed(() => {
    this.user();
    return [
      { icon: 'bi-people', name: 'Members', path: '/members', show: this.auth.can(Perm.members.view) },
      { icon: 'bi-shield-lock', name: 'Roles & Permissions', path: '/roles', show: this.auth.can(Perm.roles.view) },
    ].filter((m) => m.show);
  });

  protected readonly today = new Date();
  protected readonly account = this.user;
  protected readonly name = computed(() => displayNameOf(this.user()));
  protected readonly initials = computed(() => initialsOf(this.user()));
  protected readonly greeting = HomeComponent.greetingFor(this.today.getHours());

  /** Roadmap preview only; these modules are not implemented yet. */
  protected readonly upcoming = [
    { icon: 'bi-speedometer2', name: 'Dashboard' },
    { icon: 'bi-folder2-open', name: 'Projects' },
    { icon: 'bi-list-check', name: 'Tasks' },
    { icon: 'bi-journal-text', name: 'Meeting notes' },
    { icon: 'bi-flag', name: 'Milestones' },
  ];

  private static greetingFor(hour: number): string {
    return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
  }
}
