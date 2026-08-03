import { adminJson } from './client';
import type { ModelDeployment, RoutingStrategy } from './types';

export function listModelDeployments(): Promise<ModelDeployment[]> {
  return adminJson('/api/v1/config/model-deployments');
}

export function getModelDeployment(name: string): Promise<ModelDeployment> {
  return adminJson(`/api/v1/config/model-deployments/${encodeURIComponent(name)}`);
}

export function createModelDeployment(name: string, strategy: RoutingStrategy): Promise<ModelDeployment> {
  return adminJson('/api/v1/config/model-deployments', {
    method: 'POST',
    body: JSON.stringify({ name, strategy }),
  });
}

export function updateModelDeployment(name: string, strategy: RoutingStrategy): Promise<ModelDeployment> {
  return adminJson(`/api/v1/config/model-deployments/${encodeURIComponent(name)}`, {
    method: 'PUT',
    body: JSON.stringify({ strategy }),
  });
}

export function deleteModelDeployment(name: string): Promise<void> {
  return adminJson(`/api/v1/config/model-deployments/${encodeURIComponent(name)}`, { method: 'DELETE' });
}

export interface AddEndpointInput {
  endpointId: string;
  priority: number;
  enabled: boolean;
}

export function addEndpoint(deploymentName: string, input: AddEndpointInput): Promise<ModelDeployment['endpoints'][number]> {
  return adminJson(`/api/v1/config/model-deployments/${encodeURIComponent(deploymentName)}/endpoints`, {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export interface UpdateEndpointInput {
  priority?: number;
  enabled?: boolean;
}

export function updateEndpoint(
  deploymentName: string,
  endpointId: string,
  input: UpdateEndpointInput,
): Promise<ModelDeployment['endpoints'][number]> {
  return adminJson(`/api/v1/config/model-deployments/${encodeURIComponent(deploymentName)}/endpoints/${endpointId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export function removeEndpoint(deploymentName: string, endpointId: string): Promise<void> {
  return adminJson(`/api/v1/config/model-deployments/${encodeURIComponent(deploymentName)}/endpoints/${endpointId}`, {
    method: 'DELETE',
  });
}