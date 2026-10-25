import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard } from './core/auth/auth.guard';
import { Perm } from './core/auth/permissions';

const APP_NAME = 'Task Management System';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    title: `Sign in | ${APP_NAME}`,
    loadComponent: () => import('./features/auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'forgot-password',
    canActivate: [guestGuard],
    title: `Forgot password | ${APP_NAME}`,
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password.component').then((m) => m.ForgotPasswordComponent),
  },
  {
    path: 'reset-password',
    title: `Set password | ${APP_NAME}`,
    loadComponent: () =>
      import('./features/auth/reset-password/reset-password.component').then((m) => m.ResetPasswordComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    children: [
      {
        path: '',
        title: APP_NAME,
        loadComponent: () => import('./features/home/home.component').then((m) => m.HomeComponent),
      },
      {
        path: 'account/change-password',
        title: `Change password | ${APP_NAME}`,
        loadComponent: () =>
          import('./features/auth/change-password/change-password.component').then((m) => m.ChangePasswordComponent),
      },
      {
        path: 'members',
        canActivate: [permissionGuard(Perm.members.view)],
        title: `Members | ${APP_NAME}`,
        loadComponent: () => import('./features/members/members-list.component').then((m) => m.MembersListComponent),
      },
      {
        path: 'members/new',
        canActivate: [permissionGuard(Perm.members.create)],
        title: `Add member | ${APP_NAME}`,
        loadComponent: () => import('./features/members/member-form.component').then((m) => m.MemberFormComponent),
      },
      {
        path: 'members/:id',
        canActivate: [permissionGuard(Perm.members.viewDetails)],
        title: `Member | ${APP_NAME}`,
        loadComponent: () => import('./features/members/member-detail.component').then((m) => m.MemberDetailComponent),
      },
      {
        path: 'members/:id/edit',
        canActivate: [permissionGuard(Perm.members.viewDetails)],
        title: `Edit member | ${APP_NAME}`,
        loadComponent: () => import('./features/members/member-form.component').then((m) => m.MemberFormComponent),
      },
      {
        path: 'roles',
        canActivate: [permissionGuard(Perm.roles.view)],
        title: `Roles & Permissions | ${APP_NAME}`,
        loadComponent: () => import('./features/roles/roles-list.component').then((m) => m.RolesListComponent),
      },
      {
        path: 'roles/new',
        canActivate: [permissionGuard(Perm.roles.create)],
        title: `Create role | ${APP_NAME}`,
        loadComponent: () => import('./features/roles/role-form.component').then((m) => m.RoleFormComponent),
      },
      {
        path: 'roles/:id',
        canActivate: [permissionGuard(Perm.roles.view)],
        title: `Role | ${APP_NAME}`,
        loadComponent: () => import('./features/roles/role-detail.component').then((m) => m.RoleDetailComponent),
      },
      {
        path: 'roles/:id/edit',
        canActivate: [permissionGuard(Perm.roles.edit)],
        title: `Edit role | ${APP_NAME}`,
        loadComponent: () => import('./features/roles/role-form.component').then((m) => m.RoleFormComponent),
      },
      {
        path: 'forbidden',
        title: `Access denied | ${APP_NAME}`,
        loadComponent: () => import('./features/forbidden/forbidden.component').then((m) => m.ForbiddenComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
