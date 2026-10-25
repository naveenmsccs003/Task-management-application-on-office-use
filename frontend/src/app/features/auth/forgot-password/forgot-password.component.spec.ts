import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ForgotPasswordComponent } from './forgot-password.component';

describe('ForgotPasswordComponent', () => {
  let fixture: ComponentFixture<ForgotPasswordComponent>;
  let http: HttpTestingController;
  let element: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ForgotPasswordComponent);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  async function submit(email: string): Promise<void> {
    const input = element.querySelector('#email') as HTMLInputElement;
    input.value = email;
    input.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('requires a valid email address', async () => {
    await submit('not-an-email');

    expect(element.textContent).toContain('Enter a valid email address.');
    http.expectNone('/api/auth/forgot-password');
  });

  it('shows the generic server confirmation and hides the form', async () => {
    await submit('alice@example.com');

    const request = http.expectOne('/api/auth/forgot-password');
    expect(request.request.body).toEqual({ email: 'alice@example.com' });
    request.flush({ message: 'If an account with that email exists, a password reset link has been sent.' }, { status: 202, statusText: 'Accepted' });
    await fixture.whenStable();

    expect(element.querySelector('[data-testid=forgot-confirmation]')?.textContent).toContain('If an account with that email exists');
    expect(element.querySelector('form')).toBeNull();
  });

  it('reports rate limiting without exposing details', async () => {
    await submit('alice@example.com');

    http.expectOne('/api/auth/forgot-password').flush(
      { detail: 'Too many attempts. Please wait a moment and try again.' },
      { status: 429, statusText: 'Too Many Requests' },
    );
    await fixture.whenStable();

    expect(element.querySelector('.alert-danger')?.textContent).toContain('Too many attempts');
  });
});
