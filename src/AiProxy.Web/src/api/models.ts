import { adminJson } from './client';
import type { Model, RoutingStrategy } from './types';

export function listModels(): Promise<Model[]> {
  return adminJson('/api/v1/config/models');
}

export function getModel(name: string): Promise<Model> {
  return adminJson(`/api/v1/config/models/${encodeURIComponent(name)}`);
}

export function createModel(name: string, strategy: RoutingStrategy): Promise<Model> {
  return adminJson('/api/v1/config/models', {
    method: 'POST',
    body: JSON.stringify({ name, strategy }),
  });
}

export function updateModel(name: string, strategy: RoutingStrategy): Promise<Model> {
  return adminJson(`/api/v1/config/models/${encodeURIComponent(name)}`, {
    method: 'PUT',
    body: JSON.stringify({ strategy }),
  });
}

export function deleteModel(name: string): Promise<void> {
  return adminJson(`/api/v1/config/models/${encodeURIComponent(name)}`, { method: 'DELETE' });
}

export interface AddEndpointInput {
  endpointId: string;
  priority: number;
  enabled: boolean;
}

export function addEndpoint(modelName: string, input: AddEndpointInput): Promise<Model['endpoints'][number]> {
  return adminJson(`/api/v1/config/models/${encodeURIComponent(modelName)}/endpoints`, {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export interface UpdateEndpointInput {
  priority?: number;
  enabled?: boolean;
}

export function updateEndpoint(
  modelName: string,
  endpointId: string,
  input: UpdateEndpointInput,
): Promise<Model['endpoints'][number]> {
  return adminJson(`/api/v1/config/models/${encodeURIComponent(modelName)}/endpoints/${endpointId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export function removeEndpoint(modelName: string, endpointId: string): Promise<void> {
  return adminJson(`/api/v1/config/models/${encodeURIComponent(modelName)}/endpoints/${endpointId}`, {
    method: 'DELETE',
  });
}