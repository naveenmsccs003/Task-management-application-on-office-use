import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { MembersListComponent } from './members-list.component';

describe('MembersListComponent', () => {
  const JANE = { id: 'a1b2c3d4-0000-0000-0000-000000000001', displayName: 'Jane Doe', userName: 'jane', email: 'jane@example.com', role: { id: 'r1', name: 'Developer', isSystem: false, isActive: true }, isActive: true, isLockedOut: false, hasPhoto: false, createdAtUtc: '2026-10-01T00:00:00Z', lastLoginAtUtc: null };
  const BOSS = { ...JANE, id: 'b0000000-0000-0000-0000-000000000002', displayName: 'Boss', userName: 'boss', role: { id: 'r0', name: 'Administrator', isSystem: true, isActive: true } };

  async function render(permissions: string[], items: unknown[] = [JANE], me = { id: 'me', isAdministrator: false }) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: signal(me), can: (p: string) => permissions.includes(p), canAny: (...p: string[]) => p.some((x) => permissions.includes(x)) } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MembersListComponent);
    await fixture.whenStable();
    http.expectOne('/api/roles/assignable').flush([]);
    await new Promise((resolve) => setTimeout(resolve, 250));
    http.expectOne((r) => r.url === '/api/members').flush({ items, page: 1, pageSize: 10, totalCount: items.length });
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('hides create and export actions without permission', async () => {
    const element = await render(['members.view']);

    expect(element.querySelectorAll('[data-testid=member-row]').length).toBe(1);
    expect(element.querySelector('[data-testid=create-member]')).toBeNull();
    expect(element.querySelector('[data-testid=export-members]')).toBeNull();
    expect(element.querySelector('a[aria-label="Edit Jane Doe"]')).toBeNull();
  });

  it('shows actions the user is allowed to perform', async () => {
    const element = await render(['members.view', 'members.create', 'members.export', 'members.view_details', 'members.edit']);

    expect(element.querySelector('[data-testid=create-member]')).not.toBeNull();
    expect(element.querySelector('[data-testid=export-members]')).not.toBeNull();
    expect(element.querySelector('a[aria-label="Edit Jane Doe"]')).not.toBeNull();
    expect(element.querySelector('[data-testid=page-summary]')?.textContent).toContain('1–1 of 1');
    expect(element.querySelector('[data-testid=member-id]')?.textContent?.trim()).toBe('A1B2C3D4');
  });

  it('offers row actions the user may perform and protects administrators and their own account', async () => {
    const element = await render(
      ['members.view', 'members.change_role', 'members.deactivate', 'members.reset_password'],
      [JANE, BOSS, { ...JANE, id: 'me', displayName: 'Me Myself' }],
    );
    const rows = element.querySelectorAll('[data-testid=member-row]');

    expect(rows[0].querySelector('[data-testid=row-actions]')).not.toBeNull();
    expect(rows[1].querySelector('[data-testid=row-actions]')).toBeNull();
    expect(rows[2].querySelector('[data-testid=row-actions]')).toBeNull();

    (rows[0].querySelector('[data-testid=row-actions]') as HTMLButtonElement).click();
    await new Promise((resolve) => setTimeout(resolve));
    const menu = rows[0].querySelector('[role=menu]')!;
    expect(menu.textContent).toContain('Change role');
    expect(menu.textContent).toContain('Deactivate');
    expect(menu.textContent).toContain('Reset password');
    expect(menu.textContent).not.toContain('Delete');
  });
});
