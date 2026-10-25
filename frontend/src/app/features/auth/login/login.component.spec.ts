import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TEST_USER } from '../../../core/auth/testing';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  let element: HTMLElement;

  async function open(url = '/login'): Promise<ComponentFixture<unknown>> {
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, LoginComponent);
    element = harness.routeNativeElement!;
    return harness.fixture;
  }

  const query = <T extends HTMLElement>(selector: string) => element.querySelector(selector) as T;

  async function type(selector: string, value: string): Promise<void> {
    const input = query<HTMLInputElement>(selector);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await harness.fixture.whenStable();
  }

  async function submit(): Promise<void> {
    query<HTMLFormElement>('form').dispatchEvent(new Event('submit'));
    await harness.fixture.whenStable();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'login', component: LoginComponent },
          { path: '**', children: [] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renders the application name, labelled fields and the forgot-password link', async () => {
    await open();

    expect(element.textContent).toContain('Task Management System');
    expect(query('h1').textContent).toContain('Welcome back');
    expect(query('label[for=userNameOrEmail]').textContent).toContain('Username or email');
    expect(query<HTMLInputElement>('#password').type).toBe('password');
    expect(query<HTMLInputElement>('#password').autocomplete).toBe('current-password');
    expect(query('label[for=rememberMe]')).toBeTruthy();
    expect(query<HTMLAnchorElement>('a[href="/forgot-password"]')).toBeTruthy();
  });

  it('shows validation messages and sends nothing when the form is empty', async () => {
    await open();

    await submit();

    expect(element.textContent).toContain('Username or email is required.');
    expect(element.textContent).toContain('Password is required.');
    expect(query('#userNameOrEmail').classList).toContain('is-invalid');
    http.expectNone('/api/auth/login');
  });

  it('toggles password visibility', async () => {
    await open();
    const toggle = query<HTMLButtonElement>('[data-testid=toggle-password]');

    toggle.click();
    await harness.fixture.whenStable();
    expect(query<HTMLInputElement>('#password').type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');

    toggle.click();
    await harness.fixture.whenStable();
    expect(query<HTMLInputElement>('#password').type).toBe('password');
  });

  it('shows a loading state and blocks repeated submissions while signing in', async () => {
    await open();
    await type('#userNameOrEmail', 'alice');
    await type('#password', 'correct horse battery');

    await submit();
    await submit();

    const button = query<HTMLButtonElement>('[data-testid=login-submit]');
    expect(button.disabled).toBe(true);
    expect(button.textContent).toContain('Signing in');
    expect(query('.spinner-border')).toBeTruthy();
    const request = http.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({ userNameOrEmail: 'alice', password: 'correct horse battery', rememberMe: false });
    request.flush(TEST_USER);
  });

  it('displays the generic server message and clears the password on failure', async () => {
    await open();
    await type('#userNameOrEmail', 'alice');
    await type('#password', 'wrong password');
    await submit();

    http.expectOne('/api/auth/login').flush(
      { status: 401, detail: 'Invalid username or password.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await harness.fixture.whenStable();

    expect(query('[data-testid=login-error]').textContent).toContain('Invalid username or password.');
    expect(query<HTMLInputElement>('#password').value).toBe('');
    expect(query<HTMLButtonElement>('[data-testid=login-submit]').disabled).toBe(false);
  });

  it('navigates to a safe return URL after signing in', async () => {
    await open('/login?returnUrl=%2Faccount%2Fchange-password');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
    await type('#userNameOrEmail', 'alice');
    await type('#password', 'correct horse battery');
    await submit();

    http.expectOne('/api/auth/login').flush(TEST_USER);
    await harness.fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/account/change-password');
  });

  it('ignores external return URLs', async () => {
    await open('/login?returnUrl=https%3A%2F%2Fevil.example');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
    await type('#userNameOrEmail', 'alice');
    await type('#password', 'correct horse battery');
    await submit();

    http.expectOne('/api/auth/login').flush(TEST_USER);
    await harness.fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('explains why the user was returned to the login page', async () => {
    await open('/login?reason=session-expired');

    expect(element.textContent).toContain('Your session has expired. Please sign in again.');
  });

  it('uses the responsive split layout with a brand panel hidden on small screens', async () => {
    await open();

    expect(query('.auth-layout')).toBeTruthy();
    expect(query('.auth-form-wrap')).toBeTruthy();
    expect(query('app-auth-brand').className).toContain('d-none d-lg-flex');
    expect(query('app-auth-brand').getAttribute('aria-hidden')).toBe('true');
  });
});
