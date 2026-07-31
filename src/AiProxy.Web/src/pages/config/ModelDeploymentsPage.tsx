import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
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
import { AddRegular, DeleteRegular } from '@fluentui/react-icons';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { createModelDeployment, deleteModelDeployment, listModelDeployments } from '../../api/modelDeployments';
import type { ModelDeployment } from '../../api/types';

function healthSummary(deployment: ModelDeployment): string {
  if (deployment.endpoints.length === 0) {
    return 'No endpoints';
  }

  const unhealthy = deployment.endpoints.filter((e) => e.health === 'Unhealthy').length;
  return unhealthy > 0 ? `${unhealthy}/${deployment.endpoints.length} unhealthy` : `${deployment.endpoints.length} healthy`;
}

export function ModelDeploymentsPage() {
  const navigate = useNavigate();
  const [deployments, setDeployments] = useState<ModelDeployment[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [creating, setCreating] = useState(false);

  const reload = () => {
    setError(null);
    listModelDeployments().then(setDeployments).catch(setError);
  };

  useEffect(reload, []);

  return (
    <PageContainer>
      <PageHeader
        title="Model Deployments"
        actions={
          <Dialog open={createOpen} onOpenChange={(_, data) => setCreateOpen(data.open)}>
            <DialogTrigger disableButtonEnhancement>
              <Button appearance="primary" icon={<AddRegular />}>
                New deployment
              </Button>
            </DialogTrigger>
            <DialogSurface>
              <DialogBody>
                <DialogTitle>New model deployment</DialogTitle>
                <DialogContent>
                  <Field label="Name" hint="The routing key clients send as 'model' (e.g. gpt-4o).">
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
                    disabled={creating || !newName.trim()}
                    icon={creating ? <Spinner size="tiny" /> : undefined}
                    onClick={async () => {
                      setCreating(true);
                      setError(null);
                      try {
                        // RoutingStrategy has only one value today (architecture.md §16) — nothing to pick yet.
                        await createModelDeployment(newName.trim(), 'Priority');
                        setNewName('');
                        setCreateOpen(false);
                        reload();
                      } catch (err) {
                        setError(err);
                      } finally {
                        setCreating(false);
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

      {deployments === null && <Spinner label="Loading deployments…" />}

      {deployments !== null && deployments.length === 0 && <p>No model deployments yet — create one to get started.</p>}

      {deployments !== null && deployments.length > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Strategy</TableHeaderCell>
              <TableHeaderCell>Endpoints</TableHeaderCell>
              <TableHeaderCell>Actions</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {deployments.map((deployment) => (
              <TableRow key={deployment.id} onClick={() => navigate(`/config/model-deployments/${deployment.name}`)}>
                <TableCell>
                  <TableCellLayout>{deployment.name}</TableCellLayout>
                </TableCell>
                <TableCell>{deployment.strategy}</TableCell>
                <TableCell>
                  <Badge color={deployment.endpoints.some((e) => e.health === 'Unhealthy') ? 'danger' : 'success'}>
                    {healthSummary(deployment)}
                  </Badge>
                </TableCell>
                <TableCell>
                  <ConfirmDialog
                    trigger={
                      <Button
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        aria-label={`Delete ${deployment.name}`}
                        onClick={(event) => event.stopPropagation()}
                      />
                    }
                    title={`Delete '${deployment.name}'?`}
                    message="This also deletes every endpoint configured under this deployment. This cannot be undone."
                    onConfirm={async () => {
                      await deleteModelDeployment(deployment.name);
                      reload();
                    }}
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </PageContainer>
  );
}
