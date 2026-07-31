import type { ReactNode } from 'react';
import { MessageBar, MessageBarBody, MessageBarTitle } from '@fluentui/react-components';
import { ApiError } from '../api/client';

/** Surfaces the backend's actual error (status + message, e.g. a 429's Retry-After) — see Iteration 2 Phase 5. */
export function ErrorBanner({ error }: { error: unknown }) {
  if (!error) {
    return null;
  }

  const message = error instanceof ApiError ? error.message : error instanceof Error ? error.message : String(error);
  const retryAfter = error instanceof ApiError ? error.headers.get('Retry-After') : null;

  return (
    <MessageBar intent="error">
      <MessageBarBody>
        <MessageBarTitle>{error instanceof ApiError ? `Error ${error.status}` : 'Error'}</MessageBarTitle>
        {message}
        {retryAfter && ` (retry after ${retryAfter}s)`}
      </MessageBarBody>
    </MessageBar>
  );
}

export function InfoBanner({ children }: { children: ReactNode }) {
  return (
    <MessageBar intent="info">
      <MessageBarBody>{children}</MessageBarBody>
    </MessageBar>
  );
}
