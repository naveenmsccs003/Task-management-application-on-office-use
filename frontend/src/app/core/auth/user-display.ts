import { CurrentUser } from './auth.models';

export function displayNameOf(user: CurrentUser | null): string {
  return user?.displayName || user?.userName || '';
}

/** Up to two initials for avatars, e.g. "Alice Smith" -> "AS". */
export function initialsOf(user: CurrentUser | null): string {
  const parts = displayNameOf(user).split(/[\s._-]+/).filter(Boolean);
  return parts.slice(0, 2).map((part) => part[0].toUpperCase()).join('') || '?';
}
