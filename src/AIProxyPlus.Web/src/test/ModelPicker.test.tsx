import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ModelPicker } from '../components/ModelPicker';

function model(name: string, capabilities: string[]) {
  return {
    id: name,
    name,
    strategy: 'Priority',
    capabilities,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    endpoints: [],
  };
}

describe('ModelPicker', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows only models matching the requested capability and clears a stale selection', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify([
      model('chat-model', ['Chat']),
      model('speech-model', ['SpeechToText']),
    ]), { status: 200, headers: { 'Content-Type': 'application/json' } })));
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(<ModelPicker capability="SpeechToText" value="chat-model" onChange={onChange} />);

    await waitFor(() => expect(onChange).toHaveBeenCalledWith(''));
    await user.click(screen.getByPlaceholderText('Select a model…'));
    expect(await screen.findByRole('option', { name: 'speech-model' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'chat-model' })).not.toBeInTheDocument();
  });

  it('explains when no model supports the capability', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('[]', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })));

    render(<ModelPicker capability="TextToSpeech" value="" onChange={vi.fn()} />);

    expect(await screen.findByText('No configured models support this capability.')).toBeInTheDocument();
  });
});