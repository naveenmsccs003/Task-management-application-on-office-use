import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PermissionModule } from '../../core/api/api.models';
import { ConfirmService } from '../../core/ui/confirm.service';
import { PermissionMatrixComponent } from './permission-matrix.component';

const MODULES: PermissionModule[] = [
  {
    key: 'members', name: 'Member Management', icon: 'bi-people', isImplemented: true,
    permissions: [
      { id: 200, key: 'members.access', displayName: 'Tab Access', description: '', parentPermissionId: null, isActive: true, allowMemberOverride: true },
      { id: 201, key: 'members.view', displayName: 'View Member List', description: '', parentPermissionId: 200, isActive: true, allowMemberOverride: true },
      { id: 203, key: 'members.create', displayName: 'Create Member', description: '', parentPermissionId: 200, isActive: true, allowMemberOverride: true },
    ],
  },
  {
    key: 'tasks', name: 'Tasks', icon: 'bi-list-check', isImplemented: false,
    permissions: [
      { id: 500, key: 'tasks.access', displayName: 'Tab Access', description: '', parentPermissionId: null, isActive: false, allowMemberOverride: true },
    ],
  },
];

describe('PermissionMatrixComponent', () => {
  let fixture: ComponentFixture<PermissionMatrixComponent>;
  let element: HTMLElement;
  let http: HttpTestingController;

  async function create(granted: number[], editable = true): Promise<void> {
    fixture = TestBed.createComponent(PermissionMatrixComponent);
    fixture.componentRef.setInput('roleId', 'r1');
    fixture.componentRef.setInput('modules', MODULES);
    fixture.componentRef.setInput('granted', granted);
    fixture.componentRef.setInput('editable', editable);
    fixture.componentRef.setInput('memberCount', 3);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const box = (id: number) => element.querySelector(`#perm-${id}`) as HTMLInputElement;

  async function click(selector: string): Promise<void> {
    (element.querySelector(selector) as HTMLElement).click();
    await fixture.whenStable();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(true) } }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('shows the selected count and disables modules that are not implemented', async () => {
    await create([200, 201]);

    expect(element.querySelector('[data-testid=selected-count]')?.textContent).toContain('2 of 3 selected');
    expect(element.querySelector('[data-module=tasks]')?.textContent).toContain('Coming soon');
  });

  it('selecting an operation also selects the tab access; clearing tab access clears the module', async () => {
    await create([]);

    await click('#perm-203');
    expect(box(200).checked).toBe(true);
    expect(box(203).checked).toBe(true);

    await click('#perm-200');
    expect(box(203).checked).toBe(false);
  });

  it('supports select all, clear all and search', async () => {
    await create([]);

    await click('[data-testid=select-all]');
    expect(element.querySelector('[data-testid=selected-count]')?.textContent).toContain('3 of 3');

    await click('[data-testid=clear-all]');
    expect(element.querySelector('[data-testid=selected-count]')?.textContent).toContain('0 of 3');

    const search = element.querySelector('[data-testid=permission-search]') as HTMLInputElement;
    search.value = 'create';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(element.querySelectorAll('.permission-list li').length).toBe(1);
  });

  it('saves the complete selection through the API after confirmation', async () => {
    await create([200]);

    await click('#perm-201');
    await click('[data-testid=save-permissions]');
    await fixture.whenStable();

    const request = http.expectOne('/api/roles/r1/permissions');
    expect(request.request.method).toBe('PUT');
    expect([...request.request.body.permissionIds].sort()).toEqual([200, 201]);
    request.flush({ added: ['members.view'], removed: [], affectedMembers: 3 });
    http.match('/api/auth/me').forEach((r) => r.flush({ isAuthenticated: false, user: null }));
  });

  it('is read-only when the user cannot manage permissions', async () => {
    await create([200], false);

    expect(box(201).disabled).toBe(true);
    expect(element.querySelector('[data-testid=select-all]')).toBeNull();
  });
});
