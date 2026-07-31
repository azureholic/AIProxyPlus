/** Minimal SSE reader for `data: {...}` event streams (architecture.md §10) — no external EventSource use
 * since EventSource can't POST or set custom headers; this reads the same ReadableStream `fetch` gives us. */
export async function readSseEvents(response: Response, onEvent: (data: string) => void): Promise<void> {
  const reader = response.body?.getReader();
  if (!reader) {
    return;
  }

  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    const events = buffer.split('\n\n');
    buffer = events.pop() ?? '';

    for (const event of events) {
      const dataLine = event.split('\n').find((line) => line.startsWith('data:'));
      if (dataLine) {
        onEvent(dataLine.slice(5).trim());
      }
    }
  }
}
