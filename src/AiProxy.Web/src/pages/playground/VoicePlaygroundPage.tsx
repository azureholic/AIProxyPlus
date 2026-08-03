import { useEffect, useRef, useState } from 'react';
import { Badge, Button, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { MicRegular, PlugConnectedRegular, PlugDisconnectedRegular, SendRegular } from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { ModelPicker } from '../../components/ModelPicker';
import { adminJson } from '../../api/client';
import { getAudioDelta, RealtimeAudioSession } from './realtimeAudio';

const useStyles = makeStyles({
  controls: {
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
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
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1fr) auto',
    gap: tokens.spacingHorizontalM,
    alignItems: 'start',
    '@media (max-width: 700px)': {
      gridTemplateColumns: 'minmax(0, 1fr)',
    },
  },
  talk: {
    alignSelf: 'flex-start',
    minWidth: '180px',
  },
  eventEditor: {
    width: '100%',
    minWidth: 0,
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
  const [model, setModel] = useState('');
  const [state, setState] = useState<ConnectionState>('disconnected');
  const [error, setError] = useState<unknown>(null);
  const [isRecording, setIsRecording] = useState(false);
  const [eventText, setEventText] = useState(exampleEvent);
  const [log, setLog] = useState<string[]>([]);
  const socketRef = useRef<WebSocket | null>(null);
  const audioRef = useRef<RealtimeAudioSession | null>(null);

  useEffect(() => () => {
    void audioRef.current?.close();
    socketRef.current?.close();
  }, []);

  const appendLog = (line: string) => setLog((prev) => [...prev, `${new Date().toLocaleTimeString()}  ${line}`]);

  const connect = async () => {
    setError(null);
    setState('connecting');
    try {
      const { ticket } = await adminJson<{ ticket: string }>('/api/v1/auth/realtime-ticket', { method: 'POST' });
      const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
      const url = `${protocol}//${window.location.host}/openai/v1/realtime?model=${encodeURIComponent(model)}&ticket=${encodeURIComponent(ticket)}`;
      const socket = new WebSocket(url);
      socketRef.current = socket;

      socket.onopen = () => {
        const audio = new RealtimeAudioSession(socket);
        audio.configure();
        audioRef.current = audio;
        setState('connected');
        appendLog('[connected]');
      };
      socket.onmessage = (message) => {
        appendLog(`< ${message.data}`);
        try {
          const delta = getAudioDelta(JSON.parse(message.data));
          if (delta) {
            void audioRef.current?.play(delta).catch(setError);
          }
        } catch {
          appendLog('[ignored non-JSON event]');
        }
      };
      socket.onerror = () => setError(new Error('WebSocket connection failed.'));
      socket.onclose = (event) => {
        void audioRef.current?.close();
        audioRef.current = null;
        socketRef.current = null;
        setIsRecording(false);
        setState('disconnected');
        appendLog(`[closed] code=${event.code}`);
      };
    } catch (reason) {
      setState('disconnected');
      setError(reason);
    }
  };

  const disconnect = () => socketRef.current?.close();

  const startTalking = async () => {
    if (state !== 'connected' || isRecording || !audioRef.current) {
      return;
    }
    setError(null);
    try {
      const started = await audioRef.current.startCapture();
      setIsRecording(started);
    } catch (reason) {
      setError(reason);
      await audioRef.current.stopCapture(false);
    }
  };

  const stopTalking = async () => {
    if (!isRecording || !audioRef.current) {
      return;
    }
    setIsRecording(false);
    try {
      await audioRef.current.stopCapture(true);
    } catch (reason) {
      setError(reason);
    }
  };

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
        <ModelPicker value={model} onChange={setModel} />
        <Badge color={state === 'connected' ? 'success' : state === 'connecting' ? 'warning' : 'informative'}>
          {state}
        </Badge>
        {state === 'disconnected' ? (
          <Button appearance="primary" icon={<PlugConnectedRegular />} disabled={!model} onClick={() => void connect()}>
            Connect
          </Button>
        ) : (
          <Button icon={<PlugDisconnectedRegular />} onClick={disconnect}>
            Disconnect
          </Button>
        )}
      </div>

      <ErrorBanner error={error} />

      <Button
        className={styles.talk}
        appearance={isRecording ? 'primary' : 'secondary'}
        icon={<MicRegular />}
        disabled={state !== 'connected'}
        aria-pressed={isRecording}
        onPointerDown={(event) => {
          event.currentTarget.setPointerCapture(event.pointerId);
          void startTalking();
        }}
        onPointerUp={() => void stopTalking()}
        onPointerCancel={() => void stopTalking()}
        onKeyDown={(event) => {
          if ((event.key === ' ' || event.key === 'Enter') && !event.repeat) {
            event.preventDefault();
            void startTalking();
          }
        }}
        onKeyUp={(event) => {
          if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            void stopTalking();
          }
        }}
      >
        {isRecording ? 'Release to send' : 'Hold to talk'}
      </Button>

      <div className={styles.eventRow}>
        <Textarea className={styles.eventEditor} rows={6} value={eventText} onChange={(_, data) => setEventText(data.value)} />
        <Button icon={<SendRegular />} disabled={state !== 'connected'} onClick={sendEvent}>
          Send event
        </Button>
      </div>

      <div className={styles.log}>{log.length === 0 ? 'No events yet — connect and send one.' : log.join('\n')}</div>
    </PageContainer>
  );
}
