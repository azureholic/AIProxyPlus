import { useEffect, useState } from 'react';
import {
  Button,
  Combobox,
  Field,
  Input,
  Option,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { PageContainer, PageHeader } from '../../components/PageLayout';
import { ErrorBanner } from '../../components/Banners';
import { queryUsage } from '../../api/usage';
import type { OperationType, PagedResult, UsageRecord } from '../../api/types';

const operationTypes: OperationType[] = [
  'ChatCompletion',
  'Completion',
  'Embedding',
  'ImageGeneration',
  'ImageEdit',
  'ImageVariation',
  'ResponsesApi',
  'RealtimeVoice',
];

const useStyles = makeStyles({
  filters: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalM,
  },
  charts: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalL,
  },
  chart: {
    width: '480px',
    height: '260px',
  },
  pagination: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
});

export function UsageDashboardPage() {
  const styles = useStyles();
  const [modelDeploymentName, setModelDeploymentName] = useState('');
  const [consumerName, setConsumerName] = useState('');
  const [operationType, setOperationType] = useState<OperationType | ''>('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize] = useState(25);
  const [result, setResult] = useState<PagedResult<UsageRecord> | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(false);

  const load = () => {
    setLoading(true);
    setError(null);
    queryUsage({
      modelDeploymentName: modelDeploymentName || undefined,
      consumerName: consumerName || undefined,
      operationType,
      from: from ? new Date(from).toISOString() : undefined,
      to: to ? new Date(to).toISOString() : undefined,
      page,
      pageSize,
    })
      .then(setResult)
      .catch(setError)
      .finally(() => setLoading(false));
  };

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(load, [page]);

  const records = result?.items ?? [];
  const tokensByDeployment = Object.values(
    records.reduce<Record<string, { name: string; tokens: number }>>((acc, record) => {
      const key = record.modelDeploymentName;
      acc[key] ??= { name: key, tokens: 0 };
      acc[key].tokens += record.totalTokens ?? 0;
      return acc;
    }, {}),
  );
  const requestsByDay = Object.entries(
    records.reduce<Record<string, number>>((acc, record) => {
      const day = record.timestamp.slice(0, 10);
      acc[day] = (acc[day] ?? 0) + 1;
      return acc;
    }, {}),
  )
    .map(([day, count]) => ({ day, count }))
    .sort((a, b) => a.day.localeCompare(b.day));

  return (
    <PageContainer>
      <PageHeader title="Usage Dashboard" />

      <div className={styles.filters}>
        <Field label="Model deployment">
          <Input value={modelDeploymentName} onChange={(_, data) => setModelDeploymentName(data.value)} />
        </Field>
        <Field label="Consumer">
          <Input value={consumerName} onChange={(_, data) => setConsumerName(data.value)} />
        </Field>
        <Field label="Operation type">
          <Combobox
            value={operationType}
            selectedOptions={operationType ? [operationType] : []}
            onOptionSelect={(_, data) => setOperationType((data.optionValue as OperationType) ?? '')}
          >
            <Option value="">Any</Option>
            {operationTypes.map((type) => (
              <Option key={type} value={type}>
                {type}
              </Option>
            ))}
          </Combobox>
        </Field>
        <Field label="From">
          <input type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
        </Field>
        <Field label="To">
          <input type="date" value={to} onChange={(event) => setTo(event.target.value)} />
        </Field>
        <Button
          appearance="primary"
          onClick={() => {
            setPage(1);
            load();
          }}
        >
          Apply filters
        </Button>
      </div>

      <ErrorBanner error={error} />

      {loading && <Spinner label="Loading usage…" />}

      {!loading && records.length === 0 && result !== null && <p>No usage records match these filters.</p>}

      {records.length > 0 && (
        <>
          <div className={styles.charts}>
            <div className={styles.chart}>
              <ResponsiveContainer>
                <BarChart data={tokensByDeployment}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="name" />
                  <YAxis />
                  <Tooltip />
                  <Bar dataKey="tokens" fill="#464feb" name="Tokens by deployment" />
                </BarChart>
              </ResponsiveContainer>
            </div>
            <div className={styles.chart}>
              <ResponsiveContainer>
                <LineChart data={requestsByDay}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="day" />
                  <YAxis allowDecimals={false} />
                  <Tooltip />
                  <Line type="monotone" dataKey="count" stroke="#464feb" name="Requests per day" />
                </LineChart>
              </ResponsiveContainer>
            </div>
          </div>
          <p>
            <em>Charts reflect only the currently loaded page ({records.length} of {result?.totalCount} records).</em>
          </p>

          <Table>
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Timestamp</TableHeaderCell>
                <TableHeaderCell>Deployment</TableHeaderCell>
                <TableHeaderCell>Consumer</TableHeaderCell>
                <TableHeaderCell>Operation</TableHeaderCell>
                <TableHeaderCell>Tokens</TableHeaderCell>
                <TableHeaderCell>Streaming</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
                <TableHeaderCell>Duration</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {records.map((record) => (
                <TableRow key={record.id}>
                  <TableCell>{new Date(record.timestamp).toLocaleString()}</TableCell>
                  <TableCell>{record.modelDeploymentName}</TableCell>
                  <TableCell>{record.consumerName}</TableCell>
                  <TableCell>{record.operationType}</TableCell>
                  <TableCell>{record.totalTokens ?? '—'}</TableCell>
                  <TableCell>{record.streaming ? 'Yes' : 'No'}</TableCell>
                  <TableCell>{record.httpStatusCode}</TableCell>
                  <TableCell>{record.durationMs}ms</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          <div className={styles.pagination}>
            <Button disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              Previous
            </Button>
            <span>
              Page {result?.page} — {result?.totalCount} total
            </span>
            <Button disabled={(result?.items.length ?? 0) < pageSize} onClick={() => setPage((p) => p + 1)}>
              Next
            </Button>
          </div>
        </>
      )}
    </PageContainer>
  );
}
