// Phase 2 browser flow: roles, permissions, members, overrides and backend enforcement as seen in the UI.
import { chromium } from 'playwright-core';
import { writeFileSync } from 'node:fs';

const out = new URL('./output/', import.meta.url).pathname;
const base = 'http://localhost:4200';
const results = [];
const check = (name, ok, extra = '') => results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${extra ? ' — ' + extra : ''}`);
// A real 1x1 PNG for the profile-photo upload.
const photoPath = `${out}/avatar.png`;
writeFileSync(photoPath, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64'));
const browser = await chromium.launch({ executablePath: process.env.E2E_CHROMIUM });

async function login(page, user, password) {
  await page.goto(base + '/login');
  await page.fill('#userNameOrEmail', user);
  await page.fill('#password', password);
  await page.click('[data-testid=login-submit]');
  await page.waitForURL(base + '/');
}

async function confirmDialog(page) {
  await page.click('[data-testid=confirm-ok]');
}

const admin = await (await browser.newContext({ viewport: { width: 1280, height: 860 } })).newPage();
try {
  // 1. Administrator sees both management tabs.
  await login(admin, 'admin', 'operator passphrase 2026');
  check('admin sees Members and Roles tabs',
    (await admin.isVisible('[data-testid=nav-members]')) && (await admin.isVisible('[data-testid=nav-roles]')));

  // 2. Create a custom role and configure permissions.
  await admin.click('[data-testid=nav-roles]');
  await admin.waitForSelector('[data-testid=role-row]');
  await admin.waitForTimeout(500);
  await admin.screenshot({ path: `${out}/roles-list-desktop.png`, fullPage: true });
  await admin.click('[data-testid=create-role]');
  await admin.fill('#roleName', '  Developer  ');
  await admin.fill('#roleDescription', 'Builds and maintains features');
  await admin.click('[data-testid=save-role]');
  await admin.waitForSelector('[data-testid=role-name]');
  check('role created with trimmed name', (await admin.textContent('[data-testid=role-name]')).trim() === 'Developer');
  await admin.click('#perm-201'); // members.view (auto-selects members.access)
  check('selecting an operation selects tab access', await admin.isChecked('#perm-200'));
  await admin.click('#perm-202'); // members.view_details
  await admin.waitForTimeout(400);
  await admin.screenshot({ path: `${out}/role-permissions-desktop.png`, fullPage: true });
  await admin.click('[data-testid=save-permissions]');
  await confirmDialog(admin);
  await admin.waitForSelector('[data-testid=toast]');
  check('role permissions saved', (await admin.textContent('[data-testid=toast]')).includes('Permissions saved'));

  // 3. Duplicate role name is rejected by the server.
  await admin.goto(base + '/roles/new');
  await admin.fill('#roleName', 'developer');
  await admin.click('[data-testid=save-role]');
  await admin.waitForSelector('.invalid-feedback');
  check('duplicate role name rejected', (await admin.textContent('form')).includes('already exists'));

  // 4. Create a member with that role and an administrator-set password.
  await admin.goto(base + '/members/new');
  await admin.fill('#displayName', 'Dev One');
  await admin.fill('#email', 'dev1@example.com');
  await admin.fill('#userName', 'dev1');
  await admin.waitForSelector('#roleId option:has-text("Developer")', { state: 'attached' });
  await admin.selectOption('#roleId', { label: 'Developer' });
  await admin.click('label[for=setupPassword]');
  await admin.fill('#password', 'developer passphrase 1');
  await admin.fill('#confirmPassword', 'developer passphrase 1');
  await admin.fill('#dateOfBirth', '1995-03-14');
  await admin.fill('#city', 'Chennai');
  await admin.setInputFiles('[data-testid=photo-input]', photoPath);
  check('photo preview shown before creation', await admin.isVisible('[data-testid=photo-preview] img'));
  check('age is calculated from date of birth', /\d+/.test(await admin.textContent('[data-testid=age]')));
  await admin.screenshot({ path: `${out}/member-form-desktop.png`, fullPage: true });
  await admin.click('[data-testid=save-member]');
  await admin.waitForSelector('[data-testid=member-name]');
  check('member created and shown', (await admin.textContent('[data-testid=member-name]')).includes('Dev One'));
  const memberUrl = admin.url();
  await admin.waitForSelector('img[alt^="Profile photo of"]');
  check('photo chosen during creation was uploaded', await admin.isVisible('img[alt^="Profile photo of"]'));
  await admin.waitForTimeout(400);
  await admin.screenshot({ path: `${out}/member-detail-desktop.png`, fullPage: true });

  // 4b. Member list: Member ID column and per-row actions.
  await admin.goto(base + '/members');
  await admin.waitForSelector('[data-testid=member-row]:has-text("Dev One")');
  const devRow = admin.locator('[data-testid=member-row]', { hasText: 'Dev One' });
  check('member ID column shown', /^[0-9A-F]{8}$/.test((await devRow.locator('[data-testid=member-id]').textContent()).trim()));
  await devRow.locator('[data-testid=row-actions]').click();
  const menuText = await admin.textContent('[role=menu]');
  check('row actions menu offers role, password and status actions',
    ['Change role', 'Reset password', 'Deactivate'].every((t) => menuText.includes(t)));
  await admin.screenshot({ path: `${out}/members-row-actions-desktop.png` });
  const adminRow = admin.locator('[data-testid=member-row]', { hasText: '@admin' });
  check('own account has no row actions', (await adminRow.locator('[data-testid=row-actions]').count()) === 0);
  await admin.keyboard.press('Escape');
  await admin.goto(memberUrl);
  await admin.waitForSelector('[data-testid=member-name]');

  // 5. Member-specific override: allow members.export for this member only.
  await admin.click('[data-testid=permissions-tab]');
  await admin.waitForSelector('[data-key="members.export"]');
  await admin.click('label[for="ov-213-Allow"]');
  check('override preview shows Allow', (await admin.textContent('[data-key="members.export"] [data-testid=effective]')).trim() === 'Allow');
  await admin.waitForTimeout(300);
  await admin.screenshot({ path: `${out}/member-permissions-desktop.png`, fullPage: true });
  await admin.click('[data-testid=save-overrides]');
  await confirmDialog(admin);
  await admin.waitForSelector('text=Member permissions saved.');
  check('override saved', (await admin.textContent('[data-testid=override-count]')).includes('1'));

  // 6. The member sees only what their effective permissions allow.
  const memberContext = await browser.newContext({ viewport: { width: 1280, height: 860 } });
  const member = await memberContext.newPage();
  await login(member, 'dev1', 'developer passphrase 1');
  check('member sees Members tab but not Roles tab',
    (await member.isVisible('[data-testid=nav-members]')) && !(await member.isVisible('[data-testid=nav-roles]')));
  await member.click('[data-testid=nav-members]');
  await member.waitForSelector('[data-testid=member-row]');
  check('member has no Add member button', !(await member.isVisible('[data-testid=create-member]')));
  check('override grants export button', await member.isVisible('[data-testid=export-members]'));
  await member.goto(base + '/roles');
  await member.waitForURL(/\/forbidden/);
  check('direct navigation to roles is denied', (await member.textContent('main')).includes('Access denied'));
  const apiStatus = await member.evaluate(async () => (await fetch('/api/roles')).status);
  check('API rejects the member on roles endpoint', apiStatus === 403, String(apiStatus));
  await member.goto(base + '/members');
  await member.waitForSelector('[data-testid=member-row]');
  await member.click('a:has-text("Dev One")');
  await member.waitForSelector('[data-testid=personal-section]');
  check('personal information hidden without permission',
    (await member.textContent('[data-testid=personal-section]')).includes('do not have permission'));

  // 7. Revoking access applies immediately: deactivate the member.
  await admin.goto(memberUrl);
  await admin.waitForSelector('[data-testid=deactivate-member]');
  await admin.click('[data-testid=deactivate-member]');
  await confirmDialog(admin);
  await admin.waitForSelector('[data-testid=member-status]:has-text("Inactive")');
  check('member deactivated', true);
  await member.goto(base + '/members');
  await member.waitForURL(/\/login/);
  check('deactivated member is signed out on next navigation', true);

  // 8. Mobile layouts.
  await admin.setViewportSize({ width: 375, height: 760 });
  for (const [name, path, selector] of [['members-list', '/members', '[data-testid=member-row]'], ['roles-list', '/roles', '[data-testid=role-row]']]) {
    await admin.goto(base + path);
    await admin.waitForSelector(selector);
    await admin.waitForTimeout(400);
    check(`no horizontal page overflow on ${name} at 375px`, !(await admin.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)));
    await admin.screenshot({ path: `${out}/${name}-mobile.png`, fullPage: true });
  }
  await memberContext.close();
} catch (error) {
  check('flow completed without errors', false, String(error).split('\n')[0]);
  await admin.screenshot({ path: `${out}/rbac-failure.png`, fullPage: true }).catch(() => {});
}

await browser.close();
console.log(results.join('\n'));
if (results.some((r) => r.startsWith('FAIL'))) process.exit(1);
