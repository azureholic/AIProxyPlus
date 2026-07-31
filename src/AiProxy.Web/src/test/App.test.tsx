import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';

describe('App shell + navigation', () => {
  beforeEach(() => {
    // Model Deployments/Consumers fetch on mount — stub fetch so this suite tests navigation, not data loading.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([]), { status: 200 })));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the app title and lands on the default (Model Deployments) route', async () => {
    render(<App />);

    expect(await screen.findByText('AI Proxy')).toBeInTheDocument();
    expect(await screen.findByText('New deployment')).toBeInTheDocument();
  });

  it('navigates to each top-level page via the sidebar', async () => {
    const user = userEvent.setup();
    render(<App />);

    for (const [navLabel, pageBodyText] of [
      ['Consumers', 'New consumer'],
      ['Chat', 'Send'],
      ['Images', 'Generate'],
      ['Voice', 'Connect'],
      ['Usage', /Usage filtering, table, and charts/],
      ['Settings', /placeholder for real authentication/],
    ] as const) {
      await user.click(screen.getByText(navLabel));
      expect(await screen.findByText(pageBodyText)).toBeInTheDocument();
    }
  });
});
