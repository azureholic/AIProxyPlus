import { useEffect, useState } from 'react';
import {
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
import { AddRegular, ArrowRotateClockwiseRegular, DeleteRegular, EditRegular } from '@fluentui/react-icons';
import {
  createEndpoint,
  deleteEndpoint,
  listEndpoints,
  setEndpointSecret,
  updateEndpointConfig,
  type CreateEndpointInput,
} from '../../api/endpoints';
import type { AuthMode, EndpointConfig } from '../../api/types';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { PageContainer, PageHeader } from '../../components/PageLayout';

const authModes: AuthMode[] = ['ApiKey', 'ManagedIdentity'];
const emptyEndpoint: CreateEndpointInput = {
  name: '',
  baseUrl: '',
  authMode: 'ApiKey',
  apiKeyPlaintext: '',
  managedIdentityScope: '',
  managedIdentityTenantId: '',
  timeoutSeconds: 100,
  maxRetries: 2,
};

export function EndpointsPage() {
  const [endpoints, setEndpoints] = useState<EndpointConfig[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [newEndpoint, setNewEndpoint] = useState<CreateEndpointInput>(emptyEndpoint);
  const [editing, setEditing] = useState<EndpointConfig | null>(null);
  const [rotating, setRotating] = useState<EndpointConfig | null>(null);
  const [secret, setSecret] = useState('');
  const [busy, setBusy] = useState(false);

  const reload = () => {
    setError(null);
    listEndpoints().then(setEndpoints).catch(setError);
  };

  useEffect(reload, []);

  const run = async (action: () => Promise<unknown>, onSuccess: () => void) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      onSuccess();
      reload();
    } catch (caught) {
      setError(caught);
    } finally {
      setBusy(false);
    }
  };

  return (
    <PageContainer>
      <PageHeader
        title="Endpoints"
        actions={
          <Dialog open={createOpen} onOpenChange={(_, data) => setCreateOpen(data.open)}>
            <DialogTrigger disableButtonEnhancement>
              <Button appearance="primary" icon={<AddRegular />} onClick={() => setNewEndpoint({ ...emptyEndpoint })}>
                New endpoint
              </Button>
            </DialogTrigger>
            <DialogSurface>
              <DialogBody>
                <DialogTitle>New endpoint</DialogTitle>
                <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                  <EndpointFields value={newEndpoint} onChange={setNewEndpoint} includeIdentity />
                  <ErrorBanner error={error} />
                </DialogContent>
                <DialogActions>
                  <DialogTrigger disableButtonEnhancement><Button appearance="secondary">Cancel</Button></DialogTrigger>
                  <Button
                    appearance="primary"
                    disabled={busy || !canCreate(newEndpoint)}
                    icon={busy ? <Spinner size="tiny" /> : undefined}
                    onClick={() => run(
                      () => createEndpoint({
                        ...newEndpoint,
                        apiKeyPlaintext: newEndpoint.authMode === 'ApiKey' ? newEndpoint.apiKeyPlaintext : undefined,
                        managedIdentityScope: newEndpoint.authMode === 'ManagedIdentity' ? newEndpoint.managedIdentityScope : undefined,
                        managedIdentityTenantId: newEndpoint.authMode === 'ManagedIdentity' ? newEndpoint.managedIdentityTenantId || undefined : undefined,
                      }),
                      () => setCreateOpen(false),
                    )}
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
      {endpoints === null && <Spinner label="Loading endpoints..." />}
      {endpoints !== null && endpoints.length === 0 && <p>No endpoints yet. Create one before assigning it to a model.</p>}
      {endpoints !== null && endpoints.length > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Base URL</TableHeaderCell>
              <TableHeaderCell>Authentication</TableHeaderCell>
              <TableHeaderCell>Timeout</TableHeaderCell>
              <TableHeaderCell>Retries</TableHeaderCell>
              <TableHeaderCell>Actions</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {endpoints.map(endpoint => (
              <TableRow key={endpoint.id}>
                <TableCell><TableCellLayout>{endpoint.name}</TableCellLayout></TableCell>
                <TableCell><TableCellLayout truncate>{endpoint.baseUrl}</TableCellLayout></TableCell>
                <TableCell>{endpoint.authMode === 'ApiKey' ? 'API key' : 'Managed identity'}</TableCell>
                <TableCell>{endpoint.timeoutSeconds}s</TableCell>
                <TableCell>{endpoint.maxRetries}</TableCell>
                <TableCell>
                  <Button appearance="subtle" icon={<EditRegular />} aria-label={`Edit ${endpoint.name}`} onClick={() => setEditing(endpoint)} />
                  {endpoint.authMode === 'ApiKey' && (
                    <Button
                      appearance="subtle"
                      icon={<ArrowRotateClockwiseRegular />}
                      aria-label={`Rotate API key for ${endpoint.name}`}
                      onClick={() => {
                        setSecret('');
                        setRotating(endpoint);
                      }}
                    />
                  )}
                  <ConfirmDialog
                    trigger={<Button appearance="subtle" icon={<DeleteRegular />} aria-label={`Delete ${endpoint.name}`} />}
                    title={`Delete '${endpoint.name}'?`}
                    message="The endpoint can only be deleted when it is not assigned to any model."
                    confirmLabel="Delete"
                    onConfirm={async () => {
                      await deleteEndpoint(endpoint.id);
                      reload();
                    }}
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <Dialog open={editing !== null} onOpenChange={(_, data) => !data.open && setEditing(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Edit endpoint</DialogTitle>
            <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {editing && <EndpointFields value={editing} onChange={setEditing} />}
              <ErrorBanner error={error} />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setEditing(null)}>Cancel</Button>
              <Button
                appearance="primary"
                disabled={busy || !editing?.name.trim() || !editing.baseUrl.trim() || editing.timeoutSeconds < 1 || editing.maxRetries < 0}
                onClick={() => editing && run(
                  () => updateEndpointConfig(editing.id, {
                    name: editing.name.trim(),
                    baseUrl: editing.baseUrl.trim(),
                    managedIdentityScope: editing.authMode === 'ManagedIdentity' ? editing.managedIdentityScope ?? undefined : undefined,
                    managedIdentityTenantId: editing.authMode === 'ManagedIdentity' ? editing.managedIdentityTenantId ?? undefined : undefined,
                    timeoutSeconds: editing.timeoutSeconds,
                    maxRetries: editing.maxRetries,
                  }),
                  () => setEditing(null),
                )}
              >
                Save
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog open={rotating !== null} onOpenChange={(_, data) => !data.open && setRotating(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Rotate API key</DialogTitle>
            <DialogContent>
              <Field label="New API key">
                <Input type="password" value={secret} onChange={(_, data) => setSecret(data.value)} />
              </Field>
              <ErrorBanner error={error} />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setRotating(null)}>Cancel</Button>
              <Button
                appearance="primary"
                disabled={busy || !secret}
                onClick={() => rotating && run(() => setEndpointSecret(rotating.id, secret), () => setRotating(null))}
              >
                Rotate
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </PageContainer>
  );
}

function EndpointFields<T extends CreateEndpointInput | EndpointConfig>({
  value,
  onChange,
  includeIdentity = false,
}: {
  value: T;
  onChange: (value: T) => void;
  includeIdentity?: boolean;
}) {
  const update = <K extends keyof T>(key: K, nextValue: T[K]) => onChange({ ...value, [key]: nextValue });

  return (
    <>
      <Field label="Name"><Input value={value.name} onChange={(_, data) => update('name', data.value)} /></Field>
      <Field label="Base URL"><Input value={value.baseUrl} onChange={(_, data) => update('baseUrl', data.value)} /></Field>
      {includeIdentity && (
        <>
          <Field label="Authentication">
            <select
              value={value.authMode}
              onChange={(event) => update('authMode', event.target.value as T['authMode'])}
            >
              {authModes.map(mode => <option key={mode} value={mode}>{mode === 'ApiKey' ? 'API key' : 'Managed identity'}</option>)}
            </select>
          </Field>
        </>
      )}
      {value.authMode === 'ApiKey' && includeIdentity && (
        <Field label="API key"><Input type="password" value={'apiKeyPlaintext' in value ? value.apiKeyPlaintext : ''} onChange={(_, data) => update('apiKeyPlaintext' as keyof T, data.value as T[keyof T])} /></Field>
      )}
      {value.authMode === 'ManagedIdentity' && (
        <>
          <Field label="Managed identity scope"><Input value={value.managedIdentityScope ?? ''} onChange={(_, data) => update('managedIdentityScope', data.value)} /></Field>
          <Field label="Tenant ID"><Input value={value.managedIdentityTenantId ?? ''} onChange={(_, data) => update('managedIdentityTenantId', data.value)} /></Field>
        </>
      )}
      <Field label="Timeout (seconds)"><Input type="number" value={String(value.timeoutSeconds)} onChange={(_, data) => update('timeoutSeconds', (Number(data.value) || 0) as T['timeoutSeconds'])} /></Field>
      <Field label="Maximum retries"><Input type="number" value={String(value.maxRetries)} onChange={(_, data) => update('maxRetries', (Number(data.value) || 0) as T['maxRetries'])} /></Field>
    </>
  );
}

function canCreate(endpoint: CreateEndpointInput) {
  return endpoint.name.trim() !== ''
    && endpoint.baseUrl.trim() !== ''
    && endpoint.timeoutSeconds >= 1
    && endpoint.maxRetries >= 0
    && (endpoint.authMode !== 'ApiKey' || Boolean(endpoint.apiKeyPlaintext))
    && (endpoint.authMode !== 'ManagedIdentity' || Boolean(endpoint.managedIdentityScope?.trim()));
}
