import { useEffect, useState } from 'react';
import {
  Badge,
  Body1,
  Button,
  Card,
  Field,
  Input,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PageContainer, PageHeader } from '../components/PageLayout';
import { getAdminApiKey, getConsumerApiKey, setAdminApiKey, setConsumerApiKey } from '../api/settingsStore';

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
    maxWidth: '480px',
  },
  status: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
});

type ConnectionStatus = 'checking' | 'reachable' | 'unreachable';

export function SettingsPage() {
  const styles = useStyles();
  const [adminKey, setAdminKeyState] = useState(getAdminApiKey());
  const [consumerKey, setConsumerKeyState] = useState(getConsumerApiKey());
  const [status, setStatus] = useState<ConnectionStatus>('checking');

  useEffect(() => {
    let cancelled = false;
    // Unauthenticated — a same-origin/dev-proxy misconfiguration is distinguishable from an auth failure.
    fetch('/health/ready')
      .then((response) => {
        if (!cancelled) {
          setStatus(response.ok ? 'reachable' : 'unreachable');
        }
      })
      .catch(() => {
        if (!cancelled) {
          setStatus('unreachable');
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <PageContainer>
      <PageHeader title="Settings" />
      <Body1>
        These keys are stored only in this browser tab's session storage — they are cleared when the tab
        closes and are never sent anywhere except this same-origin API. This is a placeholder for real
        authentication (architecture.md §17).
      </Body1>

      <Card className={styles.card}>
        <div className={styles.status}>
          {status === 'checking' && <Spinner size="tiny" label="Checking API connection…" />}
          {status === 'reachable' && <Badge color="success">API reachable</Badge>}
          {status === 'unreachable' && <Badge color="danger">API unreachable — check the dev proxy/same-origin setup</Badge>}
        </div>

        <Field label="Admin API Key" hint="Used for Config (Model Deployments, Consumers) and the Usage dashboard.">
          <Input
            type="password"
            value={adminKey}
            onChange={(_, data) => setAdminKeyState(data.value)}
            placeholder="X-Admin-Api-Key"
          />
        </Field>

        <Field label="Consumer API Key" hint="Used for the Playground (chat/images/voice) — create one under Config → Consumers.">
          <Input
            type="password"
            value={consumerKey}
            onChange={(_, data) => setConsumerKeyState(data.value)}
            placeholder="X-Api-Key"
          />
        </Field>

        <Button
          appearance="primary"
          onClick={() => {
            setAdminApiKey(adminKey);
            setConsumerApiKey(consumerKey);
          }}
        >
          Save
        </Button>
      </Card>
    </PageContainer>
  );
}
