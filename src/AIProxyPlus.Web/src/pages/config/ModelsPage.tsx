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
import { ModelCapabilityPicker } from '../../components/ModelCapabilityPicker';
import { createModel, deleteModel, listModels } from '../../api/models';
import type { Model, ModelCapability } from '../../api/types';

function healthSummary(model: Model): string {
  if (model.endpoints.length === 0) {
    return 'No endpoints';
  }

  const unhealthy = model.endpoints.filter((e) => e.health === 'Unhealthy').length;
  return unhealthy > 0 ? `${unhealthy}/${model.endpoints.length} unhealthy` : `${model.endpoints.length} healthy`;
}

export function ModelsPage() {
  const navigate = useNavigate();
  const [models, setModels] = useState<Model[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [newCapabilities, setNewCapabilities] = useState<ModelCapability[]>(['Chat']);
  const [creating, setCreating] = useState(false);

  const reload = () => {
    setError(null);
    listModels().then(setModels).catch(setError);
  };

  useEffect(reload, []);

  return (
    <PageContainer>
      <PageHeader
        title="Models"
        actions={
          <Dialog open={createOpen} onOpenChange={(_, data) => setCreateOpen(data.open)}>
            <DialogTrigger disableButtonEnhancement>
              <Button appearance="primary" icon={<AddRegular />}>
                New model
              </Button>
            </DialogTrigger>
            <DialogSurface>
              <DialogBody>
                <DialogTitle>New model</DialogTitle>
                <DialogContent>
                  <Field label="Name" hint="The routing key clients send as 'model' (e.g. gpt-4o).">
                    <Input value={newName} onChange={(_, data) => setNewName(data.value)} />
                  </Field>
                  <ModelCapabilityPicker value={newCapabilities} onChange={setNewCapabilities} />
                  <ErrorBanner error={error} />
                </DialogContent>
                <DialogActions>
                  <DialogTrigger disableButtonEnhancement>
                    <Button appearance="secondary">Cancel</Button>
                  </DialogTrigger>
                  <Button
                    appearance="primary"
                    disabled={creating || !newName.trim() || newCapabilities.length === 0}
                    icon={creating ? <Spinner size="tiny" /> : undefined}
                    onClick={async () => {
                      setCreating(true);
                      setError(null);
                      try {
                        // RoutingStrategy has only one value today (architecture.md §16) — nothing to pick yet.
                        await createModel(newName.trim(), 'Priority', newCapabilities);
                        setNewName('');
                        setNewCapabilities(['Chat']);
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

      {models === null && <Spinner label="Loading models…" />}

      {models !== null && models.length === 0 && <p>No models yet — create one to get started.</p>}

      {models !== null && models.length > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Strategy</TableHeaderCell>
              <TableHeaderCell>Capabilities</TableHeaderCell>
              <TableHeaderCell>Endpoints</TableHeaderCell>
              <TableHeaderCell>Actions</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {models.map((model) => (
              <TableRow key={model.id} onClick={() => navigate(`/config/models/${model.name}`)}>
                <TableCell>
                  <TableCellLayout>{model.name}</TableCellLayout>
                </TableCell>
                <TableCell>{model.strategy}</TableCell>
                <TableCell>
                  {model.capabilities.map((capability) => (
                    <Badge key={capability} appearance="outline">{capability}</Badge>
                  ))}
                </TableCell>
                <TableCell>
                  <Badge color={model.endpoints.some((e) => e.health === 'Unhealthy') ? 'danger' : 'success'}>
                    {healthSummary(model)}
                  </Badge>
                </TableCell>
                <TableCell>
                  <ConfirmDialog
                    trigger={
                      <Button
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        aria-label={`Delete ${model.name}`}
                        onClick={(event) => event.stopPropagation()}
                      />
                    }
                    title={`Delete '${model.name}'?`}
                    message="This also deletes every endpoint configured under this model. This cannot be undone."
                    onConfirm={async () => {
                      await deleteModel(model.name);
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
