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
  Dropdown,
  Field,
  Input,
  Option,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableCellLayout,
  TableHeader,
  TableHeaderCell,
  TableRow,
} from '@fluentui/react-components';
import {
  AddRegular,
  ArrowRotateClockwiseRegular,
  DeleteRegular,
  DismissCircleRegular,
  PauseRegular,
  PlayRegular,
  SettingsRegular,
} from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { SecretRevealDialog } from '../../components/SecretRevealDialog';
import {
  allowConsumerModel,
  createConsumer,
  createManagedIdentityConsumer,
  createOidcConsumer,
  deleteConsumerPermanently,
  disableConsumer,
  enableConsumer,
  listConsumers,
  revokeConsumer,
  revokeConsumerModel,
  rotateConsumerKey,
} from '../../api/consumers';
import { listModels } from '../../api/models';
import type { Consumer, Model } from '../../api/types';

function authenticationLabel(consumer: Consumer) {
  if (consumer.authenticationType === 'managedIdentity') return 'Managed identity';
  if (consumer.authenticationType === 'oidc') return 'OIDC';
  return 'API key';
}

function authenticationDetails(consumer: Consumer) {
  if (consumer.authenticationType === 'managedIdentity') {
    return `${consumer.managedIdentityTenantId} / ${consumer.managedIdentityObjectId}`;
  }
  if (consumer.authenticationType === 'oidc') {
    return `${consumer.oidcIssuer} / ${consumer.oidcSubject}`;
  }
  return undefined;
}

export function ConsumersPage() {
  const [consumers, setConsumers] = useState<Consumer[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [authenticationType, setAuthenticationType] = useState<'apiKey' | 'oidc' | 'managedIdentity'>('apiKey');
  const [issuer, setIssuer] = useState('');
  const [subject, setSubject] = useState('');
  const [tenantId, setTenantId] = useState('');
  const [objectId, setObjectId] = useState('');
  const [busy, setBusy] = useState(false);
  const [revealedKeys, setRevealedKeys] = useState<ReadonlyArray<{ label: string; value: string }> | null>(null);
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
                  <Field label="Authentication">
                    <Dropdown
                      value={authenticationType === 'apiKey' ? 'API key' : authenticationType === 'oidc' ? 'OIDC' : 'Managed identity'}
                      selectedOptions={[authenticationType]}
                      onOptionSelect={(_, data) => setAuthenticationType(data.optionValue as typeof authenticationType)}
                    >
                      <Option value="apiKey">API key</Option>
                      <Option value="oidc">OIDC</Option>
                      <Option value="managedIdentity">Managed identity</Option>
                    </Dropdown>
                  </Field>
                  {authenticationType === 'oidc' && (
                    <>
                      <Field label="Issuer"><Input value={issuer} onChange={(_, data) => setIssuer(data.value)} /></Field>
                      <Field label="Subject"><Input value={subject} onChange={(_, data) => setSubject(data.value)} /></Field>
                    </>
                  )}
                  {authenticationType === 'managedIdentity' && (
                    <>
                      <Field label="Tenant ID"><Input value={tenantId} onChange={(_, data) => setTenantId(data.value)} /></Field>
                      <Field label="Object ID"><Input value={objectId} onChange={(_, data) => setObjectId(data.value)} /></Field>
                    </>
                  )}
                  <ErrorBanner error={error} />
                </DialogContent>
                <DialogActions>
                  <DialogTrigger disableButtonEnhancement>
                    <Button appearance="secondary">Cancel</Button>
                  </DialogTrigger>
                  <Button
                    appearance="primary"
                    disabled={busy || !newName.trim() ||
                      (authenticationType === 'oidc' && (!issuer.trim() || !subject.trim())) ||
                      (authenticationType === 'managedIdentity' && (!tenantId.trim() || !objectId.trim()))}
                    icon={busy ? <Spinner size="tiny" /> : undefined}
                    onClick={async () => {
                      setBusy(true);
                      setError(null);
                      try {
                        if (authenticationType === 'oidc') {
                          await createOidcConsumer(newName.trim(), issuer.trim(), subject.trim());
                        } else if (authenticationType === 'managedIdentity') {
                          await createManagedIdentityConsumer(newName.trim(), tenantId.trim(), objectId.trim());
                        } else {
                          const result = await createConsumer(newName.trim());
                          setRevealedKeys([
                            { label: 'Key 1', value: result.apiKey1 },
                            { label: 'Key 2', value: result.apiKey2 },
                          ]);
                        }
                        setNewName('');
                        setIssuer('');
                        setSubject('');
                        setTenantId('');
                        setObjectId('');
                        setCreateOpen(false);
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
                <TableCell>
                  <TableCellLayout description={authenticationDetails(consumer)}>
                    {authenticationLabel(consumer)}
                  </TableCellLayout>
                </TableCell>
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
                      disabled={consumer.revokedAt !== null || consumer.authenticationType !== 'apiKey'}
                      onClick={async () => {
                        try {
                          const result = await rotateConsumerKey(consumer.name, slot);
                          setRevealedKeys([{ label: `Key ${slot}`, value: result.apiKey }]);
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
                  {!consumer.revokedAt && (
                    <Button
                      appearance="subtle"
                      icon={consumer.enabled ? <PauseRegular /> : <PlayRegular />}
                      aria-label={`${consumer.enabled ? 'Disable' : 'Enable'} ${consumer.name}`}
                      title={consumer.enabled ? 'Disable' : 'Enable'}
                      onClick={async () => {
                        try {
                          const updated = consumer.enabled
                            ? await disableConsumer(consumer.name)
                            : await enableConsumer(consumer.name);
                          setConsumers((current) => current?.map((item) => item.id === updated.id ? updated : item) ?? null);
                        } catch (err) {
                          setError(err);
                        }
                      }}
                    />
                  )}
                  {!consumer.revokedAt && (
                    <ConfirmDialog
                      trigger={
                        <Button
                          appearance="subtle"
                          icon={<DismissCircleRegular />}
                          aria-label={`Revoke ${consumer.name}`}
                          title="Revoke permanently"
                        />
                      }
                      title={`Revoke '${consumer.name}'?`}
                      message={consumer.authenticationType !== 'apiKey'
                        ? "This permanently revokes local access and cannot be undone. The external identity is unchanged. Historical usage records are preserved."
                        : "This permanently invalidates both API keys and cannot be undone. Historical usage records are preserved."}
                      confirmLabel="Revoke"
                      onConfirm={async () => {
                        await revokeConsumer(consumer.name);
                        reload();
                      }}
                    />
                  )}
                  {consumer.revokedAt && (
                    <ConfirmDialog
                      trigger={
                        <Button
                          appearance="subtle"
                          icon={<DeleteRegular />}
                          aria-label={`Delete permanently ${consumer.name}`}
                          title="Delete permanently"
                        />
                      }
                      title={`Delete '${consumer.name}' permanently?`}
                      message="This removes the consumer permanently. Historical usage records are preserved."
                      confirmLabel="Delete permanently"
                      onConfirm={async () => {
                        await deleteConsumerPermanently(consumer.name);
                        reload();
                      }}
                    />
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <SecretRevealDialog
        open={revealedKeys !== null}
        title={revealedKeys?.length === 2 ? 'Consumer API keys' : 'Consumer API key'}
        secrets={revealedKeys}
        onClose={() => setRevealedKeys(null)}
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
