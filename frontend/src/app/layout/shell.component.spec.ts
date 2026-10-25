import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { TEST_USER } from '../core/auth/testing';
import { ShellComponent } from './shell.component';

describe('ShellComponent', () => {
  it('shows the signed-in user and signs out to the login page', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const auth = TestBed.inject(AuthService);
    auth.login({ userNameOrEmail: 'alice', password: 'x', rememberMe: false }).subscribe();
    http.expectOne('/api/auth/login').flush(TEST_USER);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[data-testid=current-user]')?.textContent).toContain('Alice');

    expect(element.querySelector('[data-testid=sign-out]')).toBeNull();
    (element.querySelector('[data-testid=user-menu-toggle]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(element.querySelector('[data-testid=user-menu-toggle]')!.getAttribute('aria-expanded')).toBe('true');
    (element.querySelector('[data-testid=sign-out]') as HTMLButtonElement).click();
    http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { reason: 'logged-out' } });
    http.verify();
  });

  it('shows module tabs only for modules the user can access', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const auth = TestBed.inject(AuthService);
    auth.login({ userNameOrEmail: 'alice', password: 'x', rememberMe: false }).subscribe();
    http.expectOne('/api/auth/login').flush({ ...TEST_USER, permissions: ['members.access', 'members.view'] });

    const fixture = TestBed.createComponent(ShellComponent);
    await fixture.whenStable();
    const nav = (fixture.nativeElement as HTMLElement).querySelector('[data-testid=main-nav]')!;

    expect(nav.querySelector('[data-testid=nav-members]')).not.toBeNull();
    expect(nav.querySelector('[data-testid=nav-roles]')).toBeNull();
  });
});
