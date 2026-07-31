import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import App from '../App';

describe('App shell + navigation', () => {
  it('renders the app title and lands on the default (Model Deployments) route', async () => {
    render(<App />);

    expect(await screen.findByText('AI Proxy')).toBeInTheDocument();
    expect(await screen.findByText(/Model deployment \+ endpoint management/)).toBeInTheDocument();
  });

  it('navigates to each top-level page via the sidebar', async () => {
    const user = userEvent.setup();
    render(<App />);

    for (const [navLabel, pageBodyText] of [
      ['Consumers', /Consumer API key management/],
      ['Chat', /Streaming\/non-streaming chat testing/],
      ['Images', /Image generation testing/],
      ['Voice', /Realtime \(voice\) WebSocket connectivity test/],
      ['Usage', /Usage filtering, table, and charts/],
      ['Settings', /placeholder for real authentication/],
    ] as const) {
      await user.click(screen.getByText(navLabel));
      expect(await screen.findByText(pageBodyText)).toBeInTheDocument();
    }
  });
});
