import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Decorative brand panel shown beside the auth forms on large screens. */
@Component({
  selector: 'app-auth-brand',
  templateUrl: './auth-brand.component.html',
  styleUrl: './auth-brand.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'd-none d-lg-flex', 'aria-hidden': 'true' },
})
export class AuthBrandComponent {
  protected readonly year = new Date().getFullYear();
  protected readonly features = [
    { icon: 'bi-kanban', title: 'Organise every project', text: 'Plan tasks, milestones and deadlines in one place.' },
    { icon: 'bi-people', title: 'Work as a team', text: 'Share progress and meeting notes with your colleagues.' },
    { icon: 'bi-shield-lock', title: 'Secure by design', text: 'Protected sessions, lockout and role-based access.' },
  ];
}
