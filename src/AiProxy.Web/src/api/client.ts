import { getAdminApiKey, getConsumerApiKey } from './settingsStore';

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

async function toApiError(response: Response): Promise<ApiError> {
  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('application/json')) {
    try {
      const body = (await response.json()) as { error?: string };
      if (body.error) {
        return new ApiError(response.status, body.error, response.headers);
      }
    } catch {
      // fall through to the generic message below
    }
  }

  return new ApiError(response.status, `Request failed with status ${response.status}`, response.headers);
}

/** Same-origin always — the frontend is served by the same host as the API in every deployment target. */
function buildInit(init: RequestInit | undefined, headerName: string, headerValue: string): RequestInit {
  const headers = new Headers(init?.headers);
  headers.set(headerName, headerValue);
  if (init?.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  return { ...init, headers };
}

/** Low-level fetch attaching the admin key (Config + Usage routes) — returns the raw Response. */
export async function adminFetch(path: string, init?: RequestInit): Promise<Response> {
  return fetch(path, buildInit(init, 'X-Admin-Api-Key', getAdminApiKey()));
}

/** Low-level fetch attaching the consumer key (ingress/playground routes) — returns the raw Response. */
export async function consumerFetch(path: string, init?: RequestInit): Promise<Response> {
  return fetch(path, buildInit(init, 'X-Api-Key', getConsumerApiKey()));
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
