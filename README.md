# Task Management System

Implemented so far:

- **Phase 1:** login and authentication foundation.
- **Phase 2:** Role Management and Member Management, with database-driven permissions and member-specific overrides.

Projects, Tasks, Dashboard, Meeting Notes, Milestones and Global Settings are not implemented yet. Their
permissions are registered as *inactive*, so they appear as "Coming soon" and are never granted.

| Layer     | Technology                                                                       |
|-----------|----------------------------------------------------------------------------------|
| Frontend  | Angular 21.2 (standalone components, signals), TypeScript 5.9, Bootstrap 5.3 (CSS only) |
| Backend   | ASP.NET Core 10 Web API (C#), ASP.NET Core Identity                              |
| Database  | Microsoft SQL Server via Entity Framework Core 10 (migrations)                   |
| Tests     | xUnit + WebApplicationFactory (backend), Vitest + jsdom via `ng test` (frontend) |

> Angular 21 rather than 22: Angular CLI 22 requires Node.js ≥ 22.22.3, and the development machine
> has Node.js 22.22.1. Upgrading Node allows `ng update` to Angular 22 without code changes.

## Repository layout

```
backend/
  TaskManagement.slnx
  src/TaskManagement.Api/
    Authentication/   AuthenticationService, options, session store, sign-in manager, security log
    Authorization/    PermissionCatalog (registry), PermissionService (central resolver),
                      AccessPolicy (delegation rules), [RequirePermission]
    Auditing/         AuditService (role/permission/member audit trail)
    Roles/            RoleService, PermissionRegistry, DTOs
    Members/          MemberService, invitation tokens, DTOs
    Controllers/      Auth, Roles, Permissions, Members controllers
    Data/             ApplicationUser, ApplicationDbContext, UserSession, Migrations/
    Email/            SMTP / pickup-directory implementation of Identity's IEmailSender
    Infrastructure/   CSRF filter, security headers
    Setup/            AdminProvisioner + seed-admin command (first administrator)
  tests/TaskManagement.Api.Tests/
e2e/                  Browser test: DevHost (API on in-memory SQLite) + Playwright script
frontend/
  src/app/core/auth/  AuthService (+ can()), authGuard/guestGuard/permissionGuard, authInterceptor
  src/app/core/api/   RolesApi, MembersApi, models
  src/app/core/ui/    Toasts, confirmation dialog, modal, pagination
  src/app/features/auth/  Login, ForgotPassword, ResetPassword (also invitations), ChangePassword
  src/app/features/roles/    Role list, form, detail + permission matrix
  src/app/features/members/  Member list, form, detail, change-role dialog, override editor
  src/app/layout/     Authenticated shell (navbar, sign out)
```

## Running locally

Prerequisites: .NET SDK 10, Node.js ≥ 22.12, a SQL Server instance (LocalDB, Docker or a server).

### 1. Configure the database connection (not committed)

`appsettings.Development.json` points at Windows LocalDB. On Linux/macOS, or for any other server,
store the connection string in user secrets:

```bash
cd backend/src/TaskManagement.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=localhost,1433;Database=TaskManagement;User Id=<user>;Password=<password>;TrustServerCertificate=True"
```

For example, SQL Server in Docker:
`docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='<strong password>' -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest`

### 2. Apply migrations

```bash
cd backend
dotnet tool restore
dotnet ef database update --project src/TaskManagement.Api
# For production, review and apply an idempotent script instead:
dotnet ef migrations script --idempotent --project src/TaskManagement.Api -o auth.sql
```

The application never calls `EnsureCreated` or migrates automatically.

### 3. Create the first administrator

There is no public registration page or setup endpoint. An operator with access to the server runs:

```bash
cd backend/src/TaskManagement.Api
dotnet run -- seed-admin --username admin --email admin@example.com --display-name "Administrator"
# Prompts for the password (hidden input). For automation, provide it via the TM_ADMIN_PASSWORD
# environment variable instead -- never via committed configuration.
```

The command refuses to run while migrations are pending or once any user holds the `Administrator`
role, so the setup cannot be repeated. In a published build use
`dotnet TaskManagement.Api.dll seed-admin ...`.

### 4. Start the API and the Angular app

```bash
# terminal 1
cd backend/src/TaskManagement.Api && dotnet run          # http://localhost:5028

# terminal 2
cd frontend && npm install && npm start                  # http://localhost:4200
```

The Angular dev server proxies `/api` to the API (`frontend/proxy.conf.json`), so the browser sees a
single origin. In development, password-reset emails are written as `.eml` files to
`backend/src/TaskManagement.Api/App_Data/mail/` (git-ignored).

### Tests

```bash
# Backend: 122 integration/unit tests (in-memory SQLite, no database server needed)
cd backend && dotnet test

# Backend against a real SQL Server: applies the EF migrations to a throwaway database, provisions
# the first administrator, signs in/out, then drops the database
TM_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=<pw>;TrustServerCertificate=True" \
  dotnet test --filter "FullyQualifiedName~SqlServerMigrationTests"

# Frontend: 78 component/service tests (Vitest + jsdom)
cd frontend && npx ng test --watch=false

# End-to-end browser flows (41 checks): start the real API (in-memory SQLite) and `ng serve`, then drive
# Chromium through the authentication flows (flow.mjs) and roles/members/overrides/enforcement
# (flow-rbac.mjs). Seeded users: alice / correct horse battery (no role), admin / operator passphrase 2026
# (Administrator). Screenshots go to e2e/output/.
cd e2e && E2E_CHROMIUM=/path/to/chromium ./run.sh
```

### Production build and hosting

`npx ng build` writes the app to `frontend/dist/frontend/browser`. Serve it from the **same origin**
as the API, either by copying it into `backend/src/TaskManagement.Api/wwwroot` (the API serves it and
falls back to `index.html` for client routes) or behind a reverse proxy that routes `/api` to the API.
Cookies are `SameSite=Strict`, and CORS is deliberately not enabled. A cross-origin deployment needs a
documented review of cookie and CORS settings first.

## Configuration

| Key | Default | Purpose |
|-----|---------|---------|
| `ConnectionStrings:DefaultConnection` | — (secret) | SQL Server connection |
| `Frontend:BaseUrl` | `https://tasks.example.com` (dev: `http://localhost:4200`) | Origin used in password-reset links. Never derived from the request's Host header |
| `Authentication:PasswordMinimumLength` | 12 | Minimum password length |
| `Authentication:MaxFailedAccessAttempts` | 5 | Failed logins before lockout |
| `Authentication:LockoutMinutes` | 15 | Lockout duration |
| `Authentication:SessionIdleTimeoutMinutes` | 120 | Idle timeout without "Remember me" (sliding) |
| `Authentication:SessionMaxLifetimeHours` | 12 | Hard limit without "Remember me" |
| `Authentication:RememberMeDays` | 14 | Persistent cookie lifetime (sliding) |
| `Authentication:RememberMeMaxLifetimeDays` | 30 | Hard limit with "Remember me" |
| `Authentication:SecurityStampValidationMinutes` | 5 | How often sessions are re-checked against the account |
| `Authentication:PasswordResetTokenLifetimeMinutes` | 30 | Reset link validity |
| `Authentication:LoginRateLimitPermits` / `...WindowSeconds` | 10 / 60 | Per-IP limit for login and change-password |
| `Authentication:PasswordResetRateLimitPermits` / `...WindowSeconds` | 5 / 900 | Per-IP limit for forgot/reset password |
| `Email:DeliveryMethod` | `None` (dev: `PickupDirectory`) | `None`, `PickupDirectory` or `Smtp` |
| `Email:From`, `Email:SmtpHost`, `Email:SmtpPort`, `Email:SmtpEnableSsl`, `Email:SmtpUserName` | | SMTP settings |
| `Email:SmtpPassword` | — (secret) | Supply via user secrets or the `Email__SmtpPassword` environment variable |
| `TM_ADMIN_PASSWORD` (env var) | — | Optional non-interactive password for `seed-admin` |

Environment variables use `__` as the separator, for example
`ConnectionStrings__DefaultConnection` or `Authentication__LockoutMinutes`.

## API

| Method | Route | Auth | Success | Failures |
|--------|-------|------|---------|----------|
| GET  | `/api/auth/me` | anonymous | 200 `{ isAuthenticated, user }`; also issues the CSRF token cookie | — |
| POST | `/api/auth/login` | anonymous | 200 current user; sets session cookie | 400 validation, 401 generic "Invalid username or password.", 429 |
| POST | `/api/auth/logout` | anonymous (idempotent) | 204 | — |
| POST | `/api/auth/change-password` | required | 204 | 400 validation/policy/wrong current password, 401, 429 |
| POST | `/api/auth/forgot-password` | anonymous | 202 same message whether or not the account exists | 400 validation, 429 |
| POST | `/api/auth/reset-password` | anonymous | 204 | 400 invalid/expired/used token or policy violation, 429 |

All POST requests require the `X-XSRF-TOKEN` header (see CSRF below). Errors use RFC 7807 Problem
Details. Unauthenticated requests get **401**; authenticated requests without permission get **403**.
The API never redirects. `user` contains only `id`, `userName`, `email`, `displayName` and `roles`.

## Security design

**Passwords.** Hashing and verification use ASP.NET Core Identity (PBKDF2), with no custom
cryptography. The policy favours length over composition: at least 12 characters, at most 128,
4 or more distinct characters, and no password containing the user name or email local part.
No forced upper/lower/digit/symbol rules, since those tend to produce predictable passwords
(NIST SP 800-63B). Changing or resetting a password requires a confirmation field, checked on the
server. Passwords are never logged, put in URLs, stored in the browser, or returned by the API.

**Login and lockout.** Users sign in with a user name or email. User names cannot contain `@`, so
the two never collide. Every failure (unknown account, wrong password, inactive account, locked
account) returns the same 401 message. Unknown, inactive and locked accounts still run a dummy
password hash, so response timing doesn't reveal account state. Identity counts failed attempts
and locks the account for 15 minutes after 5 failures. Unknown user names are never locked, because
no account exists to lock. Lockout is temporary; accounts are never deactivated automatically.
Per-IP fixed-window rate limits apply on top of lockout.

**Sessions (server-managed cookies).** The session cookie `tm.session` is `HttpOnly`,
`SameSite=Strict` and `Secure` outside Development. It holds only a data-protected random key. The
authentication ticket is stored in the `UserSessions` table, which keeps the SHA-256 hash of the key
plus a data-protected ticket. Nothing is placed in `localStorage` or `sessionStorage`. Because the
server holds the session:
- **Logout** deletes the session row, so a copied cookie stops working immediately.
- **Change password** deletes all other sessions of the user and re-issues the current one with the new security stamp.
- **Password reset** deletes all sessions of the user.
- **Deactivation or security-stamp change** (future member management) ends sessions at the next validation, at most 5 minutes later. `AuthenticationService.RevokeAllSessionsAsync` revokes them immediately.
- A background job removes expired rows every hour.

**Remember me.** Unchecked: a browser-session cookie (no `Expires`), ended after 120 minutes idle
and at most 12 hours after login. Checked: a persistent cookie that slides up to 14 days and can
never outlive 30 days from sign-in; the absolute limit is enforced server-side. A persistent cookie
stops working when it expires, on logout, on password change or reset, or when the account is
deactivated. Remember Me never bypasses password verification, lockout or authorization.

**CSRF.** A global filter validates an ASP.NET Core antiforgery token on every non-GET request,
login and logout included (this prevents login CSRF and forced logout). The token is bound to the
current identity. `GET /api/auth/me`, login and logout issue a fresh token in the JavaScript-readable
`XSRF-TOKEN` cookie; Angular's built-in XSRF support sends it back as `X-XSRF-TOKEN`.
`SameSite=Strict` cookies add a second layer.

**Password reset.** Tokens come from Identity's data-protection token provider: signed,
time-limited (30 minutes) and bound to the user's security stamp. Nothing is stored in the database,
and a token stops working after one successful use. Links carry the user id, never the email. The
Angular page removes the token from the address bar and history, and the page sets
`Referrer-Policy: no-referrer`. If email sending fails, the error is logged and the response stays the same.

**Authorization foundation.**
- Authentication and authorization are kept separate. A fallback policy requires an authenticated user on every endpoint unless it opts out with `[AllowAnonymous]`.
- Signing in grants no privileges; the `Administrator` role is assigned only by the setup command.
- Future modules declare permissions with `[RequirePermission("tasks.create")]`. These resolve through `IPermissionService`, which denies everything by default. The Roles and Permissions module will provide a database-backed `IPermissionService` without touching the login code.
- Every check runs on the backend. The Angular guards only shape navigation.

**Security logging.** Structured events with IDs 1000–1040 and UTC timestamps cover login
success, failure, lockout and inactive accounts, logout, password change, reset request and
completion, and administrator creation. Events record the user id and client IP. They never record
passwords, tokens, cookies or the submitted login identifier.

**Other.** Responses carry `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy: no-referrer` and `Cache-Control: no-store` on `/api`. HSTS is enabled outside
Development. Unhandled exceptions return generic Problem Details with no stack traces.

## Phase 2: Roles, permissions and members

### Permission registry
`Authorization/PermissionCatalog.cs` is the only place permissions are defined. Each has a fixed id, a
unique machine-readable key (for example `members.change_role`), a display name, a description, a
module, a parent (the module's *Tab Access* permission), and a flag saying whether an individual member
may be granted it. The rows are seeded into the `Permissions` table through EF Core migrations. Clients
can only read them; they can never create one.

To add a module later:

1. Add its permissions to the catalog. Use new ids and never reuse old ones.
2. Mark the module as implemented once its endpoints exist.
3. Protect the endpoints with `[RequirePermission("<key>")]`.
4. Add a migration.

Implemented keys:

- **Members:** `members.access`, `.view`, `.view_details`, `.create`, `.edit`, `.change_email`,
  `.delete`, `.activate`, `.deactivate`, `.change_role`, `.reset_password`,
  `.view_personal_information`, `.edit_personal_information`, `.export`, `.manage_permissions`,
  `.view_audit`.
- **Roles:** `roles.access`, `.view`, `.create`, `.edit`, `.delete`, `.manage_permissions`,
  `.view_audit`.

### Permission resolution
`PermissionService` is the only resolver. It is used by every endpoint check, by `/api/auth/me` and by
the administrator policy. Rules, in order:

1. Unknown or inactive users get nothing.
2. Unknown permissions, and inactive (unimplemented) ones, are never granted, even to administrators.
3. Members of the active system role **Administrator** get every active permission.
4. Otherwise, start from the defaults of the member's role, if the role is active.
5. A member-level **Deny** removes a permission, even when the role grants it.
6. A member-level **Allow** adds a permission only if that permission may be granted individually.
   Security-administration permissions cannot be: `roles.create/edit/delete/manage_permissions`,
   `members.change_role` and `members.manage_permissions` come only from roles.
7. A permission inside a module counts only when that module's Tab Access is also effective.
8. Anything not granted is denied.

"Inherit" is stored as the absence of an override row. Permissions are resolved from the database on
every request; results are cached only within a single request. A revoked permission therefore stops
working on the very next request, and there is no cache to invalidate.

### Delegation and escalation rules
- Non-administrators can grant only permissions they currently hold. This applies to role permission
  changes and to member Allow overrides.
- Assigning a role requires the role to be active and its permissions to be within the assigner's own.
- Only administrators can assign the Administrator role, or edit, deactivate, re-role, reset or delete
  an administrator account.
- Nobody can change their own role, status, overrides or deletion. Non-administrators can't edit the
  permissions of a role they hold.
- The system role can't be renamed, deactivated, deleted or have its permissions edited.
- The last active administrator can't be removed.
- **Recovery:** `dotnet run -- grant-admin --email <address>` restores administrator access to an
  existing account. It is an operator-only command and works only when no active administrator exists.
- Updates use explicit DTOs. Role and status have their own endpoints. Each field group needs its own
  permission: name and username need `edit`, email needs `change_email`, and personal information needs
  `edit_personal_information`. Unknown JSON properties are ignored.
- The member list has a Member ID column and a per-row actions menu (Change role, Reset password,
  Activate/Deactivate, Delete). The menu shows only actions the user may take; the API enforces them
  regardless.
- Personal information is returned only to callers with `members.view_personal_information`;
  otherwise the field is `null`. Age is calculated from the date of birth and never stored.
- **Account lifecycle:** deactivating a member ends all their sessions immediately. A member must be
  deactivated before they can be deleted. Audit history is kept after deletion.
- **Password setup for new members:** either a 3-day, single-use invitation link (recommended), or an
  administrator-set password hashed by Identity. Passwords are never logged or returned.
- **Profile photos:** JPEG or PNG only, checked by file signature rather than the declared type, up to 1 MB.
  A photo can be chosen while creating a member; it is uploaded right after the account is created.
- **CSV export** contains account fields only, and cells are protected against spreadsheet formula
  injection.

### API (Phase 2)
| Method | Route | Permission |
|---|---|---|
| GET | `/api/roles?search=&status=&sort=&direction=&page=&pageSize=` | roles.view |
| GET | `/api/roles/assignable` | members.access |
| GET / POST / PUT / DELETE | `/api/roles/{id}` (`DELETE ?reassignToRoleId=`) | roles.view / .create / .edit / .delete |
| GET / PUT | `/api/roles/{id}/permissions` | roles.view / roles.manage_permissions |
| GET | `/api/roles/{id}/audit` | roles.view_audit |
| GET | `/api/permissions` | roles.view |
| GET | `/api/members?search=&roleId=&status=&sort=&direction=&page=&pageSize=` | members.view |
| GET | `/api/members/export` | members.export |
| GET / POST / PUT / DELETE | `/api/members/{id}` | view_details / create / per-field / delete |
| POST | `/api/members/{id}/change-role` | members.change_role |
| POST | `/api/members/{id}/status` | members.activate or members.deactivate |
| POST | `/api/members/{id}/reset-password` | members.reset_password |
| GET / PUT | `/api/members/{id}/permissions` | members.view_details / members.manage_permissions |
| GET | `/api/members/{id}/audit` | members.view_audit |
| GET / PUT / DELETE | `/api/members/{id}/photo` | view_details / edit_personal_information |

### Database (migration `RolesAndMembers`)
- **New tables:** `Permissions` (seeded), `RolePermissions` (primary key on role and permission, so
  duplicates are impossible), `MemberPermissionOverrides` (unique per user and permission),
  `MemberPersonalInformation`, `MemberProfilePhotos` and `AuditLogs`.
- **New columns:** on `AspNetRoles` (Description, IsActive, IsSystem, created and updated timestamps,
  CreatedBy) and on `AspNetUsers` (LastLoginAtUtc, CreatedByUserId).
- **Existing data:** an existing Administrator role is marked as the system role.
- **Nothing destructive:** applying the migration drops nothing. Drops appear only in its rollback (`Down`).

## Known limitations / next steps

- SQL Server was not available in the build environment. The migration's SQL script was generated and reviewed, and the admin-provisioning logic is covered by tests on SQLite, but nothing has been run against a real SQL Server yet. Run the `TM_TEST_SQLSERVER` test above once against your server to confirm the migration end to end.
- Data-protection keys use the framework default location. For multiple instances or containers, persist them to shared storage (for example the database or Azure Blob Storage) and protect them at rest. Otherwise sessions and reset links become invalid on redeploy.
- Behind a reverse proxy, configure `ForwardedHeaders` so per-IP rate limiting and logs see the real client address.
- `forgot-password` takes slightly longer for existing accounts (token generation and email). Moving email delivery to a background queue would remove this timing signal.
- Consider a breached-password check (for example k-anonymity Pwned Passwords), a Content-Security-Policy for the SPA, MFA, and an admin UI for unlocking and deactivating users (part of the Members module).
- Sign-in security events (login, logout, lockout) go to the logging provider. Role, permission and member administration is recorded in the `AuditLogs` table.
- Effective permissions are resolved from the database on every request (a few small queries). If load requires it, add a short-lived cache that is invalidated on role, override and membership changes.
- Profile photos are stored in the database (up to 1 MB each). Move them to blob storage if usage grows.
- **Next phase:** Projects and Tasks. Mark their modules as implemented in `PermissionCatalog` and protect their endpoints with the existing `tasks.*` and `projects.*` keys. Add record-level rules (for example, project membership) in the resolver's step 4.
