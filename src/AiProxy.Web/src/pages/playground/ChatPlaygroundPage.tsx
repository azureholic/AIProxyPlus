import { useEffect, useRef, useState } from 'react';
import {
  Body1,
  Button,
  Field,
  Select,
  Spinner,
  Switch,
  Textarea,
  makeStyles,
  mergeClasses,
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
    gap: tokens.spacingVerticalM,
    height: '60vh',
    overflowY: 'auto',
    scrollBehavior: 'smooth',
    padding: tokens.spacingHorizontalS,
  },
  messageRow: {
    display: 'flex',
    // Without this, Card's overflow:hidden zeroes out flex's auto min-height, so the column
    // shrinks every bubble to fit instead of overflowing/scrolling (visible as cropped, overlapping text).
    flexShrink: 0,
  },
  messageRowUser: {
    justifyContent: 'flex-end',
  },
  messageRowAssistant: {
    justifyContent: 'flex-start',
  },
  bubble: {
    maxWidth: '75%',
    padding: `${tokens.spacingVerticalSNudge} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusXLarge,
    whiteSpace: 'pre-wrap',
    overflowWrap: 'anywhere',
    lineHeight: tokens.lineHeightBase300,
  },
  bubbleUser: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorNeutralForeground1,
    borderBottomRightRadius: tokens.borderRadiusSmall,
  },
  bubbleAssistant: {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
    borderBottomLeftRadius: tokens.borderRadiusSmall,
  },
  roleLabel: {
    display: 'block',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    marginBottom: tokens.spacingVerticalXXS,
    opacity: 0.75,
  },
  inputRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
  },
});

type ApiMode = 'ChatCompletions' | 'ResponsesApi';

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

interface ResponsesApiUsage {
  input_tokens?: number;
  output_tokens?: number;
  total_tokens?: number;
}

// Non-streaming Responses API shape: `id` is the previous_response_id for the next turn's chaining.
interface ResponsesApiMessage {
  id?: string;
  output?: { type?: string; content?: { type?: string; text?: string }[] }[];
  usage?: ResponsesApiUsage;
}

// Streaming Responses API events carry their own `type` in the JSON payload, so no `event:` line parsing is needed.
interface ResponsesStreamEvent {
  type?: string;
  delta?: string;
  response?: { id?: string; usage?: ResponsesApiUsage };
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

function toTokenUsageFromResponses(usage: ResponsesApiUsage): TokenUsage {
  const promptTokens = usage.input_tokens ?? 0;
  const completionTokens = usage.output_tokens ?? 0;
  return {
    prompt_tokens: promptTokens,
    completion_tokens: completionTokens,
    total_tokens: usage.total_tokens ?? promptTokens + completionTokens,
  };
}

function extractResponsesApiText(output: ResponsesApiMessage['output']): string {
  return output
    ?.filter((item) => item.type === 'message')
    .flatMap((item) => item.content ?? [])
    .filter((block) => block.type === 'output_text')
    .map((block) => block.text ?? '')
    .join('') ?? '';
}

export function ChatPlaygroundPage() {
  const styles = useStyles();
  const [model, setModel] = useState('');
  const [provider, setProvider] = useState<Provider>('OpenAI');
  const [apiMode, setApiMode] = useState<ApiMode>('ChatCompletions');
  const [streaming, setStreaming] = useState(true);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [usage, setUsage] = useState<TokenUsage | null>(null);
  // The Responses API's own "next turn" key (architecture.md §6.4) — not our own session concept.
  const [previousResponseId, setPreviousResponseId] = useState<string | null>(null);
  const latestMessageRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    latestMessageRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages]);

  const isAnthropic = provider === 'Anthropic';
  // Anthropic only supports Messages via ChatCompletion (architecture.md §9) — Responses API isn't offered for it.
  const isResponsesCapable = !isAnthropic;
  const isResponses = apiMode === 'ResponsesApi' && isResponsesCapable;

  const resetConversation = () => {
    setMessages([]);
    setUsage(null);
    setError(null);
    setPreviousResponseId(null);
  };

  const send = async () => {
    if (!model || !input.trim() || busy) {
      return;
    }

    const userMessage = input.trim();
    const history = [...messages, { role: 'user', content: userMessage } satisfies ChatMessage];
    setMessages(history);
    setInput('');
    setBusy(true);
    setError(null);
    setUsage(null);

    try {
      const path = isResponses ? '/openai/v1/responses' : isAnthropic ? '/anthropic/v1/messages' : '/openai/v1/chat/completions';
      const body = isResponses
        ? {
            model,
            input: userMessage,
            stream: streaming,
            ...(previousResponseId ? { previous_response_id: previousResponseId } : {}),
          }
        : {
            model,
            stream: streaming,
            ...(isAnthropic ? { max_tokens: 1024 } : {}),
            messages: history.map((m) => ({ role: m.role, content: m.content })),
          };

      const response = await consumerFetch(path, {
        method: 'POST',
        headers: isAnthropic ? { 'anthropic-version': '2023-06-01' } : undefined,
        body: JSON.stringify(body),
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
          const chunk = JSON.parse(data) as OpenAiChatChunk & AnthropicMessage & ResponsesStreamEvent;
          if (isResponses) {
            if (chunk.type === 'response.output_text.delta' && chunk.delta) {
              assistantText += chunk.delta;
              setMessages([...history, { role: 'assistant', content: assistantText }]);
            } else if (chunk.type === 'response.completed' && chunk.response) {
              setPreviousResponseId(chunk.response.id ?? previousResponseId);
              if (chunk.response.usage) {
                setUsage(toTokenUsageFromResponses(chunk.response.usage));
              }
            }
            return;
          }

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
      } else if (isResponses) {
        const responseBody = (await response.json()) as ResponsesApiMessage;
        setMessages([...history, { role: 'assistant', content: extractResponsesApiText(responseBody.output) }]);
        setPreviousResponseId(responseBody.id ?? previousResponseId);
        if (responseBody.usage) {
          setUsage(toTokenUsageFromResponses(responseBody.usage));
        }
      } else {
        const responseBody = (await response.json()) as OpenAiChatChunk & AnthropicMessage;
        const content = isAnthropic
          ? responseBody.content?.filter((block) => block.type === 'text').map((block) => block.text ?? '').join('') ?? ''
          : responseBody.choices?.[0]?.message?.content ?? '';
        setMessages([...history, { role: 'assistant', content }]);
        if (responseBody.usage) {
          setUsage(isAnthropic
            ? toTokenUsage(responseBody.usage.input_tokens, responseBody.usage.output_tokens)
            : responseBody.usage as TokenUsage);
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
            const nextProvider = getPreferredProvider(selectedModel);
            setProvider(nextProvider);
            // previous_response_id only resolves on the endpoint that created it (architecture.md §6.4/§9) —
            // it can never be valid for a different model/provider.
            setPreviousResponseId(null);
            if (nextProvider === 'Anthropic') {
              setApiMode('ChatCompletions');
            }
          }}
          capability="Chat"
        />
        {isResponsesCapable && (
          <Field label="API">
            <Select
              value={apiMode}
              onChange={(_, data) => {
                setApiMode(data.value as ApiMode);
                resetConversation();
              }}
            >
              <option value="ChatCompletions">Chat Completions</option>
              <option value="ResponsesApi">Responses API</option>
            </Select>
          </Field>
        )}
        <Switch checked={streaming} onChange={(_, data) => setStreaming(data.checked)} label="Streaming" />
      </div>

      <ErrorBanner error={error} />

      {usage && (
        <Body1>
          Tokens — prompt: {usage.prompt_tokens}, completion: {usage.completion_tokens}, total: {usage.total_tokens}
        </Body1>
      )}

      <div className={styles.messages}>
        {messages.map((message, index) => {
          const isUser = message.role === 'user';
          return (
            <div
              key={index}
              className={mergeClasses(styles.messageRow, isUser ? styles.messageRowUser : styles.messageRowAssistant)}
              ref={index === messages.length - 1 ? latestMessageRef : undefined}
            >
              <div className={mergeClasses(styles.bubble, isUser ? styles.bubbleUser : styles.bubbleAssistant)}>
                <span className={styles.roleLabel}>{isUser ? 'You' : 'Assistant'}</span>
                {message.content}
              </div>
            </div>
          );
        })}
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
