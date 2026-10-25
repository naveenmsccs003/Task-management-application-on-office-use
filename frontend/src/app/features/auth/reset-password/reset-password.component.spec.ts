import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ResetPasswordComponent } from './reset-password.component';

describe('ResetPasswordComponent', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'reset-password', component: ResetPasswordComponent },
          { path: '**', children: [] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  const element = () => harness.routeNativeElement!;

  async function fill(password: string, confirmation: string): Promise<void> {
    for (const [id, value] of [['#newPassword', password], ['#confirmNewPassword', confirmation]]) {
      const input = element().querySelector(id) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new Event('blur'));
    }
    element().querySelector('form')!.dispatchEvent(new Event('submit'));
    await harness.fixture.whenStable();
  }

  it('reports a link without token as invalid', async () => {
    await harness.navigateByUrl('/reset-password', ResetPasswordComponent);

    expect(element().querySelector('[data-testid=reset-link-invalid]')).toBeTruthy();
    expect(element().querySelector('form')).toBeNull();
  });

  it('requires matching passwords before submitting', async () => {
    await harness.navigateByUrl('/reset-password?uid=u1&token=t1', ResetPasswordComponent);

    await fill('a long enough passphrase', 'something else entirely');

    expect(element().textContent).toContain('The passwords must match.');
    http.expectNone('/api/auth/reset-password');
  });

  it('submits the token from the link and shows an expired-link message when rejected', async () => {
    await harness.navigateByUrl('/reset-password?uid=u1&token=t1', ResetPasswordComponent);

    await fill('a long enough passphrase', 'a long enough passphrase');
    const request = http.expectOne('/api/auth/reset-password');
    expect(request.request.body).toEqual({
      userId: 'u1',
      token: 't1',
      newPassword: 'a long enough passphrase',
      confirmNewPassword: 'a long enough passphrase',
    });
    request.flush({ errors: { token: ['The password reset link is invalid or has expired.'] } }, { status: 400, statusText: 'Bad Request' });
    await harness.fixture.whenStable();

    expect(element().querySelector('[data-testid=reset-link-invalid]')).toBeTruthy();
  });
});
