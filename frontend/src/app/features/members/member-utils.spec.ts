import { FormControl } from '@angular/forms';
import { ageFrom, pastDateValidator } from './member-utils';

describe('member utils', () => {
  it('calculates age from date of birth without storing it', () => {
    const today = new Date(2026, 9, 25);
    expect(ageFrom('1990-10-25', today)).toBe(36);
    expect(ageFrom('1990-10-26', today)).toBe(35);
    expect(ageFrom('', today)).toBeNull();
  });

  it('rejects future and implausible dates of birth', () => {
    expect(pastDateValidator(new FormControl('2999-01-01', { nonNullable: true }))).toEqual({ futureDate: true });
    expect(pastDateValidator(new FormControl('1850-01-01', { nonNullable: true }))).toEqual({ invalidDate: true });
    expect(pastDateValidator(new FormControl('1990-05-17', { nonNullable: true }))).toBeNull();
  });
});
