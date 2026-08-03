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
  Select,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableCellLayout,
  TableHeader,
  TableHeaderCell,
  TableRow,
} from '@fluentui/react-components';
import { AddRegular, DeleteRegular, EditRegular } from '@fluentui/react-icons';
import {
  createApiRoute,
  deleteApiRoute,
  listApiRoutes,
  updateApiRoute,
  type ApiRouteInput,
} from '../../api/apiRoutes';
import type { ApiRoute, Provider } from '../../api/types';
import { ErrorBanner } from '../../components/Banners';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { PageContainer, PageHeader } from '../../components/PageLayout';

const providers: Provider[] = ['OpenAI', 'MicrosoftAI', 'Anthropic'];
const emptyRoute: ApiRouteInput = { name: '', provider: 'OpenAI', pathPrefix: '/v1' };

export function ApiRoutesPage() {
  const [routes, setRoutes] = useState<ApiRoute[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [draft, setDraft] = useState<ApiRouteInput>(emptyRoute);
  const [editing, setEditing] = useState<ApiRoute | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = () => {
    setError(null);
    listApiRoutes().then(setRoutes).catch(setError);
  };

  useEffect(reload, []);

  const save = async (action: () => Promise<ApiRoute>, onSuccess: () => void) => {
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
        title="API Routes"
        actions={
          <Dialog open={createOpen} onOpenChange={(_, data) => setCreateOpen(data.open)}>
            <DialogTrigger disableButtonEnhancement>
              <Button appearance="primary" icon={<AddRegular />} onClick={() => setDraft({ ...emptyRoute })}>
                New API route
              </Button>
            </DialogTrigger>
            <DialogSurface>
              <DialogBody>
                <DialogTitle>New API route</DialogTitle>
                <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                  <ApiRouteFields value={draft} onChange={setDraft} />
                  <ErrorBanner error={error} />
                </DialogContent>
                <DialogActions>
                  <DialogTrigger disableButtonEnhancement><Button appearance="secondary">Cancel</Button></DialogTrigger>
                  <Button
                    appearance="primary"
                    disabled={busy || !isValid(draft)}
                    icon={busy ? <Spinner size="tiny" /> : undefined}
                    onClick={() => save(() => createApiRoute(normalize(draft)), () => setCreateOpen(false))}
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
      {routes === null && <Spinner label="Loading API routes..." />}
      {routes !== null && routes.length === 0 && <p>No API routes yet.</p>}
      {routes !== null && routes.length > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Provider</TableHeaderCell>
              <TableHeaderCell>Path prefix</TableHeaderCell>
              <TableHeaderCell>Origin</TableHeaderCell>
              <TableHeaderCell>Actions</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {routes.map(route => (
              <TableRow key={route.id}>
                <TableCell><TableCellLayout>{route.name}</TableCellLayout></TableCell>
                <TableCell>{route.provider}</TableCell>
                <TableCell>{route.pathPrefix}</TableCell>
                <TableCell><Badge appearance="tint">{route.isBuiltIn ? 'Built in' : 'Custom'}</Badge></TableCell>
                <TableCell>
                  <Button appearance="subtle" icon={<EditRegular />} aria-label={`Edit ${route.name}`} onClick={() => setEditing(route)} />
                  <ConfirmDialog
                    trigger={<Button appearance="subtle" icon={<DeleteRegular />} aria-label={`Delete ${route.name}`} />}
                    title={`Delete '${route.name}'?`}
                    message="The API route can only be deleted when it is not assigned to any model."
                    onConfirm={async () => {
                      await deleteApiRoute(route.id);
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
            <DialogTitle>Edit API route</DialogTitle>
            <DialogContent style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {editing && <ApiRouteFields value={editing} onChange={setEditing} />}
              <ErrorBanner error={error} />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setEditing(null)}>Cancel</Button>
              <Button
                appearance="primary"
                disabled={busy || editing === null || !isValid(editing)}
                onClick={() => editing && save(
                  () => updateApiRoute(editing.id, normalize(editing)),
                  () => setEditing(null),
                )}
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

function ApiRouteFields<T extends ApiRouteInput>({ value, onChange }: { value: T; onChange: (value: T) => void }) {
  const update = <K extends keyof T>(key: K, nextValue: T[K]) => onChange({ ...value, [key]: nextValue });
  return (
    <>
      <Field label="Name"><Input value={value.name} onChange={(_, data) => update('name', data.value)} /></Field>
      <Field label="Provider">
        <Select value={value.provider} onChange={(_, data) => update('provider', data.value as T['provider'])}>
          {providers.map(provider => <option key={provider} value={provider}>{provider}</option>)}
        </Select>
      </Field>
      <Field label="Path prefix"><Input value={value.pathPrefix} onChange={(_, data) => update('pathPrefix', data.value)} /></Field>
    </>
  );
}

function isValid(route: ApiRouteInput) {
  return route.name.trim() !== '' && route.pathPrefix.trim().startsWith('/');
}

function normalize(route: ApiRouteInput): ApiRouteInput {
  return { name: route.name.trim(), provider: route.provider, pathPrefix: route.pathPrefix.trim() };
}