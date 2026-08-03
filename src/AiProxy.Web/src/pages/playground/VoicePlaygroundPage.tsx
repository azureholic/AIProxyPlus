import { useEffect, useRef, useState } from 'react';
import { Badge, Button, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { PlugConnectedRegular, PlugDisconnectedRegular, SendRegular } from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { DeploymentPicker } from '../../components/DeploymentPicker';
import { adminJson } from '../../api/client';

const useStyles = makeStyles({
  controls: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalM,
  },
  log: {
    fontFamily: 'monospace',
    fontSize: tokens.fontSizeBase200,
    whiteSpace: 'pre-wrap',
    maxHeight: '400px',
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground3,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
  },
  eventRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
  },
});

const exampleEvent = JSON.stringify(
  {
    type: 'conversation.item.create',
    item: { type: 'message', role: 'user', content: [{ type: 'input_text', text: 'Hello!' }] },
  },
  null,
  2,
);

type ConnectionState = 'disconnected' | 'connecting' | 'connected';

export function VoicePlaygroundPage() {
  const styles = useStyles();
  const [deployment, setDeployment] = useState('');
  const [state, setState] = useState<ConnectionState>('disconnected');
  const [error, setError] = useState<unknown>(null);
  const [eventText, setEventText] = useState(exampleEvent);
  const [log, setLog] = useState<string[]>([]);
  const socketRef = useRef<WebSocket | null>(null);

  useEffect(() => () => socketRef.current?.close(), []);

  const appendLog = (line: string) => setLog((prev) => [...prev, `${new Date().toLocaleTimeString()}  ${line}`]);

  const connect = async () => {
    setError(null);
    setState('connecting');
    try {
      const { ticket } = await adminJson<{ ticket: string }>('/api/v1/auth/realtime-ticket', { method: 'POST' });
      const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const url = `${protocol}//${window.location.host}/openai/v1/realtime?model=${encodeURIComponent(deployment)}&ticket=${encodeURIComponent(ticket)}`;
      const socket = new WebSocket(url);
      socketRef.current = socket;

      socket.onopen = () => {
        setState('connected');
        appendLog('[connected]');
      };
      socket.onmessage = (event) => appendLog(`< ${event.data}`);
      socket.onerror = () => setError(new Error('WebSocket connection failed.'));
      socket.onclose = (event) => {
        setState('disconnected');
        appendLog(`[closed] code=${event.code}`);
      };
    } catch (reason) {
      setState('disconnected');
      setError(reason);
    }
  };

  const disconnect = () => socketRef.current?.close();

  const sendEvent = () => {
    try {
      JSON.parse(eventText); // validate before sending
      socketRef.current?.send(eventText);
      appendLog(`> ${eventText}`);
    } catch {
      setError(new Error('Event body must be valid JSON.'));
    }
  };

  return (
    <PageContainer>
      <PageHeader title="Voice Playground" />

      <div className={styles.controls}>
        <DeploymentPicker value={deployment} onChange={setDeployment} />
        <Badge color={state === 'connected' ? 'success' : state === 'connecting' ? 'warning' : 'informative'}>
          {state}
        </Badge>
        {state === 'disconnected' ? (
          <Button appearance="primary" icon={<PlugConnectedRegular />} disabled={!deployment} onClick={() => void connect()}>
            Connect
          </Button>
        ) : (
          <Button icon={<PlugDisconnectedRegular />} onClick={disconnect}>
            Disconnect
          </Button>
        )}
      </div>

      <ErrorBanner error={error} />

      <div className={styles.eventRow}>
        <Textarea style={{ flex: 1 }} rows={6} value={eventText} onChange={(_, data) => setEventText(data.value)} />
        <Button icon={<SendRegular />} disabled={state !== 'connected'} onClick={sendEvent}>
          Send event
        </Button>
      </div>

      <div className={styles.log}>{log.length === 0 ? 'No events yet — connect and send one.' : log.join('\n')}</div>
    </PageContainer>
  );
}
