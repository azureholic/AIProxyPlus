import { useEffect, useRef, useState } from 'react';
import {
  Button,
  Field,
  Select,
  Spinner,
  Tab,
  TabList,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ArrowDownloadRegular, MicRecordRegular, RecordStopRegular, Speaker2Regular } from '@fluentui/react-icons';
import { consumerFetch, toApiError } from '../../api/client';
import { ErrorBanner } from '../../components/Banners';
import { ModelPicker } from '../../components/ModelPicker';
import { PageContainer, PageHeader } from '../../components/PageLayout';

type SpeechMode = 'transcribe' | 'synthesize';

const useStyles = makeStyles({
  form: {
    display: 'grid',
    gap: tokens.spacingVerticalM,
    maxWidth: '760px',
  },
  row: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
    gap: tokens.spacingHorizontalM,
    alignItems: 'end',
  },
  audio: {
    width: '100%',
    maxWidth: '560px',
  },
  actions: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  fileInput: {
    minHeight: '32px',
    font: 'inherit',
  },
});

export function SpeechPlaygroundPage() {
  const styles = useStyles();
  const [mode, setMode] = useState<SpeechMode>('transcribe');
  const [model, setModel] = useState('');
  const [audioInput, setAudioInput] = useState<Blob | null>(null);
  const [audioInputName, setAudioInputName] = useState('recording.webm');
  const [inputUrl, setInputUrl] = useState('');
  const [transcription, setTranscription] = useState('');
  const [text, setText] = useState('');
  const [voice, setVoice] = useState('alloy');
  const [outputUrl, setOutputUrl] = useState('');
  const [outputType, setOutputType] = useState('audio/mpeg');
  const [recording, setRecording] = useState(false);
  const [recordingSeconds, setRecordingSeconds] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const inputUrlRef = useRef('');
  const outputUrlRef = useRef('');

  const releaseInputUrl = () => {
    if (inputUrlRef.current) URL.revokeObjectURL(inputUrlRef.current);
    inputUrlRef.current = '';
    setInputUrl('');
  };

  const releaseOutputUrl = () => {
    if (outputUrlRef.current) URL.revokeObjectURL(outputUrlRef.current);
    outputUrlRef.current = '';
    setOutputUrl('');
  };

  const stopMedia = () => {
    recorderRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    setRecording(false);
  };

  useEffect(() => () => {
    abortRef.current?.abort();
    if (recorderRef.current?.state === 'recording') recorderRef.current.stop();
    streamRef.current?.getTracks().forEach((track) => track.stop());
    if (inputUrlRef.current) URL.revokeObjectURL(inputUrlRef.current);
    if (outputUrlRef.current) URL.revokeObjectURL(outputUrlRef.current);
  }, []);

  useEffect(() => {
    if (!recording) return;
    const timer = window.setInterval(() => setRecordingSeconds((seconds) => seconds + 1), 1000);
    return () => window.clearInterval(timer);
  }, [recording]);

  const changeMode = (nextMode: SpeechMode) => {
    abortRef.current?.abort();
    if (recorderRef.current?.state === 'recording') recorderRef.current.stop();
    stopMedia();
    releaseInputUrl();
    releaseOutputUrl();
    setAudioInput(null);
    setRecordingSeconds(0);
    setBusy(false);
    setModel('');
    setError(null);
    setMode(nextMode);
  };

  const chooseAudio = (file: File | null) => {
    releaseInputUrl();
    if (file && !file.type.startsWith('audio/')) {
      setAudioInput(null);
      setError(new Error('Select a supported audio file.'));
      return;
    }
    setAudioInput(file);
    setAudioInputName(file?.name ?? 'recording.webm');
    const nextUrl = file ? URL.createObjectURL(file) : '';
    inputUrlRef.current = nextUrl;
    setInputUrl(nextUrl);
    setTranscription('');
    setError(null);
  };

  const startRecording = async () => {
    setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const recorder = new MediaRecorder(stream);
      const chunks: BlobPart[] = [];
      streamRef.current = stream;
      recorderRef.current = recorder;
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunks.push(event.data);
      };
      recorder.onstop = () => {
        const blob = new Blob(chunks, { type: recorder.mimeType || 'audio/webm' });
        chooseAudio(new File([blob], 'recording.webm', { type: blob.type }));
        stopMedia();
      };
      recorder.start();
      setRecordingSeconds(0);
      setRecording(true);
    } catch (caught) {
      stopMedia();
      setError(caught);
    }
  };

  const transcribe = async () => {
    if (!audioInput) return;
    const controller = new AbortController();
    abortRef.current?.abort();
    abortRef.current = controller;
    setBusy(true);
    setError(null);
    try {
      const form = new FormData();
      form.append('model', model);
      form.append('file', audioInput, audioInputName);
      const response = await consumerFetch('/openai/v1/audio/transcriptions', {
        method: 'POST',
        body: form,
        signal: controller.signal,
      });
      if (!response.ok) throw await toApiError(response);
      const body = (await response.json()) as { text?: string };
      setTranscription(body.text ?? '');
    } catch (caught) {
      if (!controller.signal.aborted) setError(caught);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  };

  const synthesize = async () => {
    const controller = new AbortController();
    abortRef.current?.abort();
    abortRef.current = controller;
    setBusy(true);
    setError(null);
    releaseOutputUrl();
    try {
      const response = await consumerFetch('/openai/v1/audio/speech', {
        method: 'POST',
        body: JSON.stringify({ model, input: text, voice }),
        signal: controller.signal,
      });
      if (!response.ok) throw await toApiError(response);
      const blob = await response.blob();
      setOutputType(blob.type || 'audio/mpeg');
      const nextUrl = URL.createObjectURL(blob);
      outputUrlRef.current = nextUrl;
      setOutputUrl(nextUrl);
    } catch (caught) {
      if (!controller.signal.aborted) setError(caught);
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  };

  return (
    <PageContainer>
      <PageHeader title="Speech Playground" />
      <TabList
        selectedValue={mode}
        onTabSelect={(_, data) => changeMode(data.value as SpeechMode)}
      >
        <Tab value="transcribe">Speech to text</Tab>
        <Tab value="synthesize">Text to speech</Tab>
      </TabList>

      <div className={styles.form}>
        <ModelPicker
          value={model}
          onChange={setModel}
          capability={mode === 'transcribe' ? 'SpeechToText' : 'TextToSpeech'}
        />

        {mode === 'transcribe' ? (
          <>
            <div className={styles.row}>
              <Field label="Audio file">
                <input
                  className={styles.fileInput}
                  type="file"
                  accept="audio/*"
                  aria-label="Audio file"
                  onChange={(event) => chooseAudio(event.target.files?.[0] ?? null)}
                />
              </Field>
              <Button
                icon={recording ? <RecordStopRegular /> : <MicRecordRegular />}
                onClick={() => recording ? recorderRef.current?.stop() : void startRecording()}
              >
                {recording ? `Stop recording (${recordingSeconds}s)` : 'Record microphone'}
              </Button>
            </div>
            {inputUrl && <audio className={styles.audio} controls src={inputUrl} onError={() => setError(new Error('The selected audio could not be played.'))} />}
            <Button appearance="primary" disabled={!model || !audioInput || busy || recording} onClick={transcribe}>
              {busy ? <Spinner size="tiny" /> : 'Transcribe'}
            </Button>
            {transcription && <Field label="Transcription"><Textarea readOnly value={transcription} /></Field>}
          </>
        ) : (
          <>
            <Field label="Text"><Textarea value={text} onChange={(_, data) => setText(data.value)} /></Field>
            <Field label="Voice">
              <Select value={voice} onChange={(_, data) => setVoice(data.value)}>
                {['alloy', 'ash', 'coral', 'echo', 'fable', 'nova', 'onyx', 'sage', 'shimmer'].map((item) => (
                  <option key={item} value={item}>{item}</option>
                ))}
              </Select>
            </Field>
            <Button appearance="primary" icon={<Speaker2Regular />} disabled={!model || !text.trim() || busy} onClick={synthesize}>
              {busy ? <Spinner size="tiny" /> : 'Generate speech'}
            </Button>
            {outputUrl && (
              <>
                <audio className={styles.audio} controls autoPlay src={outputUrl} onError={() => setError(new Error('The generated audio could not be played.'))} />
                <div className={styles.actions}>
                  <Button as="a" href={outputUrl} download={`speech.${outputType.includes('wav') ? 'wav' : 'mp3'}`} icon={<ArrowDownloadRegular />}>
                    Download
                  </Button>
                </div>
              </>
            )}
          </>
        )}

        <ErrorBanner error={error} />
      </div>
    </PageContainer>
  );
}