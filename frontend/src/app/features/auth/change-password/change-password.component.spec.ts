import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ChangePasswordComponent } from './change-password.component';

describe('ChangePasswordComponent', () => {
  let fixture: ComponentFixture<ChangePasswordComponent>;
  let http: HttpTestingController;
  let element: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ChangePasswordComponent);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  async function fillAndSubmit(current: string, next: string, confirmation: string): Promise<void> {
    for (const [id, value] of [['#currentPassword', current], ['#newPassword', next], ['#confirmNewPassword', confirmation]]) {
      const input = element.querySelector(id) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new Event('blur'));
    }
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('validates required fields, minimum length and confirmation without calling the API', async () => {
    await fillAndSubmit('', 'short', 'different');

    expect(element.textContent).toContain('Current password is required.');
    expect(element.textContent).toContain('Password must be at least 12 characters.');
    expect(element.textContent).toContain('The passwords must match.');
    http.expectNone('/api/auth/change-password');
  });

  it('shows a success message and clears the form', async () => {
    await fillAndSubmit('old passphrase', 'a brand new passphrase', 'a brand new passphrase');

    const button = element.querySelector('[data-testid=change-password-submit]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    http.expectOne('/api/auth/change-password').flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(element.querySelector('.alert-success')?.textContent).toContain('Your password has been changed');
    expect((element.querySelector('#newPassword') as HTMLInputElement).value).toBe('');
  });

  it('shows the server error for an incorrect current password', async () => {
    await fillAndSubmit('wrong passphrase', 'a brand new passphrase', 'a brand new passphrase');

    http.expectOne('/api/auth/change-password').flush(
      { errors: { currentPassword: ['The current password is incorrect.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    expect(element.textContent).toContain('The current password is incorrect.');
    expect(element.querySelector('#currentPassword')!.classList).toContain('is-invalid');
  });

  it('updates the requirement checklist and strength meter as the user types', async () => {
    const input = element.querySelector('#newPassword') as HTMLInputElement;
    input.value = 'Correct-Horse-Battery-9';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    const items = Array.from(element.querySelectorAll('[data-testid=requirements] li'));
    expect(items[0].classList).toContain('met');
    expect(element.textContent).toContain('Strength: Strong');
  });

  it('toggles visibility of each password field independently', async () => {
    const toggle = element.querySelector('[aria-controls=newPassword]') as HTMLButtonElement;
    toggle.click();
    await fixture.whenStable();

    expect((element.querySelector('#newPassword') as HTMLInputElement).type).toBe('text');
    expect((element.querySelector('#currentPassword') as HTMLInputElement).type).toBe('password');
  });
});
