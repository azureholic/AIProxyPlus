import { useState } from 'react';
import { Button, Spinner, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { ImageAddRegular } from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { DeploymentPicker } from '../../components/DeploymentPicker';
import { consumerFetch } from '../../api/client';

const useStyles = makeStyles({
  controls: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalM,
  },
  gallery: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
  },
  image: {
    maxWidth: '256px',
    maxHeight: '256px',
    borderRadius: tokens.borderRadiusMedium,
  },
});

interface ImageResult {
  url?: string;
  b64_json?: string;
}

export function ImagePlaygroundPage() {
  const styles = useStyles();
  const [deployment, setDeployment] = useState('');
  const [prompt, setPrompt] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [images, setImages] = useState<ImageResult[]>([]);

  const generate = async () => {
    setBusy(true);
    setError(null);
    try {
      const response = await consumerFetch('/openai/v1/images/generations', {
        method: 'POST',
        body: JSON.stringify({ model: deployment, prompt }),
      });

      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as { error?: string };
        throw new Error(body.error ?? `Request failed with status ${response.status}`);
      }

      const body = (await response.json()) as { data?: ImageResult[] };
      setImages(body.data ?? []);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <PageContainer>
      <PageHeader title="Image Playground" />

      <div className={styles.controls}>
        <DeploymentPicker value={deployment} onChange={setDeployment} />
      </div>

      <Textarea
        placeholder="Describe the image you want to generate…"
        value={prompt}
        onChange={(_, data) => setPrompt(data.value)}
      />

      <Button
        appearance="primary"
        icon={busy ? <Spinner size="tiny" /> : <ImageAddRegular />}
        disabled={!deployment || !prompt.trim() || busy}
        onClick={generate}
      >
        Generate
      </Button>

      <ErrorBanner error={error} />

      <div className={styles.gallery}>
        {images.map((image, index) => (
          <img
            key={index}
            className={styles.image}
            src={image.url ?? (image.b64_json ? `data:image/png;base64,${image.b64_json}` : '')}
            alt={`Generated result ${index + 1}`}
          />
        ))}
      </div>
    </PageContainer>
  );
}
