import { useEffect, useState } from 'react';
import {
  Button,
  Badge,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  Field,
  Input,
  makeStyles,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableCellLayout,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  tokens,
  Tooltip,
} from '@fluentui/react-components';
import {
  AddRegular,
  ArrowRotateClockwiseRegular,
  DeleteRegular,
  EditRegular,
  KeyRegular,
} from '@fluentui/react-icons';
import {
  activateEndpointCredential,
  createEndpoint,
  deleteEndpoint,
  disableEndpointCredentialFallback,
  listEndpoints,
  removeEndpointCredential,
  setEndpointCredential,
  updateEndpointConfig,
  type CreateEndpointInput,
} from '../../api/endpoints';
import type { AuthMode, EndpointConfig, EndpointCredentialSlot } from '../../api/types';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { PageContainer, PageHeader } from '../../components/PageLayout';

const authModes: AuthMode[] = ['ApiKey', 'ManagedIdentity'];
// Mirrors Endpoint.MaxTimeoutSeconds on the server, which rejects values outside 1-3600.
export const MAX_TIMEOUT_SECONDS = 3600;

function isValidTimeout(seconds: number) {
  return Number.isInteger(seconds) && seconds >= 1 && seconds <= MAX_TIMEOUT_SECONDS;
}

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

const useStyles = makeStyles({
  tableViewport: {
    overflowX: 'auto',
  },
  table: {
    minWidth: '900px',
    tableLayout: 'fixed',
  },
  authCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  authType: {
    color: tokens.colorNeutralForeground3,
  },
  slotRow: {
    display: 'grid',
    // max-content (not auto) keeps this column from soaking up the row's leftover width and stretching the badge.
    gridTemplateColumns: '72px 76px max-content',
    alignItems: 'center',
    minHeight: '22px',
    columnGap: tokens.spacingHorizontalXS,
  },
  slotName: {
    fontWeight: tokens.fontWeightSemibold,
  },
  slotEmpty: {
    color: tokens.colorNeutralForeground3,
  },
  fallback: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    whiteSpace: 'nowrap',
  },
});

