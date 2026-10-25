const AUTH_PAGES = ['/login', '/forgot-password', '/reset-password'];

/**
 * Only allows in-app paths as a post-login destination, preventing open redirects such as
 * `//evil.example` or `https://evil.example`.
 */
export function safeReturnUrl(url: string | null | undefined): string {
  if (!url || !url.startsWith('/') || url.startsWith('//') || url.startsWith('/\\')) {
    return '/';
  }
  const path = url.split(/[?#]/)[0];
  return AUTH_PAGES.includes(path) ? '/' : url;
}
