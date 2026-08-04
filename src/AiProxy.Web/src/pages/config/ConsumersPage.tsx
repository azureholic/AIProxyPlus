import { useEffect, useState } from 'react';
import {
  Badge,
  Button,
  Checkbox,
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
import { AddRegular, ArrowRotateClockwiseRegular, DeleteRegular, SettingsRegular } from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { SecretRevealDialog } from '../../components/SecretRevealDialog';
import {
  allowConsumerModel,
  createConsumer,
  listConsumers,
  revokeConsumer,
  revokeConsumerModel,
  rotateConsumerKey,
} from '../../api/consumers';
import { listModels } from '../../api/models';
import type { Consumer, Model } from '../../api/types';

export function ConsumersPage() {
  const [consumers, setConsumers] = useState<Consumer[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [busy, setBusy] = useState(false);
  const [revealedKey, setRevealedKey] = useState<string | null>(null);
  const [models, setModels] = useState<Model[]>([]);
  const [modelConsumer, setModelConsumer] = useState<Consumer | null>(null);

  const reload = () => {
    setError(null);
    listConsumers().then(setConsumers).catch(setError);
  };

  useEffect(() => {
    reload();
    listModels().then(setModels).catch(setError);
  }, []);

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
                        setRevealedKey(`Key 1: ${result.apiKey1} | Key 2: ${result.apiKey2}`);
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
              <TableHeaderCell>Authentication</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
              <TableHeaderCell>Model access</TableHeaderCell>
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
                <TableCell>{consumer.authenticationType === 'oidc' ? 'OIDC' : 'API key'}</TableCell>
                <TableCell>
                  <Badge color={consumer.revokedAt ? 'danger' : consumer.enabled ? 'success' : 'warning'}>
                    {consumer.revokedAt ? 'Revoked' : consumer.enabled ? 'Enabled' : 'Disabled'}
                  </Badge>
                </TableCell>
                <TableCell>{(consumer.allowedModelIds ?? []).length === 0 ? 'All models' : `${consumer.allowedModelIds.length} models`}</TableCell>
                <TableCell>{new Date(consumer.createdAt).toLocaleString()}</TableCell>
                <TableCell>
                  {([1, 2] as const).map((slot) => (
                    <Button
                      key={slot}
                      appearance="subtle"
                      icon={<ArrowRotateClockwiseRegular />}
                      aria-label={`Rotate key ${slot} for ${consumer.name}`}
                      title={`Rotate key ${slot}`}
                      disabled={consumer.revokedAt !== null || consumer.authenticationType === 'oidc'}
                      onClick={async () => {
                        try {
                          const result = await rotateConsumerKey(consumer.name, slot);
                          setRevealedKey(`Key ${slot}: ${result.apiKey}`);
                        } catch (err) {
                          setError(err);
                        }
                      }}
                    />
                  ))}
                  <Button
                    appearance="subtle"
                    icon={<SettingsRegular />}
                    aria-label={`Configure model access for ${consumer.name}`}
                    title="Configure model access"
                    disabled={consumer.revokedAt !== null}
                    onClick={() => setModelConsumer(consumer)}
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
                    message={consumer.authenticationType === 'oidc'
                      ? "This disables local access without changing the user's identity-provider account. Historical usage records are preserved."
                      : "This immediately invalidates the consumer's API key. Historical usage records are preserved."}
                    confirmLabel={consumer.authenticationType === 'oidc' ? 'Disable' : 'Revoke'}
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

      <Dialog open={modelConsumer !== null} onOpenChange={(_, data) => !data.open && setModelConsumer(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Model access for {modelConsumer?.name}</DialogTitle>
            <DialogContent>
              <p>{(modelConsumer?.allowedModelIds ?? []).length === 0 ? 'Unrestricted: all models are available.' : 'Only selected models are available.'}</p>
              {models.map((model) => (
                <Checkbox
                  key={model.id}
                  label={model.name}
                  checked={modelConsumer?.allowedModelIds?.includes(model.id) ?? false}
                  onChange={async (_, data) => {
                    if (!modelConsumer) return;
                    try {
                      const updated = data.checked
                        ? await allowConsumerModel(modelConsumer.name, model.name)
                        : await revokeConsumerModel(modelConsumer.name, model.name);
                      setModelConsumer(updated);
                      setConsumers((current) => current?.map((item) => item.id === updated.id ? updated : item) ?? null);
                    } catch (err) {
                      setError(err);
                    }
                  }}
                />
              ))}
            </DialogContent>
            <DialogActions>
              <Button appearance="primary" onClick={() => setModelConsumer(null)}>Done</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </PageContainer>
  );
}
