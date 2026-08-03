import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ChatPlaygroundPage } from '../pages/playground/ChatPlaygroundPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('ChatPlaygroundPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends a non-streaming chat message and renders the reply + token usage', async () => {
    const model = {
      id: '1',
      name: 'gpt-4o',
      strategy: 'Priority',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [],
    };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return jsonResponse({
          choices: [{ message: { role: 'assistant', content: 'Hi there!' } }],
          usage: { prompt_tokens: 5, completion_tokens: 3, total_tokens: 8 },
        });
      }
      return jsonResponse(url.includes('models') ? [model] : []);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ChatPlaygroundPage />);

    // Open the model combobox and pick the only option.
    await user.click(screen.getByPlaceholderText('Select a model…'));
    await user.click(await screen.findByRole('option', { name: 'gpt-4o' }));

    // Turn off streaming so this test asserts the simpler JSON-response code path.
    await user.click(screen.getByLabelText('Streaming'));

    fireEvent.change(screen.getByPlaceholderText('Type a message…'), { target: { value: 'hello' } });
    await user.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText(/Hi there!/)).toBeInTheDocument();
    expect(await screen.findByText(/total: 8/)).toBeInTheDocument();
  });
});
