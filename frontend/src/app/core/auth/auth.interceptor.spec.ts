import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  const authenticated = signal(true);
  const handleSessionExpired = vi.fn();

  beforeEach(() => {
    authenticated.set(true);
    handleSessionExpired.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { isAuthenticated: authenticated, handleSessionExpired } },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => controller.verify());

  it('treats a 401 from a protected API as an expired session', () => {
    http.get('/api/tasks').subscribe({ error: () => undefined });
    controller.expectOne('/api/tasks').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(handleSessionExpired).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/', reason: 'session-expired' } });
  });

  it('does not treat a failed login as an expired session', () => {
    http.post('/api/auth/login', {}).subscribe({ error: () => undefined });
    controller.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(handleSessionExpired).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('keeps the session on 403 (authorization failure)', () => {
    let status = 0;
    http.get('/api/settings').subscribe({ error: (e) => (status = e.status) });
    controller.expectOne('/api/settings').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(status).toBe(403);
    expect(handleSessionExpired).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not retry failed requests', () => {
    http.post('/api/auth/change-password', {}).subscribe({ error: () => undefined });
    controller.expectOne('/api/auth/change-password').flush(null, { status: 500, statusText: 'Error' });
    controller.expectNone('/api/auth/change-password');
  });
});
