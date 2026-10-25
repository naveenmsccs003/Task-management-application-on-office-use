import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { finalize, firstValueFrom, map, Observable, tap } from 'rxjs';
import {
  AuthStatus,
  ChangePasswordRequest,
  CurrentUser,
  ForgotPasswordRequest,
  LoginRequest,
  MessageResponse,
  ResetPasswordRequest,
} from './auth.models';

const API = '/api/auth';

/**
 * Frontend view of the server-side session. The session itself lives in an HttpOnly cookie that
 * script cannot read; this service only mirrors what `/api/auth/me` reports. It is a UX aid, not a
 * security boundary: the API enforces authentication and authorization on every request.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly user = signal<CurrentUser | null>(null);
  private statusRequest: Promise<CurrentUser | null> | null = null;

  readonly currentUser = this.user.asReadonly();
  readonly isAuthenticated = computed(() => this.user() !== null);

  /** Loads the status once (also obtains the CSRF token cookie). Later calls reuse the result. */
  ensureStatusLoaded(): Promise<CurrentUser | null> {
    return (this.statusRequest ??= this.refreshStatus());
  }

  /** Re-reads the status from the server and refreshes the CSRF token for the current identity. */
  refreshStatus(): Promise<CurrentUser | null> {
    const request = firstValueFrom(this.http.get<AuthStatus>(`${API}/me`))
      .then((status) => (status.isAuthenticated ? status.user : null))
      .catch(() => null)
      .then((user) => {
        this.user.set(user);
        return user;
      });
    this.statusRequest = request;
    return request;
  }

  login(request: LoginRequest): Observable<CurrentUser> {
    return this.http.post<CurrentUser>(`${API}/login`, request).pipe(
      tap((user) => {
        this.user.set(user);
        this.statusRequest = Promise.resolve(user);
      }),
    );
  }

  /** Ends the session. Local state is cleared even if the request fails. */
  logout(): Observable<void> {
    return this.http.post<void>(`${API}/logout`, {}).pipe(finalize(() => this.clearState()));
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>(`${API}/change-password`, request);
  }

  forgotPassword(request: ForgotPasswordRequest): Observable<string> {
    return this.http.post<MessageResponse>(`${API}/forgot-password`, request).pipe(map((r) => r.message));
  }

  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>(`${API}/reset-password`, request);
  }

  /**
   * Called when the API reports the session is gone (expired or revoked). Clears local state and
   * fetches a fresh anonymous CSRF token so the next login is accepted.
   */
  handleSessionExpired(): void {
    this.clearState();
    void this.refreshStatus();
  }

  private clearState(): void {
    this.user.set(null);
    this.statusRequest = Promise.resolve(null);
  }
}
