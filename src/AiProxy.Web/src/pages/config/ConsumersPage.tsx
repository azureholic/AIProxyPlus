import { useEffect, useState } from 'react';
import {
  Badge,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  Field,
  Input,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableCellLayout,
  TableHeader,
  TableHeaderCell,
  TableRow,
} from '@fluentui/react-components';
import { AddRegular, ArrowRotateClockwiseRegular, DeleteRegular } from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { SecretRevealDialog } from '../../components/SecretRevealDialog';
import { createConsumer, listConsumers, revokeConsumer, rotateConsumerKey } from '../../api/consumers';
import type { Consumer } from '../../api/types';

export function ConsumersPage() {
  const [consumers, setConsumers] = useState<Consumer[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [busy, setBusy] = useState(false);
  const [revealedKey, setRevealedKey] = useState<string | null>(null);

  const reload = () => {
    setError(null);
    listConsumers().then(setConsumers).catch(setError);
  };

  useEffect(reload, []);

  return (
    <PageContainer>
      <PageHeader
        title="Consumers"
        actions={
          <Dialog open={createOpen} onOpenChange={(_, data) => setCreateOpen(data.open)}>
            <DialogTrigger disableButtonEnhancement>
              <Button appearance="primary" icon={<AddRegular />}>
                New consumer
              </Button>
            </DialogTrigger>
            <DialogSurface>
              <DialogBody>
                <DialogTitle>New consumer</DialogTitle>
                <DialogContent>
                  <Field label="Name">
                    <Input value={newName} onChange={(_, data) => setNewName(data.value)} />
                  </Field>
                  <ErrorBanner error={error} />
                </DialogContent>
                <DialogActions>
                  <DialogTrigger disableButtonEnhancement>
                    <Button appearance="secondary">Cancel</Button>
                  </DialogTrigger>
                  <Button
                    appearance="primary"
                    disabled={busy || !newName.trim()}
                    icon={busy ? <Spinner size="tiny" /> : undefined}
                    onClick={async () => {
                      setBusy(true);
                      setError(null);
                      try {
                        const result = await createConsumer(newName.trim());
                        setNewName('');
                        setCreateOpen(false);
                        setRevealedKey(result.apiKey);
                        reload();
                      } catch (err) {
                        setError(err);
                      } finally {
                        setBusy(false);
                      }
                    }}
                  >
                    Create
                  </Button>
                </DialogActions>
              </DialogBody>
            </DialogSurface>
          </Dialog>
        }
      />

      <ErrorBanner error={error} />

      {consumers === null && <Spinner label="Loading consumers…" />}

      {consumers !== null && consumers.length === 0 && (
        <p>No consumers yet — create one to get an API key for the Playground.</p>
      )}

      {consumers !== null && consumers.length > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
              <TableHeaderCell>Created</TableHeaderCell>
              <TableHeaderCell>Actions</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {consumers.map((consumer) => (
              <TableRow key={consumer.id}>
                <TableCell>
                  <TableCellLayout>{consumer.name}</TableCellLayout>
                </TableCell>
                <TableCell>
                  <Badge color={consumer.revokedAt ? 'danger' : consumer.enabled ? 'success' : 'warning'}>
                    {consumer.revokedAt ? 'Revoked' : consumer.enabled ? 'Enabled' : 'Disabled'}
                  </Badge>
                </TableCell>
                <TableCell>{new Date(consumer.createdAt).toLocaleString()}</TableCell>
                <TableCell>
                  <Button
                    appearance="subtle"
                    icon={<ArrowRotateClockwiseRegular />}
                    aria-label={`Rotate API key for ${consumer.name}`}
                    title="Rotate API key"
                    disabled={consumer.revokedAt !== null}
                    onClick={async () => {
                      try {
                        const result = await rotateConsumerKey(consumer.name);
                        setRevealedKey(result.apiKey);
                      } catch (err) {
                        setError(err);
                      }
                    }}
                  />
                  <ConfirmDialog
                    trigger={
                      <Button
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        aria-label={`Revoke ${consumer.name}`}
                        disabled={consumer.revokedAt !== null}
                      />
                    }
                    title={`Revoke '${consumer.name}'?`}
                    message="This immediately invalidates the consumer's API key. Historical usage records are preserved."
                    confirmLabel="Revoke"
                    onConfirm={async () => {
                      await revokeConsumer(consumer.name);
                      reload();
                    }}
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <SecretRevealDialog
        open={revealedKey !== null}
        secretLabel="Consumer API key"
        secret={revealedKey}
        onClose={() => setRevealedKey(null)}
      />
    </PageContainer>
  );
}
