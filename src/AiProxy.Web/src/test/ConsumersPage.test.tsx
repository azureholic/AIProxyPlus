import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConsumersPage } from '../pages/config/ConsumersPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('ConsumersPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('creates a consumer and reveals the plaintext API key exactly once', async () => {
    const consumer = { id: '1', name: 'my-team', enabled: true, createdAt: new Date().toISOString(), revokedAt: null };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return jsonResponse({ consumer, apiKey: 'sk-plaintext-secret' }, 201);
      }
      return jsonResponse(url.includes('rotate') ? [] : []); // initial + reload GET
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ConsumersPage />);

    await user.click(await screen.findByRole('button', { name: 'New consumer' }));
    // fireEvent.change, not user.type: FluentUI's Field/Input inside a Dialog breaks userEvent's
    // per-keystroke simulation (getByRole stops finding sibling elements afterward) — see repo memory.
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'my-team' } });
    expect(await screen.findByRole('button', { name: 'Create' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Create' }));

    // The revealed key is an <Input readOnly value=...>, not text content — assert on display value.
    expect(await screen.findByDisplayValue('sk-plaintext-secret')).toBeInTheDocument();
    expect(screen.getByText('You will not see this value again — copy it now.')).toBeInTheDocument();
  });

  it('revokes a consumer only after the confirm dialog is accepted', async () => {
    const consumer = { id: '1', name: 'my-team', enabled: true, createdAt: new Date().toISOString(), revokedAt: null };
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'DELETE') {
        return jsonResponse({ ...consumer, revokedAt: new Date().toISOString() });
      }
      return jsonResponse([consumer]);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ConsumersPage />);

    await screen.findByText('my-team');
    expect(fetchMock).not.toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ method: 'DELETE' }));

    await user.click(screen.getByRole('button', { name: 'Revoke my-team' }));
    await user.click(await screen.findByRole('button', { name: 'Revoke' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/config/consumers/my-team'),
        expect.objectContaining({ method: 'DELETE' }),
      ),
    );
  });
});
