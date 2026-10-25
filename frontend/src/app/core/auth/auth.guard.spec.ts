import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { authGuard, guestGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { TEST_USER } from './testing';

describe('auth guards', () => {
  const ensureStatusLoaded = vi.fn();

  beforeEach(() => {
    ensureStatusLoaded.mockReset();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { ensureStatusLoaded } }],
    });
  });

  const run = (guard: typeof authGuard, url = '/account/change-password') =>
    TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot)) as Promise<
      boolean | UrlTree
    >;

  it('allows authenticated users through protected routes', async () => {
    ensureStatusLoaded.mockResolvedValue(TEST_USER);
    expect(await run(authGuard)).toBe(true);
  });

  it('redirects unauthenticated users to login and preserves the destination', async () => {
    ensureStatusLoaded.mockResolvedValue(null);
    const result = await run(authGuard);

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Faccount%2Fchange-password');
  });

  it('sends signed-in users away from the login page', async () => {
    ensureStatusLoaded.mockResolvedValue(TEST_USER);
    const result = await run(guestGuard, '/login');

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  it('lets guests open the login page', async () => {
    ensureStatusLoaded.mockResolvedValue(null);
    expect(await run(guestGuard, '/login')).toBe(true);
  });
});
