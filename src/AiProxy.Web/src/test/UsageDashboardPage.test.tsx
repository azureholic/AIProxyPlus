import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UsageDashboardPage } from '../pages/dashboard/UsageDashboardPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('UsageDashboardPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('loads and renders usage records in the table', async () => {
    const record = {
      id: '1',
      timestamp: new Date().toISOString(),
      modelDeploymentName: 'gpt-4o',
      consumerId: 'c1',
      consumerName: 'my-team',
      endpointId: 'e1',
      apiFamily: 'OpenAI',
      operationType: 'ChatCompletion',
      requestId: 'r1',
      sessionId: null,
      promptTokens: 10,
      completionTokens: 5,
      totalTokens: 15,
      usageMetrics: null,
      streaming: false,
      httpStatusCode: 200,
      durationMs: 120,
    };
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse({ items: [record], totalCount: 1, page: 1, pageSize: 25 })),
    );

    render(<UsageDashboardPage />);

    expect(await screen.findByText('gpt-4o')).toBeInTheDocument();
    expect(screen.getByText('my-team')).toBeInTheDocument();
    expect(screen.getByText('15')).toBeInTheDocument();
  });

  it('shows an empty state when there are no matching records', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ items: [], totalCount: 0, page: 1, pageSize: 25 })));

    render(<UsageDashboardPage />);

    expect(await screen.findByText('No usage records match these filters.')).toBeInTheDocument();
  });
});
