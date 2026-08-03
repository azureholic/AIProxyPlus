import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Badge,
  Body1,
  Button,
  Combobox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  Field,
  Input,
  Option,
  Spinner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableCellLayout,
  TableHeader,
  TableHeaderCell,
  TableRow,
} from '@fluentui/react-components';
import { AddRegular, ArrowLeftRegular, DeleteRegular, EditRegular, KeyRegular } from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import {
  addEndpoint,
  getModelDeployment,
  removeEndpoint,
  setEndpointSecret,
  updateEndpoint,
  type AddEndpointInput,
} from '../../api/modelDeployments';
import type { ApiFamily, AuthMode, Endpoint, ModelDeployment } from '../../api/types';

const apiFamilies: ApiFamily[] = ['OpenAI', 'AzureOpenAI', 'Anthropic', 'OpenAICompatible'];
const authModes: AuthMode[] = ['ApiKey', 'ManagedIdentity'];

const emptyNewEndpoint: AddEndpointInput = {
  baseUrl: '',
  apiFamily: 'OpenAI',
  priority: 1,
  authMode: 'ApiKey',
  apiKeyPlaintext: '',
  managedIdentityScope: '',
  managedIdentityTenantId: '',
  enabled: true,
  timeoutSeconds: 100,
  maxRetries: 2,
};

