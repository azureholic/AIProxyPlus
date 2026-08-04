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
    const consumer = { id: '1', name: 'my-team', authenticationType: 'apiKey', enabled: true, createdAt: new Date().toISOString(), revokedAt: null };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return jsonResponse({ consumer, apiKey1: 'sk-primary', apiKey2: 'sk-secondary' }, 201);
      }
      return jsonResponse(url.includes('rotate') ? [] : []); // initial + reload GET
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ConsumersPage />);

    await user.click(await screen.findByRole('button', { name: 'New consumer' }));
    // fireEvent, not userEvent, for the input+click pair right after: FluentUI's Field/Input inside a Dialog
    // interacts badly with userEvent's async pointer/keyboard simulation immediately after a state update
    // (getByRole intermittently stops finding sibling elements) — see repo memory.
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'my-team' } });
    const createButton = await screen.findByRole('button', { name: 'Create' });
    fireEvent.click(createButton);

    expect(await screen.findByLabelText('Key 1')).toHaveValue('sk-primary');
    expect(screen.getByLabelText('Key 2')).toHaveValue('sk-secondary');
    expect(screen.getByLabelText('Copy Key 1')).toBeInTheDocument();
    expect(screen.getByLabelText('Copy Key 2')).toBeInTheDocument();
    expect(screen.getByText('You will not see this value again — copy it now.')).toBeInTheDocument();
  });

  it('revokes a consumer only after the confirm dialog is accepted', async () => {
    const consumer = { id: '1', name: 'my-team', authenticationType: 'apiKey', enabled: true, createdAt: new Date().toISOString(), revokedAt: null };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'DELETE') {
        return jsonResponse({ ...consumer, revokedAt: new Date().toISOString() });
      }
      return jsonResponse(url.includes('/models') ? [] : [consumer]);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ConsumersPage />);

    await screen.findByText('my-team');
    expect(fetchMock).not.toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ method: 'DELETE' }));
    expect(screen.queryByRole('button', { name: 'Delete permanently my-team' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Revoke my-team' }));
    await user.click(await screen.findByRole('button', { name: 'Revoke' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/config/consumers/my-team'),
        expect.objectContaining({ method: 'DELETE' }),
      ),
    );
  });

  it('disables an enabled consumer without revoking it', async () => {
    const consumer = { id: '1', name: 'my-team', authenticationType: 'apiKey', enabled: true, createdAt: new Date().toISOString(), revokedAt: null };
    const disabledConsumer = { ...consumer, enabled: false };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT' && url.endsWith('/disable')) {
        return jsonResponse(disabledConsumer);
      }
      return jsonResponse(url.includes('/models') ? [] : [consumer]);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ConsumersPage />);

    await user.click(await screen.findByRole('button', { name: 'Disable my-team' }));

    await waitFor(() => expect(screen.getByText('Disabled')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Enable my-team' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Revoke my-team' })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/config/consumers/my-team/disable'),
      expect.objectContaining({ method: 'PUT' }),
    );
  });

  it('enables a disabled consumer', async () => {
    const consumer = { id: '1', name: 'my-team', authenticationType: 'apiKey', enabled: false, createdAt: new Date().toISOString(), revokedAt: null };
    const enabledConsumer = { ...consumer, enabled: true };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PUT' && url.endsWith('/enable')) {
        return jsonResponse(enabledConsumer);
      }
      return jsonResponse(url.includes('/models') ? [] : [consumer]);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ConsumersPage />);

    await user.click(await screen.findByRole('button', { name: 'Enable my-team' }));

    await waitFor(() => expect(screen.getByText('Enabled')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Disable my-team' })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/config/consumers/my-team/enable'),
      expect.objectContaining({ method: 'PUT' }),
    );
  });

  it('permanently deletes a revoked consumer only after confirmation', async () => {
    const consumer = {
      id: '1',
      name: 'my-team',
      authenticationType: 'apiKey',
      enabled: false,
      createdAt: new Date().toISOString(),
      revokedAt: new Date().toISOString(),
    };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'DELETE' && url.endsWith('/permanent')) {
        return new Response(null, { status: 204 });
      }
      return jsonResponse(url.includes('/models') ? [] : [consumer]);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ConsumersPage />);

    await user.click(await screen.findByRole('button', { name: 'Delete permanently my-team' }));
    await user.click(await screen.findByRole('button', { name: 'Delete permanently' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/config/consumers/my-team/permanent'),
        expect.objectContaining({ method: 'DELETE' }),
      ),
    );
    expect(screen.getByRole('button', { name: 'Rotate key 1 for my-team' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Configure model access for my-team' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Enable my-team' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revoke my-team' })).not.toBeInTheDocument();
  });

  it('shows OIDC consumers without an API key rotation action', async () => {
    const consumer = {
      id: '1',
      name: 'proxy-admin',
      authenticationType: 'oidc',
      enabled: true,
      createdAt: new Date().toISOString(),
      revokedAt: null,
    };
    vi.stubGlobal('fetch', vi.fn(async (url: string) => jsonResponse(url.includes('/models') ? [] : [consumer])));

    render(<ConsumersPage />);

    expect(await screen.findByText('OIDC')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Rotate key 1 for proxy-admin' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Rotate key 2 for proxy-admin' })).toBeDisabled();
  });
});
