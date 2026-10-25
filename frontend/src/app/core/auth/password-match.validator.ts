import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Group validator: `confirmNewPassword` must equal `newPassword`. */
export const passwordsMatchValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const password = group.get('newPassword')?.value;
  const confirmation = group.get('confirmNewPassword')?.value;
  return password && confirmation && password !== confirmation ? { passwordMismatch: true } : null;
};
