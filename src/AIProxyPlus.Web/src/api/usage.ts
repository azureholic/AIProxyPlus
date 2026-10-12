import { adminJson } from './client';
import type { OperationType, PagedResult, UsageRecord } from './types';

export interface UsageFilter {
  modelName?: string;
  consumerName?: string;
  operationType?: OperationType | '';
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}

export function queryUsage(filter: UsageFilter): Promise<PagedResult<UsageRecord>> {
  const params = new URLSearchParams();
  if (filter.modelName) params.set('modelName', filter.modelName);
  if (filter.consumerName) params.set('consumerName', filter.consumerName);
  if (filter.operationType) params.set('operationType', filter.operationType);
  if (filter.from) params.set('from', filter.from);
  if (filter.to) params.set('to', filter.to);
  params.set('page', String(filter.page));
  params.set('pageSize', String(filter.pageSize));

  return adminJson(`/api/v1/usage?${params.toString()}`);
}
