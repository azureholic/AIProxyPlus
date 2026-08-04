import { useState } from 'react';
import {
  Body1,
  Button,
  Card,
  Spinner,
  Switch,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { SendRegular } from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { ModelPicker } from '../../components/ModelPicker';
import { consumerFetch, toApiError } from '../../api/client';
import { readSseEvents } from '../../api/sse';
import type { Model, Provider } from '../../api/types';

const useStyles = makeStyles({
  controls: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalM,
  },
  messages: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    maxHeight: '480px',
    overflowY: 'auto',
  },
  message: {
    padding: tokens.spacingHorizontalM,
    whiteSpace: 'pre-wrap',
  },
  inputRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
  },
});

interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
}

interface OpenAiChatChunk {
  choices?: { delta?: { content?: string }; message?: { content?: string } }[];
  usage?: { prompt_tokens: number; completion_tokens: number; total_tokens: number };
}

interface AnthropicMessage {
  type?: string;
  content?: { type: string; text?: string }[];
  delta?: { text?: string };
  usage?: { input_tokens?: number; output_tokens?: number };
  message?: { usage?: { input_tokens?: number; output_tokens?: number } };
}

interface TokenUsage {
  prompt_tokens: number;
  completion_tokens: number;
  total_tokens: number;
}

function getPreferredProvider(model: Model | undefined): Provider {
  const enabledEndpoints = model?.endpoints.filter((endpoint) => endpoint.enabled) ?? [];
  const minimumPriority = Math.min(...enabledEndpoints.map((endpoint) => endpoint.priority));
  return enabledEndpoints.find((endpoint) => endpoint.priority === minimumPriority)?.provider ?? 'OpenAI';
}

function toTokenUsage(inputTokens = 0, outputTokens = 0): TokenUsage {
  return {
    prompt_tokens: inputTokens,
    completion_tokens: outputTokens,
    total_tokens: inputTokens + outputTokens,
  };
}

export function ChatPlaygroundPage() {
  const styles = useStyles();
  const [model, setModel] = useState('');
  const [provider, setProvider] = useState<Provider>('OpenAI');
  const [streaming, setStreaming] = useState(true);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [usage, setUsage] = useState<TokenUsage | null>(null);

  const send = async () => {
    if (!model || !input.trim() || busy) {
      return;
    }

    const history = [...messages, { role: 'user', content: input.trim() } satisfies ChatMessage];
    setMessages(history);
    setInput('');
    setBusy(true);
    setError(null);
    setUsage(null);

    try {
      const isAnthropic = provider === 'Anthropic';
      const response = await consumerFetch(isAnthropic ? '/anthropic/v1/messages' : '/openai/v1/chat/completions', {
        method: 'POST',
        headers: isAnthropic ? { 'anthropic-version': '2023-06-01' } : undefined,
        body: JSON.stringify({
          model: model,
          stream: streaming,
          ...(isAnthropic ? { max_tokens: 1024 } : {}),
          messages: history.map((m) => ({ role: m.role, content: m.content })),
        }),
      });

      if (!response.ok) {
        throw await toApiError(response);
      }

      if (streaming) {
        let assistantText = '';
        let anthropicInputTokens = 0;
        let anthropicOutputTokens = 0;
        setMessages([...history, { role: 'assistant', content: '' }]);
        await readSseEvents(response, (data) => {
          if (data === '[DONE]') {
            return;
          }
          const chunk = JSON.parse(data) as OpenAiChatChunk & AnthropicMessage;
          const delta = isAnthropic ? chunk.delta?.text : chunk.choices?.[0]?.delta?.content;
          if (delta) {
            assistantText += delta;
            setMessages([...history, { role: 'assistant', content: assistantText }]);
          }
          if (isAnthropic) {
            anthropicInputTokens = chunk.message?.usage?.input_tokens ?? anthropicInputTokens;
            anthropicOutputTokens = chunk.usage?.output_tokens ?? anthropicOutputTokens;
            if (anthropicInputTokens || chunk.usage?.output_tokens !== undefined) {
              setUsage(toTokenUsage(anthropicInputTokens, anthropicOutputTokens));
            }
          } else if (chunk.usage) {
            setUsage(chunk.usage);
          }
        });
      } else {
        const body = (await response.json()) as OpenAiChatChunk & AnthropicMessage;
        const content = isAnthropic
          ? body.content?.filter((block) => block.type === 'text').map((block) => block.text ?? '').join('') ?? ''
          : body.choices?.[0]?.message?.content ?? '';
        setMessages([...history, { role: 'assistant', content }]);
        if (body.usage) {
          setUsage(isAnthropic
            ? toTokenUsage(body.usage.input_tokens, body.usage.output_tokens)
            : body.usage as TokenUsage);
        }
      }
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <PageContainer>
      <PageHeader title="Chat Playground" />

      <div className={styles.controls}>
        <ModelPicker
          value={model}
          onChange={(name, selectedModel) => {
            setModel(name);
            setProvider(getPreferredProvider(selectedModel));
          }}
          capability="Chat"
        />
        <Switch checked={streaming} onChange={(_, data) => setStreaming(data.checked)} label="Streaming" />
      </div>

      <ErrorBanner error={error} />

      {usage && (
        <Body1>
          Tokens — prompt: {usage.prompt_tokens}, completion: {usage.completion_tokens}, total: {usage.total_tokens}
        </Body1>
      )}

      <div className={styles.messages}>
        {messages.map((message, index) => (
          <Card key={index} className={styles.message}>
            <strong>{message.role === 'user' ? 'You' : 'Assistant'}:</strong> {message.content}
          </Card>
        ))}
      </div>

      <div className={styles.inputRow}>
        <Textarea
          style={{ flex: 1 }}
          value={input}
          placeholder={model ? 'Type a message…' : 'Select a model first'}
          disabled={!model}
          onChange={(_, data) => setInput(data.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && !event.shiftKey) {
              event.preventDefault();
              send();
            }
          }}
        />
        <Button
          appearance="primary"
          icon={busy ? <Spinner size="tiny" /> : <SendRegular />}
          disabled={!model || !input.trim() || busy}
          onClick={send}
        >
          Send
        </Button>
      </div>
    </PageContainer>
  );
}
