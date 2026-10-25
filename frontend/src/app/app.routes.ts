import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guard';

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
    title: `Reset password | ${APP_NAME}`,
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
    ],
  },
  { path: '**', redirectTo: '' },
];
