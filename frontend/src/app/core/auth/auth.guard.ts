import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Sends unauthenticated users to the login page, preserving the requested URL.
 * This is navigation UX only; the API independently rejects unauthenticated requests.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = await auth.ensureStatusLoaded();
  return user ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Keeps signed-in users away from the login and forgot-password pages. */
export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = await auth.ensureStatusLoaded();
  return user ? router.createUrlTree(['/']) : true;
};

/**
 * Requires all listed permissions. Re-reads the status from the server first so a revoked permission is
 * never honoured from stale client state. Navigation UX only; the API enforces the same checks.
 */
export function permissionGuard(...permissions: string[]): CanActivateFn {
  return async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const user = await auth.refreshStatus();
    if (!user) {
      return router.createUrlTree(['/login']);
    }
    return permissions.every((p) => auth.can(p)) ? true : router.createUrlTree(['/forbidden']);
  };
}
