import { safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it.each([
    ['/account/change-password', '/account/change-password'],
    ['/tasks?status=open#top', '/tasks?status=open#top'],
  ])('keeps in-app path %s', (input, expected) => {
    expect(safeReturnUrl(input)).toBe(expected);
  });

  it.each([null, undefined, '', 'https://evil.example', '//evil.example', '/\\evil.example', 'javascript:alert(1)', '/login', '/login?returnUrl=/x'])(
    'falls back to / for %s',
    (input) => {
      expect(safeReturnUrl(input)).toBe('/');
    },
  );
});
