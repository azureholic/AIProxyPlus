import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ChatPlaygroundPage } from '../pages/playground/ChatPlaygroundPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function sseResponse(events: string[]): Response {
  const encoder = new TextEncoder();
  const stream = new ReadableStream({
    start(controller) {
      for (const event of events) {
        controller.enqueue(encoder.encode(`data: ${event}\n\n`));
      }
      controller.close();
    },
  });
  return new Response(stream, { status: 200, headers: { 'Content-Type': 'text/event-stream' } });
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
      capabilities: ['Chat'],
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

  it('uses the native Messages contract for an Anthropic model', async () => {
    const model = {
      id: '2',
      name: 'claude-haiku-4-5',
      strategy: 'Priority',
      capabilities: ['Chat'],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [{ provider: 'Anthropic', enabled: true, priority: 1 }],
    };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return jsonResponse({
          content: [{ type: 'text', text: 'Native Anthropic reply' }],
          usage: { input_tokens: 6, output_tokens: 4 },
        });
      }
      return jsonResponse(url.includes('models') ? [model] : []);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ChatPlaygroundPage />);
    await user.click(screen.getByPlaceholderText('Select a model…'));
    await user.click(await screen.findByRole('option', { name: 'claude-haiku-4-5' }));
    await user.click(screen.getByLabelText('Streaming'));
    await user.type(screen.getByPlaceholderText('Type a message…'), 'hello');
    await user.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText(/Native Anthropic reply/)).toBeInTheDocument();
    expect(await screen.findByText(/total: 10/)).toBeInTheDocument();
    const [url, init] = fetchMock.mock.calls.find(([, request]) => request?.method === 'POST')!;
    expect(url).toBe('/anthropic/v1/messages');
    expect(new Headers(init?.headers).get('anthropic-version')).toBe('2023-06-01');
    expect(JSON.parse(init?.body as string)).toMatchObject({
      model: 'claude-haiku-4-5',
      max_tokens: 1024,
      messages: [{ role: 'user', content: 'hello' }],
    });
    expect(fetchMock.mock.calls.filter(([requestUrl]) => requestUrl === '/api/v1/config/models')).toHaveLength(1);
  });

  it('hides the Responses API selector for an Anthropic model', async () => {
    const model = {
      id: '3',
      name: 'claude-haiku-4-5',
      strategy: 'Priority',
      capabilities: ['Chat'],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [{ provider: 'Anthropic', enabled: true, priority: 1 }],
    };
    vi.stubGlobal('fetch', vi.fn(async (url: string) => jsonResponse(url.includes('models') ? [model] : [])));

    const user = userEvent.setup();
    render(<ChatPlaygroundPage />);
    await user.click(screen.getByPlaceholderText('Select a model…'));
    await user.click(await screen.findByRole('option', { name: 'claude-haiku-4-5' }));

    expect(screen.queryByLabelText('API')).not.toBeInTheDocument();
  });

  it('hides the Responses API selector before any model is selected', async () => {
    const model = {
      id: '5',
      name: 'gpt-4o',
      strategy: 'Priority',
      capabilities: ['Chat'],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [],
    };
    vi.stubGlobal('fetch', vi.fn(async (url: string) => jsonResponse(url.includes('models') ? [model] : [])));

    render(<ChatPlaygroundPage />);

    expect(await screen.findByPlaceholderText('Select a model…')).toBeInTheDocument();
    expect(screen.queryByLabelText('API')).not.toBeInTheDocument();
  });

  it('sends a Responses API request without streaming and chains previous_response_id across turns', async () => {
    const model = {
      id: '4',
      name: 'gpt-5-test',
      strategy: 'Priority',
      capabilities: ['Chat'],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [],
    };
    let callCount = 0;
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        callCount += 1;
        return jsonResponse({
          id: `resp_${callCount}`,
          output: [{ type: 'message', content: [{ type: 'output_text', text: `reply ${callCount}` }] }],
          usage: { input_tokens: 5, output_tokens: 3, total_tokens: 8 },
        });
      }
      return jsonResponse(url.includes('models') ? [model] : []);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ChatPlaygroundPage />);
    await user.click(screen.getByPlaceholderText('Select a model…'));
    await user.click(await screen.findByRole('option', { name: 'gpt-5-test' }));
    await user.click(screen.getByLabelText('Streaming'));
    await user.selectOptions(screen.getByLabelText('API'), 'Responses API');

    fireEvent.change(screen.getByPlaceholderText('Type a message…'), { target: { value: 'first' } });
    await user.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText(/reply 1/)).toBeInTheDocument();
    const [firstUrl, firstInit] = fetchMock.mock.calls.find(([, request]) => request?.method === 'POST')!;
    expect(firstUrl).toBe('/openai/v1/responses');
    const firstBody = JSON.parse(firstInit?.body as string);
    expect(firstBody).toMatchObject({ model: 'gpt-5-test', input: 'first' });
    expect(firstBody).not.toHaveProperty('previous_response_id');

    fireEvent.change(screen.getByPlaceholderText('Type a message…'), { target: { value: 'second' } });
    await user.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText(/reply 2/)).toBeInTheDocument();
    const postCalls = fetchMock.mock.calls.filter(([, request]) => request?.method === 'POST');
    const secondBody = JSON.parse(postCalls[1][1]?.body as string);
    expect(secondBody).toMatchObject({ model: 'gpt-5-test', input: 'second', previous_response_id: 'resp_1' });
  });

  it('streams a Responses API reply and captures the completed usage + response id', async () => {
    const model = {
      id: '5',
      name: 'gpt-5-stream',
      strategy: 'Priority',
      capabilities: ['Chat'],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [],
    };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return sseResponse([
          JSON.stringify({ type: 'response.output_text.delta', delta: 'Hel' }),
          JSON.stringify({ type: 'response.output_text.delta', delta: 'lo' }),
          JSON.stringify({
            type: 'response.completed',
            response: { id: 'resp_stream_1', usage: { input_tokens: 2, output_tokens: 2, total_tokens: 4 } },
          }),
        ]);
      }
      return jsonResponse(url.includes('models') ? [model] : []);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ChatPlaygroundPage />);
    await user.click(screen.getByPlaceholderText('Select a model…'));
    await user.click(await screen.findByRole('option', { name: 'gpt-5-stream' }));
    await user.selectOptions(screen.getByLabelText('API'), 'Responses API');

    fireEvent.change(screen.getByPlaceholderText('Type a message…'), { target: { value: 'hi' } });
    await user.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText('Hello')).toBeInTheDocument();
    expect(await screen.findByText(/total: 4/)).toBeInTheDocument();
  });

  it('streams a Chat Completions reply, requests the usage chunk, and shows token usage', async () => {
    const model = {
      id: '6',
      name: 'gpt-chat-stream',
      strategy: 'Priority',
      capabilities: ['Chat'],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      endpoints: [],
    };
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return sseResponse([
          JSON.stringify({ choices: [{ delta: { content: 'Hi!' } }] }),
          JSON.stringify({ choices: [], usage: { prompt_tokens: 7, completion_tokens: 13, total_tokens: 20 } }),
          '[DONE]',
        ]);
      }
      return jsonResponse(url.includes('models') ? [model] : []);
    });
    vi.stubGlobal('fetch', fetchMock);

    const user = userEvent.setup();
    render(<ChatPlaygroundPage />);
    await user.click(screen.getByPlaceholderText('Select a model…'));
    await user.click(await screen.findByRole('option', { name: 'gpt-chat-stream' }));

    fireEvent.change(screen.getByPlaceholderText('Type a message…'), { target: { value: 'hi' } });
    await user.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText(/total: 20/)).toBeInTheDocument();
    const postCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!;
    expect(JSON.parse(postCall[1]!.body as string)).toMatchObject({ stream: true, stream_options: { include_usage: true } });
  });
});
