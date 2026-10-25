import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MemberPermissions } from '../../core/api/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { ConfirmService } from '../../core/ui/confirm.service';
import { MemberPermissionsComponent } from './member-permissions.component';

const DATA: MemberPermissions = {
  memberId: 'm1', roleName: 'Developer', isAdministrator: false, isEditable: true,
  items: [
    { permissionId: 200, key: 'members.access', displayName: 'Tab Access', description: '', moduleKey: 'members', moduleName: 'Member Management', roleDefault: true, override: null, effective: true, source: '', allowMemberOverride: true },
    { permissionId: 201, key: 'members.view', displayName: 'View', description: '', moduleKey: 'members', moduleName: 'Member Management', roleDefault: true, override: null, effective: true, source: '', allowMemberOverride: true },
    { permissionId: 209, key: 'members.change_role', displayName: 'Change Role', description: '', moduleKey: 'members', moduleName: 'Member Management', roleDefault: false, override: null, effective: false, source: '', allowMemberOverride: false },
    { permissionId: 202, key: 'members.view_details', displayName: 'Details', description: '', moduleKey: 'members', moduleName: 'Member Management', roleDefault: false, override: null, effective: false, source: '', allowMemberOverride: true },
  ],
};

describe('MemberPermissionsComponent', () => {
  let fixture: ComponentFixture<MemberPermissionsComponent>;
  let element: HTMLElement;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(true) } },
        { provide: AuthService, useValue: { currentUser: signal({ isAdministrator: false }), can: (key: string) => key !== 'members.view_details_x' } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MemberPermissionsComponent);
    fixture.componentRef.setInput('memberId', 'm1');
    element = fixture.nativeElement;
    await fixture.whenStable();
    http.expectOne('/api/members/m1/permissions').flush(DATA);
    await fixture.whenStable();
  });

  const row = (key: string) => element.querySelector(`[data-key="${key}"]`) as HTMLElement;
  const effective = (key: string) => row(key).querySelector('[data-testid=effective]')?.textContent?.trim();

  async function choose(id: number, choice: string): Promise<void> {
    (element.querySelector(`#ov-${id}-${choice}`) as HTMLInputElement).click();
    await fixture.whenStable();
  }

  it('shows role defaults as the effective result when inheriting', () => {
    expect(effective('members.view')).toBe('Allow');
    expect(effective('members.view_details')).toBe('Deny');
  });

  it('previews Deny overriding the role and Allow granting an extra permission', async () => {
    await choose(201, 'Deny');
    await choose(202, 'Allow');

    expect(effective('members.view')).toBe('Deny');
    expect(effective('members.view_details')).toBe('Allow');
    expect(row('members.view_details').textContent).toContain('Explicitly allowed');
  });

  it('denying tab access makes the whole module ineffective', async () => {
    await choose(200, 'Deny');

    expect(effective('members.view')).toBe('Deny');
    expect(row('members.view').textContent).toContain('Requires tab access');
  });

  it('does not offer Allow for permissions that can only come from a role', () => {
    expect((element.querySelector('#ov-209-Allow') as HTMLInputElement).disabled).toBe(true);
    expect((element.querySelector('#ov-209-Deny') as HTMLInputElement).disabled).toBe(false);
  });

  it('saves only explicit overrides; inherit is the absence of an override', async () => {
    await choose(201, 'Deny');
    await choose(202, 'Allow');
    await choose(202, 'Inherit');

    (element.querySelector('[data-testid=save-overrides]') as HTMLButtonElement).click();
    await fixture.whenStable();

    const request = http.expectOne('/api/members/m1/permissions');
    expect(request.request.body).toEqual({ overrides: [{ permissionId: 201, effect: 'Deny' }] });
    request.flush({ ...DATA, items: DATA.items.map((i) => (i.permissionId === 201 ? { ...i, override: 'Deny', effective: false } : i)) });
  });
});
