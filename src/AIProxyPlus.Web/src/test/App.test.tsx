import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';

describe('App shell + navigation', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((input: RequestInfo | URL) => {
      const path = input.toString();
      const body = path === '/api/v1/auth/session'
        ? { authenticated: true, username: 'proxy-admin', issuer: 'http://localhost:8080/realms/aiproxyplus' }
        : [];
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200 }));
    }));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the app title and lands on the default (Models) route', async () => {
    render(<App />);

    expect(await screen.findByText('AIProxyPlus')).toBeInTheDocument();
    expect(await screen.findByText('New model')).toBeInTheDocument();
  });

  it('centers sign-in around the configured identity provider when unauthenticated', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const path = input.toString();
      if (path === '/api/v1/auth/session') {
        return Promise.resolve(new Response(null, { status: 401 }));
      }

      return Promise.resolve(new Response(JSON.stringify({ displayName: 'Keycloak' }), { status: 200 }));
    });

    render(<App />);

    expect(await screen.findByRole('button', { name: 'Sign in with Keycloak' })).toBeInTheDocument();
    expect(screen.getByText('Administrator access is provided by Keycloak.')).toBeInTheDocument();
    expect(screen.getByRole('main')).toHaveStyle({ minHeight: '100vh', justifyContent: 'center' });
  });

  it('navigates to each top-level page via the sidebar', async () => {
    const user = userEvent.setup();
    render(<App />);

    for (const [navLabel, pageBodyText] of [
      ['Endpoints', 'New endpoint'],
      ['API Routes', 'New API route'],
      ['Consumers', 'New consumer'],
      ['Chat', 'Send'],
      ['Images', 'Generate'],
      ['Realtime Voice', 'Connect'],
      ['Speech', 'Speech to text'],
      ['Usage', 'Apply filters'],
      ['Settings', 'proxy-admin'],
    ] as const) {
      await user.click(await screen.findByText(navLabel));
      expect(await screen.findByText(pageBodyText)).toBeInTheDocument();
    }
  });
});
