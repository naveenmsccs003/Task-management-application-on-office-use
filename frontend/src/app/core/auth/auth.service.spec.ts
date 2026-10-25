import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { TEST_USER } from './testing';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the status once and reuses it', async () => {
    const first = service.ensureStatusLoaded();
    const second = service.ensureStatusLoaded();
    http.expectOne('/api/auth/me').flush({ isAuthenticated: true, user: TEST_USER });

    expect(await first).toEqual(TEST_USER);
    expect(await second).toEqual(TEST_USER);
    expect(service.isAuthenticated()).toBe(true);
  });

  it('treats a failed status request as signed out', async () => {
    const status = service.ensureStatusLoaded();
    http.expectOne('/api/auth/me').flush(null, { status: 500, statusText: 'Server Error' });

    expect(await status).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('stores the user after a successful login and never persists credentials', () => {
    const storageSpy = vi.spyOn(Storage.prototype, 'setItem');
    service.login({ userNameOrEmail: 'alice', password: 'secret passphrase', rememberMe: true }).subscribe();

    const request = http.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    request.flush(TEST_USER);

    expect(service.currentUser()).toEqual(TEST_USER);
    expect(storageSpy).not.toHaveBeenCalled();
  });

  it('clears local state on logout even if the request fails', () => {
    service.login({ userNameOrEmail: 'alice', password: 'x', rememberMe: false }).subscribe();
    http.expectOne('/api/auth/login').flush(TEST_USER);

    service.logout().subscribe({ error: () => undefined });
    http.expectOne('/api/auth/logout').flush(null, { status: 500, statusText: 'Server Error' });

    expect(service.isAuthenticated()).toBe(false);
  });

  it('clears state and refreshes the CSRF token when the session expires', () => {
    service.login({ userNameOrEmail: 'alice', password: 'x', rememberMe: false }).subscribe();
    http.expectOne('/api/auth/login').flush(TEST_USER);

    service.handleSessionExpired();

    expect(service.isAuthenticated()).toBe(false);
    http.expectOne('/api/auth/me').flush({ isAuthenticated: false, user: null });
  });
});
