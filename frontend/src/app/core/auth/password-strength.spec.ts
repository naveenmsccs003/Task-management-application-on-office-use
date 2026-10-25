import { passwordStrength } from './password-strength';

describe('passwordStrength', () => {
  it('reports passwords below the minimum length as too short', () => {
    expect(passwordStrength('short', 12)).toEqual({ score: 0, label: 'Too short' });
  });

  it('treats a single repeated character as too weak', () => {
    expect(passwordStrength('aaaaaaaaaaaaaaaa', 12).score).toBe(0);
  });

  it('rewards length and variety', () => {
    expect(passwordStrength('abcdefghijkl', 12).score).toBe(1);
    expect(passwordStrength('correct horse battery staple', 12).score).toBeGreaterThanOrEqual(3);
    expect(passwordStrength('Correct-Horse-Battery-9', 12)).toEqual({ score: 4, label: 'Strong' });
  });
});
