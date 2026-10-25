import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { MemberFormComponent } from './member-form.component';

describe('MemberFormComponent (create)', () => {
  let fixture: ComponentFixture<MemberFormComponent>;
  let element: HTMLElement;
  let http: HttpTestingController;

  async function render(permissions: string[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: signal({ id: 'me', isAdministrator: true }), can: (p: string) => permissions.includes(p), canAny: () => true } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(MemberFormComponent);
    element = fixture.nativeElement;
    await fixture.whenStable();
    http.expectOne('/api/roles/assignable').flush([{ id: 'r1', name: 'Developer', isSystem: false, isAssignable: true }]);
    await fixture.whenStable();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = element.querySelector(selector) as HTMLInputElement | HTMLSelectElement;
    input.value = value;
    input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
    await fixture.whenStable();
  }

  async function pickPhoto(file: File): Promise<void> {
    const input = element.querySelector('[data-testid=photo-input]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  it('hides personal information and photo without the permission', async () => {
    await render(['members.create']);

    expect(element.querySelector('[data-testid=personal-restricted]')).not.toBeNull();
    expect(element.querySelector('[data-testid=photo-input]')).toBeNull();
  });

  it('rejects unsupported photo files before upload', async () => {
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    await render(['members.create', 'members.edit_personal_information']);

    await pickPhoto(new File(['<svg/>'], 'x.svg', { type: 'image/svg+xml' }));

    expect(element.textContent).toContain('Only JPEG and PNG images are supported.');
  });

  it('uploads the chosen photo right after the member is created', async () => {
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
    await render(['members.create', 'members.edit_personal_information']);
    await type('#displayName', 'Jane Doe');
    await type('#email', 'jane@example.com');
    await type('#userName', 'jane');
    await type('#roleId', 'r1');
    await pickPhoto(new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'me.png', { type: 'image/png' }));
    expect((element.querySelector('[data-testid=photo-preview] img') as HTMLImageElement).src).toContain('blob:preview');

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const create = http.expectOne('/api/members');
    expect(create.request.body.setupMethod).toBe('Invitation');
    create.flush({ id: 'new-id', displayName: 'Jane Doe', email: 'jane@example.com' });
    await fixture.whenStable();
    const upload = http.expectOne('/api/members/new-id/photo');
    expect(upload.request.method).toBe('PUT');
    expect(upload.request.body instanceof FormData).toBe(true);
    upload.flush(null, { status: 204, statusText: 'No Content' });
  });
});
