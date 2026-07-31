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
import { DeploymentPicker } from '../../components/DeploymentPicker';
import { consumerFetch } from '../../api/client';
import { readSseEvents } from '../../api/sse';

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

export function ChatPlaygroundPage() {
  const styles = useStyles();
  const [deployment, setDeployment] = useState('');
  const [streaming, setStreaming] = useState(true);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [usage, setUsage] = useState<OpenAiChatChunk['usage'] | null>(null);

  const send = async () => {
    if (!deployment || !input.trim() || busy) {
      return;
    }

    const history = [...messages, { role: 'user', content: input.trim() } satisfies ChatMessage];
    setMessages(history);
    setInput('');
    setBusy(true);
    setError(null);
    setUsage(null);

    try {
      const response = await consumerFetch('/openai/v1/chat/completions', {
        method: 'POST',
        body: JSON.stringify({
          model: deployment,
          stream: streaming,
          messages: history.map((m) => ({ role: m.role, content: m.content })),
        }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as { error?: string };
        throw new Error(body.error ?? `Request failed with status ${response.status}`);
      }

      if (streaming) {
        let assistantText = '';
        setMessages([...history, { role: 'assistant', content: '' }]);
        await readSseEvents(response, (data) => {
          if (data === '[DONE]') {
            return;
          }
          const chunk = JSON.parse(data) as OpenAiChatChunk;
          const delta = chunk.choices?.[0]?.delta?.content;
          if (delta) {
            assistantText += delta;
            setMessages([...history, { role: 'assistant', content: assistantText }]);
          }
          if (chunk.usage) {
            setUsage(chunk.usage);
          }
        });
      } else {
        const body = (await response.json()) as OpenAiChatChunk;
        const content = body.choices?.[0]?.message?.content ?? '';
        setMessages([...history, { role: 'assistant', content }]);
        if (body.usage) {
          setUsage(body.usage);
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
        <DeploymentPicker value={deployment} onChange={setDeployment} />
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
          placeholder={deployment ? 'Type a message…' : 'Select a deployment first'}
          disabled={!deployment}
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
          disabled={!deployment || !input.trim() || busy}
          onClick={send}
        >
          Send
        </Button>
      </div>
    </PageContainer>
  );
}
