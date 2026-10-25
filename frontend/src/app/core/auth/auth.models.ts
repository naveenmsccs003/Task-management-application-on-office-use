/** Profile returned by the API. The `id` is the stable identifier for future modules. */
export interface CurrentUser {
  id: string;
  userName: string;
  email: string | null;
  displayName: string | null;
  roles: string[];
  /** Effective permission keys. Used only to shape the UI; the API re-checks every request. */
  permissions: string[];
  isAdministrator: boolean;
}

export interface AuthStatus {
  isAuthenticated: boolean;
  user: CurrentUser | null;
}

export interface LoginRequest {
  userNameOrEmail: string;
  password: string;
  rememberMe: boolean;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmNewPassword: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  userId: string;
  token: string;
  newPassword: string;
  confirmNewPassword: string;
  isInvitation?: boolean;
}

export interface MessageResponse {
  message: string;
}

/** Client-side hint only; the server's configured policy is authoritative. */
export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 128;