export function EndpointsPage() {
  const styles = useStyles();
  const [endpoints, setEndpoints] = useState<EndpointConfig[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [newEndpoint, setNewEndpoint] = useState<CreateEndpointInput>(emptyEndpoint);
  const [editing, setEditing] = useState<EndpointConfig | null>(null);
  const [settingCredential, setSettingCredential] = useState<{ endpoint: EndpointConfig; slot: EndpointCredentialSlot } | null>(null);
  const [activating, setActivating] = useState<EndpointConfig | null>(null);
  const [activationSlot, setActivationSlot] = useState<EndpointCredentialSlot>('Secondary');
  const [fallbackMinutes, setFallbackMinutes] = useState('0');
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
        <div className={styles.tableViewport}>
          <Table className={styles.table}>
            <TableHeader>
              <TableRow>
                <TableHeaderCell style={{ width: '15%' }}>Name</TableHeaderCell>
                <TableHeaderCell style={{ width: '24%' }}>Base URL</TableHeaderCell>
                <TableHeaderCell style={{ width: '33%' }}>Authentication</TableHeaderCell>
                <TableHeaderCell style={{ width: '8%' }}>Timeout</TableHeaderCell>
                <TableHeaderCell style={{ width: '7%' }}>Retries</TableHeaderCell>
                <TableHeaderCell style={{ width: '13%' }}>Actions</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {endpoints.map(endpoint => (
                <TableRow key={endpoint.id}>
                  <TableCell><TableCellLayout>{endpoint.name}</TableCellLayout></TableCell>
                  <TableCell title={endpoint.baseUrl}><TableCellLayout truncate>{endpoint.baseUrl}</TableCellLayout></TableCell>
                  <TableCell>
                    {endpoint.authMode === 'ApiKey' ? (
                      <CredentialStatus endpoint={endpoint} styles={styles} />
                    ) : (
                      <Badge appearance="tint" color="informative">Managed identity</Badge>
                    )}
                  </TableCell>
                  <TableCell>{endpoint.timeoutSeconds}s</TableCell>
                  <TableCell>{endpoint.maxRetries}</TableCell>
                  <TableCell>
                    <div className={styles.actions}>
                      <Tooltip content="Edit endpoint" relationship="label">
                        <Button appearance="subtle" icon={<EditRegular />} aria-label={`Edit ${endpoint.name}`} onClick={() => setEditing(endpoint)} />
                      </Tooltip>
                      {endpoint.authMode === 'ApiKey' && (
                        <Menu>
                          <MenuTrigger disableButtonEnhancement>
                            <Tooltip content="Manage credentials" relationship="label">
                              <Button appearance="subtle" icon={<KeyRegular />} aria-label={`Manage credentials for ${endpoint.name}`} />
                            </Tooltip>
                          </MenuTrigger>
                          <MenuPopover>
                            <MenuList>
                              <MenuItem icon={<ArrowRotateClockwiseRegular />} onClick={() => {
                                setSecret('');
                                setSettingCredential({ endpoint, slot: 'Primary' });
                              }}>Set Primary</MenuItem>
                              <MenuItem icon={<ArrowRotateClockwiseRegular />} onClick={() => {
                                setSecret('');
                                setSettingCredential({ endpoint, slot: 'Secondary' });
                              }}>Set Secondary</MenuItem>
                              {endpoint.hasSecondarySecret && (
                                <MenuItem onClick={() => {
                                  setActivationSlot(endpoint.activeCredentialSlot === 'Primary' ? 'Secondary' : 'Primary');
                                  setFallbackMinutes('0');
                                  setActivating(endpoint);
                                }}>Activate credential</MenuItem>
                              )}
                              {endpoint.fallbackCredentialSlot && (
                                <MenuItem onClick={() => run(
                                  () => disableEndpointCredentialFallback(endpoint.id),
                                  () => undefined,
                                )}>Stop fallback</MenuItem>
                              )}
                              {endpoint.hasSecondarySecret && endpoint.activeCredentialSlot !== 'Secondary' && endpoint.fallbackCredentialSlot !== 'Secondary' && (
                                <MenuItem onClick={() => run(
                                  () => removeEndpointCredential(endpoint.id, 'Secondary'),
                                  () => undefined,
                                )}>Remove Secondary</MenuItem>
                              )}
                            </MenuList>
                          </MenuPopover>
                        </Menu>
                      )}
                      <Tooltip content="Delete endpoint" relationship="label">
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
                      </Tooltip>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
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
                disabled={busy || !editing?.name.trim() || !editing.baseUrl.trim() || !isValidTimeout(editing.timeoutSeconds) || editing.maxRetries < 0}
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

      <Dialog open={settingCredential !== null} onOpenChange={(_, data) => !data.open && setSettingCredential(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Set {settingCredential?.slot} credential</DialogTitle>
            <DialogContent>
              <Field label="New API key">
                <Input type="password" value={secret} onChange={(_, data) => setSecret(data.value)} />
              </Field>
              <ErrorBanner error={error} />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setSettingCredential(null)}>Cancel</Button>
              <Button
                appearance="primary"
                disabled={busy || !secret}
                onClick={() => settingCredential && run(
                  () => setEndpointCredential(settingCredential.endpoint.id, settingCredential.slot, secret),
                  () => setSettingCredential(null),
                )}
              >
                Rotate
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog open={activating !== null} onOpenChange={(_, data) => !data.open && setActivating(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Activate credential</DialogTitle>
            <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <Field label="Active slot">
                <select value={activationSlot} onChange={event => setActivationSlot(event.target.value as EndpointCredentialSlot)}>
                  <option value="Primary">Primary</option>
                  <option value="Secondary">Secondary</option>
                </select>
              </Field>
              <Field label="Fallback window (minutes)" hint="Use 0 for immediate cutover; the server enforces its configured maximum.">
                <Input type="number" min={0} value={fallbackMinutes} onChange={(_, data) => setFallbackMinutes(data.value)} />
              </Field>
              <ErrorBanner error={error} />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setActivating(null)}>Cancel</Button>
              <Button
                appearance="primary"
                disabled={busy || Number(fallbackMinutes) < 0}
                onClick={() => activating && run(
                  () => activateEndpointCredential(
                    activating.id,
                    activationSlot,
                    Number(fallbackMinutes) > 0
                      ? new Date(Date.now() + Number(fallbackMinutes) * 60_000).toISOString()
                      : undefined,
                  ),
                  () => setActivating(null),
                )}
              >Activate</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </PageContainer>
  );
}

function CredentialStatus({ endpoint, styles }: { endpoint: EndpointConfig; styles: ReturnType<typeof useStyles> }) {
  const fallbackExpired = endpoint.fallbackExpiresAt && new Date(endpoint.fallbackExpiresAt) <= new Date();

  return (
    <div className={styles.authCell}>
      <Text size={200} className={styles.authType}>API key</Text>
      <CredentialSlotStatus
        name="Primary"
        configured={endpoint.hasPrimarySecret}
        active={endpoint.activeCredentialSlot === 'Primary'}
        styles={styles}
      />
      <CredentialSlotStatus
        name="Secondary"
        configured={endpoint.hasSecondarySecret}
        active={endpoint.activeCredentialSlot === 'Secondary'}
        styles={styles}
      />
      {endpoint.fallbackCredentialSlot && endpoint.fallbackExpiresAt && (
        <Text className={styles.fallback}>
          Fallback: {endpoint.fallbackCredentialSlot} {fallbackExpired ? 'expired' : 'until'}{' '}
          {new Date(endpoint.fallbackExpiresAt).toLocaleString()}
        </Text>
      )}
    </div>
  );
}

function CredentialSlotStatus({
  name,
  configured,
  active,
  styles,
}: {
  name: EndpointCredentialSlot;
  configured: boolean;
  active: boolean;
  styles: ReturnType<typeof useStyles>;
}) {
  return (
    <div className={styles.slotRow}>
      <span className={styles.slotName}>{name}</span>
      <span className={configured ? undefined : styles.slotEmpty}>{configured ? 'Configured' : 'Empty'}</span>
      {active && <Badge size="small" appearance="filled" color="success">Active</Badge>}
    </div>
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
      <Field
        label="Timeout (seconds)"
        hint={`1 to ${MAX_TIMEOUT_SECONDS}`}
        validationState={isValidTimeout(value.timeoutSeconds) ? 'none' : 'error'}
        validationMessage={isValidTimeout(value.timeoutSeconds) ? undefined : `Enter a whole number from 1 to ${MAX_TIMEOUT_SECONDS}.`}
      >
        <Input type="number" min={1} max={MAX_TIMEOUT_SECONDS} value={String(value.timeoutSeconds)} onChange={(_, data) => update('timeoutSeconds', (Number(data.value) || 0) as T['timeoutSeconds'])} />
      </Field>
      <Field label="Maximum retries"><Input type="number" value={String(value.maxRetries)} onChange={(_, data) => update('maxRetries', (Number(data.value) || 0) as T['maxRetries'])} /></Field>
    </>
  );
}

function canCreate(endpoint: CreateEndpointInput) {
  return endpoint.name.trim() !== ''
    && endpoint.baseUrl.trim() !== ''
    && isValidTimeout(endpoint.timeoutSeconds)
    && endpoint.maxRetries >= 0
    && (endpoint.authMode !== 'ApiKey' || Boolean(endpoint.apiKeyPlaintext))
    && (endpoint.authMode !== 'ManagedIdentity' || Boolean(endpoint.managedIdentityScope?.trim()));
}
