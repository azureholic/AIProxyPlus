/** Thrown for any non-2xx response; carries the backend's own error body/headers (architecture.md §9). */
export class ApiError extends Error {
  readonly status: number;
  readonly headers: Headers;

  constructor(status: number, message: string, headers: Headers) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.headers = headers;
  }
}

export async function toApiError(response: Response): Promise<ApiError> {
  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('application/json')) {
    try {
      const body = (await response.json()) as { error?: string | { message?: string }; detail?: string; title?: string };
      const message = typeof body.error === 'string'
        ? body.error
        : body.error?.message ?? body.detail ?? body.title;
      if (message) {
        return new ApiError(response.status, message, response.headers);
      }
    } catch {
      // fall through to the generic message below
    }
  }

  return new ApiError(response.status, `Request failed with status ${response.status}`, response.headers);
}

/** Same-origin always — the frontend is served by the same host as the API in every model target. */
let antiforgeryToken: string | undefined;
export const sessionExpiredEvent = 'aiproxy:session-expired';

function isUnsafe(method: string | undefined): boolean {
  return !['GET', 'HEAD', 'OPTIONS'].includes((method ?? 'GET').toUpperCase());
}

async function getAntiforgeryToken(): Promise<string> {
  if (antiforgeryToken) {
    return antiforgeryToken;
  }

  const response = await fetch('/api/v1/auth/antiforgery', { credentials: 'include' });
  if (!response.ok) {
    throw await toApiError(response);
  }

  const body = (await response.json()) as { token: string };
  antiforgeryToken = body.token;
  return antiforgeryToken;
}

export async function submitLogout(): Promise<void> {
  const form = document.createElement('form');
  form.method = 'POST';
  form.action = '/api/v1/auth/logout';

  const token = document.createElement('input');
  token.type = 'hidden';
  token.name = '__RequestVerificationToken';
  token.value = await getAntiforgeryToken();
  form.append(token);

  document.body.append(form);
  form.submit();
}

async function buildInit(init: RequestInit | undefined): Promise<RequestInit> {
  const headers = new Headers(init?.headers);
  if (init?.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  if (isUnsafe(init?.method)) {
    headers.set('X-CSRF-TOKEN', await getAntiforgeryToken());
  }

  return { ...init, headers, credentials: 'include' };
}

async function sessionFetch(path: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(path, await buildInit(init));
  if (response.status === 401) {
    window.dispatchEvent(new Event(sessionExpiredEvent));
  }
  return response;
}

/** Config and Usage requests use the same-origin server session. */
export async function adminFetch(path: string, init?: RequestInit): Promise<Response> {
  return sessionFetch(path, init);
}

/** Playground requests resolve the signed-in user to a Consumer on the server. */
export async function consumerFetch(path: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(path, await buildInit(init));
  if (response.status === 401) {
    const sessionResponse = await fetch('/api/v1/auth/session', { credentials: 'include' });
    if (sessionResponse.status === 401) {
      window.dispatchEvent(new Event(sessionExpiredEvent));
    }
  }
  return response;
}

async function asJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw await toApiError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export async function adminJson<T>(path: string, init?: RequestInit): Promise<T> {
  return asJson<T>(await adminFetch(path, init));
}

export async function consumerJson<T>(path: string, init?: RequestInit): Promise<T> {
  return asJson<T>(await consumerFetch(path, init));
}
