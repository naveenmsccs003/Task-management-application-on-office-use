import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from './api-error';

describe('toApiError', () => {
  it('uses the server detail for 401 responses', () => {
    const error = new HttpErrorResponse({ status: 401, error: { detail: 'Invalid username or password.' } });
    expect(toApiError(error, 'fallback').message).toBe('Invalid username or password.');
  });

  it('maps validation errors to camelCase fields', () => {
    const error = new HttpErrorResponse({ status: 400, error: { errors: { NewPassword: ['Too short.'] } } });
    expect(toApiError(error, 'fallback').fieldErrors).toEqual({ newPassword: ['Too short.'] });
  });

  it('never surfaces server error bodies for 5xx responses', () => {
    const error = new HttpErrorResponse({ status: 500, error: { detail: 'NullReferenceException at Foo.Bar()' } });
    expect(toApiError(error, 'Something went wrong.').message).toBe('Something went wrong.');
  });

  it('reports network failures clearly', () => {
    expect(toApiError(new HttpErrorResponse({ status: 0 }), 'fallback').message).toContain('Unable to reach the server');
  });

  it('distinguishes authorization failures', () => {
    expect(toApiError(new HttpErrorResponse({ status: 403 }), 'fallback').message).toContain('permission');
  });
});
