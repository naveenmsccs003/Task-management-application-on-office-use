import { chromium } from 'playwright-core';
import { readFileSync } from 'node:fs';
const out = new URL('./output/', import.meta.url).pathname;
const hostLog = process.argv[2];
const base = 'http://localhost:4200';
const results = [];
const check = (name, ok, extra = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${extra ? ' — ' + extra : ''}`); };

const browser = await chromium.launch({ executablePath: process.env.E2E_CHROMIUM });
const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
const page = await ctx.newPage();

// 1. Protected route redirects to login with returnUrl
await page.goto(base + '/account/change-password');
await page.waitForURL(/\/login/);
check('protected route redirects to login', page.url().includes('returnUrl=%2Faccount%2Fchange-password'), page.url());
await page.waitForTimeout(800);
await page.screenshot({ path: `${out}/login-desktop.png` });

// 2. Empty submit shows validation
await page.click('[data-testid=login-submit]');
check('client validation messages', (await page.textContent('form')).includes('Username or email is required.'));

// 3. Wrong password → generic error
await page.fill('#userNameOrEmail', 'alice');
await page.fill('#password', 'wrong password!!');
await page.click('[data-testid=login-submit]');
await page.waitForSelector('[data-testid=login-error]');
check('generic invalid-credentials message', (await page.textContent('[data-testid=login-error]')).includes('Invalid username or password.'));

// 4. Show/hide toggle
await page.fill('#password', 'correct horse battery');
await page.click('[data-testid=toggle-password]');
// Angular renders asynchronously after the click, so wait for the DOM update rather than reading it instantly.
const toggled = await page.waitForFunction(() => document.querySelector('#password')?.getAttribute('type') === 'text', null, { timeout: 3000 }).then(() => true, () => false);
check('password toggle shows text', toggled);
await page.click('[data-testid=toggle-password]');
await page.waitForFunction(() => document.querySelector('#password')?.getAttribute('type') === 'password', null, { timeout: 3000 });

// 5. Successful login → returnUrl
await page.click('[data-testid=login-submit]');
await page.waitForURL(base + '/account/change-password');
check('login navigates to return URL', true);
const cookies = await ctx.cookies();
const session = cookies.find((c) => c.name === 'tm.session');
check('session cookie is HttpOnly + SameSite=Strict + session-only', session?.httpOnly && session?.sameSite === 'Strict' && session?.expires === -1,
  JSON.stringify({ httpOnly: session?.httpOnly, sameSite: session?.sameSite, expires: session?.expires }));
const storage = await page.evaluate(() => JSON.stringify({ ...localStorage }) + JSON.stringify({ ...sessionStorage }));
check('nothing in localStorage/sessionStorage', storage === '{}{}', storage);
check('session cookie not readable from JS', !(await page.evaluate(() => document.cookie)).includes('tm.session'));
await page.screenshot({ path: `${out}/change-password-desktop.png` });

// 6. Change password with wrong current password → server error shown
await page.fill('#currentPassword', 'not my password');
await page.fill('#newPassword', 'brand new passphrase');
await page.fill('#confirmNewPassword', 'brand new passphrase');
await page.click('[data-testid=change-password-submit]');
await page.waitForSelector('.alert-danger');
check('wrong current password reported', (await page.textContent('form')).includes('The current password is incorrect.'));

// 7. Change password successfully
await page.fill('#currentPassword', 'correct horse battery');
await page.fill('#newPassword', 'brand new passphrase');
await page.fill('#confirmNewPassword', 'brand new passphrase');
await page.click('[data-testid=change-password-submit]');
await page.waitForSelector('.alert-success');
await page.waitForTimeout(600);
await page.screenshot({ path: `${out}/change-password-success-desktop.png`, fullPage: true });
check('password changed, still signed in', (await (await page.request.get(base + '/api/auth/me')).json()).isAuthenticated === true);

// 8. Logout
await page.click('[data-testid=user-menu-toggle]');
await page.click('[data-testid=sign-out]');
await page.waitForURL(/reason=logged-out/);
check('logout shows signed-out notice', (await page.textContent('main')).includes('You have been signed out.'));
const me = await (await page.request.get(base + '/api/auth/me')).json();
check('server reports anonymous after logout', me.isAuthenticated === false);

// 9. Forgot + reset password flow
await page.click('text=Forgot password?');
await page.fill('#email', 'alice@example.com');
await page.click('button[type=submit]');
await page.waitForSelector('[data-testid=forgot-confirmation]');
check('forgot-password generic confirmation', (await page.textContent('[data-testid=forgot-confirmation]')).includes('If an account with that email exists'));
await new Promise((r) => setTimeout(r, 800));
const link = readFileSync(hostLog, "utf8").split('\n').filter((l) => l.startsWith('RESETLINK ')).pop()?.slice(10);
await page.goto(link);
await page.waitForSelector('#newPassword');
check('reset token removed from address bar', page.url() === base + '/reset-password', page.url());
await page.fill('#newPassword', 'third passphrase here');
await page.fill('#confirmNewPassword', 'third passphrase here');
await page.click('button[type=submit]');
await page.waitForURL(/reason=password-reset/);
check('reset completes and returns to login', true);

// 10. Remember me → persistent cookie
await page.fill('#userNameOrEmail', 'alice@example.com');
await page.fill('#password', 'third passphrase here');
await page.check('#rememberMe');
await page.click('[data-testid=login-submit]');
await page.waitForURL(base + '/');
const persistent = (await ctx.cookies()).find((c) => c.name === 'tm.session');
check('remember me issues persistent cookie (~14 days)', persistent.expires > Date.now() / 1000 + 13 * 86400, new Date(persistent.expires * 1000).toISOString());
await page.waitForTimeout(800);
await page.screenshot({ path: `${out}/home-desktop.png`, fullPage: true });
await page.click('[data-testid=user-menu-toggle]');
await page.waitForTimeout(300);
await page.screenshot({ path: `${out}/home-menu-desktop.png` });
await page.keyboard.press('Escape');

// 11. Session revoked server-side → next API call returns user to login
await ctx.clearCookies({ name: 'tm.session' });
await page.click('[data-testid=home-change-password]');
await page.fill('#currentPassword', 'x'); await page.fill('#newPassword', 'yyyyyyyyyyyyyy'); await page.fill('#confirmNewPassword', 'yyyyyyyyyyyyyy');
await page.click('[data-testid=change-password-submit]');
await page.waitForURL(/reason=session-expired/);
check('expired session returns user to login', (await page.textContent('main')).includes('Your session has expired'));

// 12. Responsive screenshots
for (const [name, width, height] of [['tablet', 768, 1024], ['mobile', 375, 740]]) {
  await page.setViewportSize({ width, height });
  await page.goto(base + '/login');
  await page.waitForSelector('#userNameOrEmail');
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  check(`no horizontal overflow at ${width}px`, !overflow);
  await page.screenshot({ path: `${out}/login-${name}.png` });
}

// 13. Keyboard: Enter submits
await page.fill('#userNameOrEmail', 'alice');
await page.fill('#password', 'third passphrase here');
await page.press('#password', 'Enter');
await page.waitForURL(base + '/');
check('Enter key submits the login form', true);
await page.waitForTimeout(800);
await page.screenshot({ path: `${out}/home-mobile.png`, fullPage: true });
check('no horizontal overflow on home at 375px', !(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)));
await page.click('[data-testid=home-change-password]');
await page.fill('#newPassword', 'Correct-Horse-Battery-9');
await page.waitForTimeout(800);
await page.screenshot({ path: `${out}/change-password-mobile.png`, fullPage: true });
check('no horizontal overflow on change password at 375px', !(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)));

await browser.close();
console.log(results.join("\n"));
if (results.some((r) => r.startsWith("FAIL"))) process.exit(1);
