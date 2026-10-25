export interface PasswordStrength {
  score: 0 | 1 | 2 | 3 | 4;
  label: 'Too short' | 'Weak' | 'Fair' | 'Good' | 'Strong';
}

const LABELS: PasswordStrength['label'][] = ['Too short', 'Weak', 'Fair', 'Good', 'Strong'];

/**
 * Rough, client-side guidance only (length matters most). The server's Identity policy is what
 * actually accepts or rejects a password.
 */
export function passwordStrength(password: string, minLength: number): PasswordStrength {
  if (password.length < minLength || /^(.)\1+$/.test(password)) {
    return { score: 0, label: LABELS[0] };
  }

  const variety = [/[a-z]/, /[A-Z]/, /\d/, /[^A-Za-z\d]/].filter((pattern) => pattern.test(password)).length;
  let score = 1;
  if (password.length >= minLength + 4) score++;
  if (variety >= 3) score++;
  if (password.length >= minLength + 8 || (password.length >= minLength + 2 && variety >= 3)) score++;
  const clamped = Math.min(score, 4) as PasswordStrength['score'];
  return { score: clamped, label: LABELS[clamped] };
}
