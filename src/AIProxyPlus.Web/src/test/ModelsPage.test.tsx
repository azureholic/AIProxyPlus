import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ModelsPage } from '../pages/config/ModelsPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('ModelsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('does not delete until the confirm dialog is accepted', async () => {
    const model = {
      id: '1',
      name: 'gpt-4o',
      strategy: 'Priority',
      capabilities: ['Chat'],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [],
    };
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse([model]);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <ModelsPage />
      </MemoryRouter>,
    );

    await screen.findByText('gpt-4o');
    await user.click(screen.getByRole('button', { name: 'Delete gpt-4o' }));

    // Dialog open, but nothing sent yet.
    expect(fetchMock).not.toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ method: 'DELETE' }));

    await user.click(await screen.findByRole('button', { name: 'Delete' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/config/models/gpt-4o'),
        expect.objectContaining({ method: 'DELETE' }),
      ),
    );
  });
});
