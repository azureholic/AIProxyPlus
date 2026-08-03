import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Badge,
  Body1,
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
  Select,
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
import { AddRegular, ArrowLeftRegular, DeleteRegular, EditRegular } from '@fluentui/react-icons';
import { listEndpoints } from '../../api/endpoints';
import { addEndpoint, getModelDeployment, removeEndpoint, updateEndpoint } from '../../api/modelDeployments';
import type { Endpoint, EndpointConfig, ModelDeployment } from '../../api/types';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { PageContainer, PageHeader } from '../../components/PageLayout';

export function ModelDeploymentDetailPage() {
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [deployment, setDeployment] = useState<ModelDeployment | null>(null);
  const [availableEndpoints, setAvailableEndpoints] = useState<EndpointConfig[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [selectedEndpointId, setSelectedEndpointId] = useState('');
  const [priority, setPriority] = useState(1);
  const [enabled, setEnabled] = useState(true);
  const [editing, setEditing] = useState<Endpoint | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = () => {
    if (!name) return;
    setError(null);
    Promise.all([getModelDeployment(name), listEndpoints()])
      .then(([nextDeployment, endpoints]) => {
        setDeployment(nextDeployment);
        setAvailableEndpoints(endpoints.filter(endpoint =>
          !nextDeployment.endpoints.some(assignment => assignment.id === endpoint.id)));
      })
      .catch(setError);
  };

  useEffect(reload, [name]);

  if (!name) return null;

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
      {deployment === null && error === null && <Spinner label="Loading deployment..." />}

      {deployment !== null && (
        <>
          <Body1>
            Strategy: <strong>{deployment.strategy}</strong>. Priority and enabled state apply only to this deployment.
          </Body1>
          <PageHeader
            title="Endpoint assignments"
            actions={
              <Dialog open={addOpen} onOpenChange={(_, data) => setAddOpen(data.open)}>
                <DialogTrigger disableButtonEnhancement>
                  <Button
                    appearance="primary"
                    icon={<AddRegular />}
                    onClick={() => {
                      setSelectedEndpointId('');
                      setPriority(1);
                      setEnabled(true);
                    }}
                  >
                    Assign endpoint
                  </Button>
                </DialogTrigger>
                <DialogSurface>
                  <DialogBody>
                    <DialogTitle>Assign endpoint</DialogTitle>
                    <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                      <Field label="Endpoint">
                        <Select
                          value={selectedEndpointId}
                          onChange={(_, data) => setSelectedEndpointId(data.value)}
                        >
                          <option value="">Choose an endpoint</option>
                          {availableEndpoints.map(endpoint => (
                            <option key={endpoint.id} value={endpoint.id}>
                              {endpoint.name} ({endpoint.baseUrl})
                            </option>
                          ))}
                        </Select>
                      </Field>
                      {availableEndpoints.length === 0 && (
                        <Body1>No unassigned endpoints are available. Create one in Config &gt; Endpoints.</Body1>
                      )}
                      <Field label="Priority" hint="Endpoints sharing the lowest priority round-robin together.">
                        <Input type="number" value={String(priority)} onChange={(_, data) => setPriority(Number(data.value) || 0)} />
                      </Field>
                      <Switch checked={enabled} onChange={(_, data) => setEnabled(data.checked)} label="Enabled" />
                    </DialogContent>
                    <DialogActions>
                      <DialogTrigger disableButtonEnhancement>
                        <Button appearance="secondary">Cancel</Button>
                      </DialogTrigger>
                      <Button
                        appearance="primary"
                        disabled={busy || !selectedEndpointId || priority < 1}
                        icon={busy ? <Spinner size="tiny" /> : undefined}
                        onClick={async () => {
                          setBusy(true);
                          setError(null);
                          try {
                            await addEndpoint(name, { endpointId: selectedEndpointId, priority, enabled });
                            setAddOpen(false);
                            reload();
                          } catch (caught) {
                            setError(caught);
                          } finally {
                            setBusy(false);
                          }
                        }}
                      >
                        Assign
                      </Button>
                    </DialogActions>
                  </DialogBody>
                </DialogSurface>
              </Dialog>
            }
          />

          {deployment.endpoints.length === 0 && <p>No endpoints assigned yet.</p>}
          {deployment.endpoints.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHeaderCell>Name</TableHeaderCell>
                  <TableHeaderCell>Base URL</TableHeaderCell>
                  <TableHeaderCell>API family</TableHeaderCell>
                  <TableHeaderCell>Priority</TableHeaderCell>
                  <TableHeaderCell>Enabled</TableHeaderCell>
                  <TableHeaderCell>Health</TableHeaderCell>
                  <TableHeaderCell>Actions</TableHeaderCell>
                </TableRow>
              </TableHeader>
              <TableBody>
                {deployment.endpoints.map(endpoint => (
                  <TableRow key={endpoint.id}>
                    <TableCell><TableCellLayout>{endpoint.name}</TableCellLayout></TableCell>
                    <TableCell><TableCellLayout truncate>{endpoint.baseUrl}</TableCellLayout></TableCell>
                    <TableCell>{endpoint.apiFamily}</TableCell>
                    <TableCell>{endpoint.priority}</TableCell>
                    <TableCell><Badge color={endpoint.enabled ? 'success' : 'warning'}>{endpoint.enabled ? 'Yes' : 'No'}</Badge></TableCell>
                    <TableCell>
                      <Badge color={endpoint.health === 'Unhealthy' ? 'danger' : endpoint.health === 'Healthy' ? 'success' : 'informative'}>
                        {endpoint.health}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Button appearance="subtle" icon={<EditRegular />} aria-label={`Edit ${endpoint.name} assignment`} onClick={() => setEditing(endpoint)} />
                      <ConfirmDialog
                        trigger={<Button appearance="subtle" icon={<DeleteRegular />} aria-label={`Detach ${endpoint.name}`} />}
                        title={`Detach '${endpoint.name}'?`}
                        message="This removes the assignment from this deployment. The reusable endpoint remains available."
                        confirmLabel="Detach"
                        onConfirm={async () => {
                          await removeEndpoint(name, endpoint.id);
                          reload();
                        }}
                      />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </>
      )}

      <Dialog open={editing !== null} onOpenChange={(_, data) => !data.open && setEditing(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Edit assignment</DialogTitle>
            <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <Field label="Endpoint"><Input value={editing?.name ?? ''} disabled /></Field>
              <Field label="Priority">
                <Input
                  type="number"
                  value={String(editing?.priority ?? 1)}
                  onChange={(_, data) => setEditing(current => current ? { ...current, priority: Number(data.value) || 0 } : null)}
                />
              </Field>
              <Switch
                checked={editing?.enabled ?? false}
                onChange={(_, data) => setEditing(current => current ? { ...current, enabled: data.checked } : null)}
                label="Enabled"
              />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setEditing(null)}>Cancel</Button>
              <Button
                appearance="primary"
                disabled={busy || editing === null || editing.priority < 1}
                onClick={async () => {
                  if (!editing) return;
                  setBusy(true);
                  setError(null);
                  try {
                    await updateEndpoint(name, editing.id, { priority: editing.priority, enabled: editing.enabled });
                    setEditing(null);
                    reload();
                  } catch (caught) {
                    setError(caught);
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
