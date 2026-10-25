import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Age in whole years from an ISO date (yyyy-MM-dd); null when empty or invalid. Never stored. */
export function ageFrom(dateOfBirth: string | null | undefined, today = new Date()): number | null {
  if (!dateOfBirth) return null;
  const [year, month, day] = dateOfBirth.split('-').map(Number);
  if (!year || !month || !day) return null;
  let age = today.getFullYear() - year;
  if (today.getMonth() + 1 < month || (today.getMonth() + 1 === month && today.getDate() < day)) age--;
  return age >= 0 ? age : null;
}

/** Date of birth must be a real date between 1900-01-01 and today. */
export function pastDateValidator(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value;
  if (!value) return null;
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime()) || value < '1900-01-01') return { invalidDate: true };
  return date > new Date() ? { futureDate: true } : null;
}
