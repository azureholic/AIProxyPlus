import { useEffect, useState } from 'react';
import {
  Badge,
  Body1,
  Button,
  Card,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PageContainer, PageHeader } from '../components/PageLayout';
import { useAuth } from '../auth/AuthContext';

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
  const { session, logout } = useAuth();
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
      <Card className={styles.card}>
        <div className={styles.status}>
          {status === 'checking' && <Spinner size="tiny" label="Checking API connection…" />}
          {status === 'reachable' && <Badge color="success">API reachable</Badge>}
          {status === 'unreachable' && <Badge color="danger">API unreachable — check the dev proxy/same-origin setup</Badge>}
        </div>

        <Body1>{session?.username}</Body1>
        <Body1>{session?.issuer}</Body1>
        <Button onClick={() => void logout()}>Sign out</Button>
      </Card>
    </PageContainer>
  );
}