export function ModelDeploymentDetailPage() {
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [deployment, setDeployment] = useState<ModelDeployment | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [newEndpoint, setNewEndpoint] = useState<AddEndpointInput>(emptyNewEndpoint);
  const [busy, setBusy] = useState(false);
  const [secretDialogEndpoint, setSecretDialogEndpoint] = useState<Endpoint | null>(null);
  const [secretValue, setSecretValue] = useState('');
  const [editDialogEndpoint, setEditDialogEndpoint] = useState<Endpoint | null>(null);
  const [editValue, setEditValue] = useState({
    baseUrl: '',
    managedIdentityScope: '',
    managedIdentityTenantId: '',
    priority: 1,
    enabled: true,
    timeoutSeconds: 100,
    maxRetries: 2,
  });

  const reload = () => {
    if (!name) return;
    setError(null);
    getModelDeployment(name).then(setDeployment).catch(setError);
  };

  useEffect(reload, [name]);

  if (!name) {
    return null;
  }

  return (
    <PageContainer>
      <PageHeader
        title={`Model Deployment: ${name}`}
        actions={
          <Button appearance="subtle" icon={<ArrowLeftRegular />} onClick={() => navigate('/config/model-deployments')}>
            Back to list
          </Button>
        }
      />

      <ErrorBanner error={error} />

      {deployment === null && error === null && <Spinner label="Loading…" />}

      {deployment !== null && (
        <>
          <Body1>
            Strategy: <strong>{deployment.strategy}</strong> — round-robins across endpoints that share the lowest
            priority, failing over on 429/5xx (architecture.md §9).
          </Body1>

          <PageHeader
            title="Endpoints"
            actions={
              <Dialog open={addOpen} onOpenChange={(_, data) => setAddOpen(data.open)}>
                <DialogTrigger disableButtonEnhancement>
                  <Button appearance="primary" icon={<AddRegular />} onClick={() => setNewEndpoint(emptyNewEndpoint)}>
                    Add endpoint
                  </Button>
                </DialogTrigger>
                <DialogSurface>
                  <DialogBody>
                    <DialogTitle>Add endpoint</DialogTitle>
                    <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                      <Field label="Base URL">
                        <Input
                          value={newEndpoint.baseUrl}
                          onChange={(_, data) => setNewEndpoint({ ...newEndpoint, baseUrl: data.value })}
                        />
                      </Field>
                      <Field label="API family">
                        <Combobox
                          selectedOptions={[newEndpoint.apiFamily]}
                          value={newEndpoint.apiFamily}
                          onOptionSelect={(_, data) =>
                            setNewEndpoint({ ...newEndpoint, apiFamily: data.optionValue as ApiFamily })
                          }
                        >
                          {apiFamilies.map((family) => (
                            <Option key={family} value={family}>
                              {family}
                            </Option>
                          ))}
                        </Combobox>
                      </Field>
                      <Field label="Priority" hint="Endpoints sharing the lowest priority round-robin together.">
                        <Input
                          type="number"
                          value={String(newEndpoint.priority)}
                          onChange={(_, data) => setNewEndpoint({ ...newEndpoint, priority: Number(data.value) || 0 })}
                        />
                      </Field>
                      <Field label="Auth mode">
                        <Combobox
                          selectedOptions={[newEndpoint.authMode]}
                          value={newEndpoint.authMode}
                          onOptionSelect={(_, data) =>
                            setNewEndpoint({ ...newEndpoint, authMode: data.optionValue as AuthMode })
                          }
                        >
                          {authModes.map((mode) => (
                            <Option key={mode} value={mode}>
                              {mode}
                            </Option>
                          ))}
                        </Combobox>
                      </Field>
                      {newEndpoint.authMode === 'ApiKey' && (
                        <Field label="API key" hint="Stored in Vault — never shown again after save (architecture.md §8).">
                          <Input
                            type="password"
                            value={newEndpoint.apiKeyPlaintext}
                            onChange={(_, data) => setNewEndpoint({ ...newEndpoint, apiKeyPlaintext: data.value })}
                          />
                        </Field>
                      )}
                      {newEndpoint.authMode === 'ManagedIdentity' && (
                        <>
                          <Field label="Managed identity scope">
                            <Input
                              value={newEndpoint.managedIdentityScope}
                              onChange={(_, data) => setNewEndpoint({ ...newEndpoint, managedIdentityScope: data.value })}
                            />
                          </Field>
                          <Field label="Managed identity tenant ID">
                            <Input
                              value={newEndpoint.managedIdentityTenantId}
                              onChange={(_, data) => setNewEndpoint({ ...newEndpoint, managedIdentityTenantId: data.value })}
                            />
                          </Field>
                        </>
                      )}
                      <Field label="Timeout (seconds)">
                        <Input
                          type="number"
                          value={String(newEndpoint.timeoutSeconds)}
                          onChange={(_, data) => setNewEndpoint({ ...newEndpoint, timeoutSeconds: Number(data.value) || 0 })}
                        />
                      </Field>
                      <Field label="Max retries">
                        <Input
                          type="number"
                          value={String(newEndpoint.maxRetries)}
                          onChange={(_, data) => setNewEndpoint({ ...newEndpoint, maxRetries: Number(data.value) || 0 })}
                        />
                      </Field>
                      <Switch
                        checked={newEndpoint.enabled}
                        onChange={(_, data) => setNewEndpoint({ ...newEndpoint, enabled: data.checked })}
                        label="Enabled"
                      />
                      <ErrorBanner error={error} />
                    </DialogContent>
                    <DialogActions>
                      <DialogTrigger disableButtonEnhancement>
                        <Button appearance="secondary">Cancel</Button>
                      </DialogTrigger>
                      <Button
                        appearance="primary"
                        disabled={busy || !newEndpoint.baseUrl.trim()}
                        icon={busy ? <Spinner size="tiny" /> : undefined}
                        onClick={async () => {
                          setBusy(true);
                          setError(null);
                          try {
                            await addEndpoint(name, newEndpoint);
                            setAddOpen(false);
                            reload();
                          } catch (err) {
                            setError(err);
                          } finally {
                            setBusy(false);
                          }
                        }}
                      >
                        Add
                      </Button>
                    </DialogActions>
                  </DialogBody>
                </DialogSurface>
              </Dialog>
            }
          />

          {deployment.endpoints.length === 0 && <p>No endpoints yet — add one so this deployment can route traffic.</p>}

          {deployment.endpoints.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Base URL</TableHeaderCell>
                  <TableHeaderCell>API family</TableHeaderCell>
                  <TableHeaderCell>Priority</TableHeaderCell>
                  <TableHeaderCell>Auth</TableHeaderCell>
                  <TableHeaderCell>Enabled</TableHeaderCell>
                  <TableHeaderCell>Health</TableHeaderCell>
                  <TableHeaderCell>Timeout</TableHeaderCell>
                  <TableHeaderCell>Max retries</TableHeaderCell>
                  <TableHeaderCell>Actions</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {deployment.endpoints.map((endpoint) => (
                  <EndpointRow
                    key={endpoint.id}
                    endpoint={endpoint}
                    deploymentName={name}
                    onChanged={reload}
                    onError={setError}
                    onEdit={() => {
                      setEditDialogEndpoint(endpoint);
                      setEditValue({
                        baseUrl: endpoint.baseUrl,
                        managedIdentityScope: endpoint.managedIdentityScope ?? '',
                        managedIdentityTenantId: endpoint.managedIdentityTenantId ?? '',
                        priority: endpoint.priority,
                        enabled: endpoint.enabled,
                        timeoutSeconds: endpoint.timeoutSeconds,
                        maxRetries: endpoint.maxRetries,
                      });
                    }}
                    onSetSecret={() => {
                      setSecretDialogEndpoint(endpoint);
                      setSecretValue('');
                    }}
                  />
                ))}
              </TableBody>
            </Table>
          )}
        </>
      )}

      <Dialog open={editDialogEndpoint !== null} onOpenChange={(_, data) => !data.open && setEditDialogEndpoint(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Edit endpoint</DialogTitle>
            <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <Field label="Base URL">
                <Input value={editValue.baseUrl} onChange={(_, data) => setEditValue(current => ({ ...current, baseUrl: data.value }))} />
              </Field>
              <Field label="API family"><Input value={editDialogEndpoint?.apiFamily ?? ''} disabled /></Field>
              <Field label="Auth mode"><Input value={editDialogEndpoint?.authMode ?? ''} disabled /></Field>
              {editDialogEndpoint?.authMode === 'ManagedIdentity' && (
                <>
                  <Field label="Managed identity scope">
                    <Input
                      value={editValue.managedIdentityScope}
                      onChange={(_, data) => setEditValue(current => ({ ...current, managedIdentityScope: data.value }))}
                    />
                  </Field>
                  <Field label="Managed identity tenant ID">
                    <Input
                      value={editValue.managedIdentityTenantId}
                      onChange={(_, data) => setEditValue(current => ({ ...current, managedIdentityTenantId: data.value }))}
                    />
                  </Field>
                </>
              )}
              <Field label="Priority" hint="Endpoints sharing the lowest priority round-robin together.">
                <Input type="number" value={String(editValue.priority)} onChange={(_, data) => setEditValue(current => ({ ...current, priority: Number(data.value) || 0 }))} />
              </Field>
              <Field label="Timeout (seconds)">
                <Input type="number" value={String(editValue.timeoutSeconds)} onChange={(_, data) => setEditValue(current => ({ ...current, timeoutSeconds: Number(data.value) || 0 }))} />
              </Field>
              <Field label="Max retries">
                <Input type="number" value={String(editValue.maxRetries)} onChange={(_, data) => setEditValue(current => ({ ...current, maxRetries: Number(data.value) || 0 }))} />
              </Field>
              <Switch checked={editValue.enabled} onChange={(_, data) => setEditValue(current => ({ ...current, enabled: data.checked }))} label="Enabled" />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setEditDialogEndpoint(null)}>Cancel</Button>
              <Button
                appearance="primary"
                disabled={
                  busy ||
                  !editValue.baseUrl.trim() ||
                  editValue.priority < 1 ||
                  editValue.timeoutSeconds < 1 ||
                  editValue.maxRetries < 0 ||
                  (editDialogEndpoint?.authMode === 'ManagedIdentity' && !editValue.managedIdentityScope.trim())
                }
                icon={busy ? <Spinner size="tiny" /> : undefined}
                onClick={async () => {
                  if (!editDialogEndpoint) return;
                  setBusy(true);
                  setError(null);
                  try {
                    await updateEndpoint(name, editDialogEndpoint.id, {
                      ...editValue,
                      managedIdentityScope: editDialogEndpoint.authMode === 'ManagedIdentity' ? editValue.managedIdentityScope : undefined,
                      managedIdentityTenantId: editDialogEndpoint.authMode === 'ManagedIdentity' ? editValue.managedIdentityTenantId : undefined,
                    });
                    setEditDialogEndpoint(null);
                    reload();
                  } catch (err) {
                    setError(err);
                  } finally {
                    setBusy(false);
                  }
                }}
              >Save changes</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog open={secretDialogEndpoint !== null} onOpenChange={(_, data) => !data.open && setSecretDialogEndpoint(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Set / replace API key</DialogTitle>
            <DialogContent>
              <Field label="New API key" hint="Write-only — this replaces the endpoint's stored secret in Vault.">
                <Input type="password" value={secretValue} onChange={(_, data) => setSecretValue(data.value)} />
              </Field>
              <ErrorBanner error={error} />
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button appearance="secondary">Cancel</Button>
              </DialogTrigger>
              <Button
                appearance="primary"
                disabled={busy || !secretValue.trim()}
                onClick={async () => {
                  if (!secretDialogEndpoint) return;
                  setBusy(true);
                  setError(null);
                  try {
                    await setEndpointSecret(name, secretDialogEndpoint.id, secretValue);
                    setSecretDialogEndpoint(null);
                  } catch (err) {
                    setError(err);
                  } finally {
                    setBusy(false);
                  }
                }}
              >
                Save
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </PageContainer>
  );
}

function EndpointRow({
  endpoint,
  deploymentName,
  onChanged,
  onError,
  onEdit,
  onSetSecret,
}: {
  endpoint: Endpoint;
  deploymentName: string;
  onChanged: () => void;
  onError: (error: unknown) => void;
  onEdit: () => void;
  onSetSecret: () => void;
}) {
  return (
    <TableRow>
      <TableCell>
        <TableCellLayout truncate>{endpoint.baseUrl}</TableCellLayout>
      </TableCell>
      <TableCell>{endpoint.apiFamily}</TableCell>
      <TableCell>{endpoint.priority}</TableCell>
      <TableCell>{endpoint.authMode}</TableCell>
      <TableCell>
        <Switch
          checked={endpoint.enabled}
          onChange={async (_, data) => {
            try {
              await updateEndpoint(deploymentName, endpoint.id, { enabled: data.checked });
              onChanged();
            } catch (err) {
              onError(err);
            }
          }}
        />
      </TableCell>
      <TableCell>
        <Badge color={endpoint.health === 'Unhealthy' ? 'danger' : endpoint.health === 'Healthy' ? 'success' : 'informative'}>
          {endpoint.health}
        </Badge>
      </TableCell>
      <TableCell>{endpoint.timeoutSeconds}s</TableCell>
      <TableCell>{endpoint.maxRetries}</TableCell>
      <TableCell>
        <Button appearance="subtle" icon={<EditRegular />} title="Edit endpoint" aria-label="Edit endpoint" onClick={onEdit} />
        {endpoint.authMode === 'ApiKey' && (
          <Button
            appearance="subtle"
            icon={<KeyRegular />}
            onClick={onSetSecret}
            title="Set / replace API key"
            aria-label="Set / replace API key"
          />
        )}
        <ConfirmDialog
          trigger={<Button appearance="subtle" icon={<DeleteRegular />} />}
          title="Remove endpoint?"
          message={`Remove '${endpoint.baseUrl}' from this deployment. This cannot be undone.`}
          onConfirm={async () => {
            await removeEndpoint(deploymentName, endpoint.id);
            onChanged();
          }}
        />
      </TableCell>
    </TableRow>
  );
}
