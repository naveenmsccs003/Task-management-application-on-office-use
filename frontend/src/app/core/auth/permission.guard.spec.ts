import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { permissionGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { TEST_USER } from './testing';

describe('permissionGuard', () => {
  const refreshStatus = vi.fn();
  let granted: string[];

  beforeEach(() => {
    granted = [];
    refreshStatus.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { refreshStatus, can: (p: string) => granted.includes(p) } },
      ],
    });
  });

  const run = (...perms: string[]) =>
    TestBed.runInInjectionContext(() =>
      permissionGuard(...perms)({} as ActivatedRouteSnapshot, { url: '/members' } as RouterStateSnapshot),
    ) as Promise<boolean | UrlTree>;

  it('re-reads permissions from the server before deciding', async () => {
    refreshStatus.mockResolvedValue(TEST_USER);
    granted = ['members.view'];

    expect(await run('members.view')).toBe(true);
    expect(refreshStatus).toHaveBeenCalledOnce();
  });

  it('sends users without the permission to the access-denied page', async () => {
    refreshStatus.mockResolvedValue(TEST_USER);

    const result = await run('members.view');

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/forbidden');
  });

  it('requires every listed permission', async () => {
    refreshStatus.mockResolvedValue(TEST_USER);
    granted = ['members.view'];

    expect(await run('members.view', 'members.create')).toBeInstanceOf(UrlTree);
  });

  it('sends signed-out users to login', async () => {
    refreshStatus.mockResolvedValue(null);

    expect(TestBed.inject(Router).serializeUrl((await run('members.view')) as UrlTree)).toBe('/login');
  });
});
