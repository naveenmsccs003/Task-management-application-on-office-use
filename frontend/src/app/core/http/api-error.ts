import { HttpErrorResponse } from '@angular/common/http';

export interface ApiError {
  /** Safe, user-facing summary. Never contains exception details. */
  message: string;
  /** Server validation messages keyed by camelCase field name ("" for form-level errors). */
  fieldErrors: Record<string, string[]>;
}

interface ProblemDetailsBody {
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Converts an HTTP failure into display text, falling back to a generic message. */
export function toApiError(error: unknown, fallback: string): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { message: fallback, fieldErrors: {} };
  }
  if (error.status === 0) {
    return { message: 'Unable to reach the server. Check your connection and try again.', fieldErrors: {} };
  }

  const body = (typeof error.error === 'object' && error.error !== null ? error.error : {}) as ProblemDetailsBody;
  const fieldErrors: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(body.errors ?? {})) {
    const field = key.length > 0 ? key[0].toLowerCase() + key.slice(1) : key;
    fieldErrors[field] = messages;
  }

  switch (error.status) {
    case 400:
      return { message: body.detail ?? fieldErrors['']?.[0] ?? 'Please correct the highlighted fields.', fieldErrors };
    case 401:
    case 429:
      return { message: body.detail ?? fallback, fieldErrors };
    case 403:
      return { message: 'You do not have permission to perform this action.', fieldErrors };
    default:
      return { message: fallback, fieldErrors };
  }
}
