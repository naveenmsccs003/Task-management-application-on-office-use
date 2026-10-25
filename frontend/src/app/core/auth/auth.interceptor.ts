import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Endpoints whose 401 is an expected answer, not a sign that the session expired. */
const AUTH_ENDPOINTS_WITHOUT_SESSION = ['/api/auth/login', '/api/auth/me', '/api/auth/logout'];

/**
 * Handles authentication failures (401) consistently: the session is gone, so clear local state
 * and return to the login page. Authorization failures (403) keep the session but refresh the cached
 * permissions; the caller shows the error. Requests are never retried.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !AUTH_ENDPOINTS_WITHOUT_SESSION.some((path) => req.url.startsWith(path)) &&
        auth.isAuthenticated()
      ) {
        auth.handleSessionExpired();
        void router.navigate(['/login'], {
          queryParams: { returnUrl: router.url, reason: 'session-expired' },
        });
      } else if (error instanceof HttpErrorResponse && error.status === 403 && auth.isAuthenticated()) {
        // Permissions may have been revoked: refresh them so hidden tabs and buttons catch up.
        void auth.refreshStatus();
      }
      return throwError(() => error);
    }),
  );
};
