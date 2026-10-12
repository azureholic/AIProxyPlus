import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiRoutesPage } from '../pages/config/ApiRoutesPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('ApiRoutesPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows built-in routes and creates a reusable provider route', async () => {
    const route = {
      id: 'built-in-id',
      name: 'Third Party OpenAI',
      provider: 'OpenAI',
      pathPrefix: '/openai/v1',
      isBuiltIn: true,
    };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/v1/auth/antiforgery') return jsonResponse({ token: 'csrf-token' });
      if (init?.method === 'POST') return jsonResponse({ id: 'new-id', ...JSON.parse(String(init.body)), isBuiltIn: false }, 201);
      return jsonResponse([route]);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ApiRoutesPage />);

    expect(await screen.findByText('Third Party OpenAI')).toBeInTheDocument();
    expect(screen.getByText('Built in')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'New API route' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Custom MAI' } });
    await user.selectOptions(screen.getByRole('combobox', { name: 'Provider' }), 'MicrosoftAI');
    fireEvent.change(screen.getByLabelText('Path prefix'), { target: { value: '/custom/mai' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create', hidden: true }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/config/api-routes',
      expect.objectContaining({ method: 'POST' }),
    ));
    const createCall = fetchMock.mock.calls.find(([url, init]) => url === '/api/v1/config/api-routes' && init?.method === 'POST');
    expect(JSON.parse(String(createCall?.[1]?.body))).toEqual({
      name: 'Custom MAI',
      provider: 'MicrosoftAI',
      pathPrefix: '/custom/mai',
    });
  });
});