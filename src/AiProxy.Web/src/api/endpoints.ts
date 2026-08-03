import { adminJson } from './client';
import type { AuthMode, EndpointConfig } from './types';

export function listEndpoints(): Promise<EndpointConfig[]> {
  return adminJson('/api/v1/config/endpoints');
}

export interface CreateEndpointInput {
  name: string;
  baseUrl: string;
  authMode: AuthMode;
  apiKeyPlaintext?: string;
  managedIdentityScope?: string;
  managedIdentityTenantId?: string;
  timeoutSeconds: number;
  maxRetries: number;
}

export function createEndpoint(input: CreateEndpointInput): Promise<EndpointConfig> {
  return adminJson('/api/v1/config/endpoints', { method: 'POST', body: JSON.stringify(input) });
}

export interface UpdateEndpointConfigInput {
  name: string;
  baseUrl: string;
  managedIdentityScope?: string;
  managedIdentityTenantId?: string;
  timeoutSeconds: number;
  maxRetries: number;
}

export function updateEndpointConfig(id: string, input: UpdateEndpointConfigInput): Promise<EndpointConfig> {
  return adminJson(`/api/v1/config/endpoints/${id}`, { method: 'PUT', body: JSON.stringify(input) });
}

export function deleteEndpoint(id: string): Promise<void> {
  return adminJson(`/api/v1/config/endpoints/${id}`, { method: 'DELETE' });
}

export function setEndpointSecret(id: string, apiKeyPlaintext: string): Promise<void> {
  return adminJson(`/api/v1/config/endpoints/${id}/secret`, {
    method: 'PUT',
    body: JSON.stringify({ apiKeyPlaintext }),
  });
}