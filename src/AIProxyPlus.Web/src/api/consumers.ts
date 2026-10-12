import { adminJson } from './client';
import type { Consumer, CreatedConsumer, RotatedConsumerKey } from './types';

export function listConsumers(): Promise<Consumer[]> {
  return adminJson('/api/v1/config/consumers');
}

export function createConsumer(name: string): Promise<CreatedConsumer> {
  return adminJson('/api/v1/config/consumers', { method: 'POST', body: JSON.stringify({ name }) });
}

export function createOidcConsumer(name: string, issuer: string, subject: string): Promise<Consumer> {
  return adminJson('/api/v1/config/consumers/oidc', {
    method: 'POST',
    body: JSON.stringify({ name, issuer, subject }),
  });
}

export function createManagedIdentityConsumer(name: string, tenantId: string, objectId: string): Promise<Consumer> {
  return adminJson('/api/v1/config/consumers/managed-identity', {
    method: 'POST',
    body: JSON.stringify({ name, tenantId, objectId }),
  });
}

export function rotateConsumerKey(name: string, slot: 1 | 2): Promise<RotatedConsumerKey> {
  return adminJson(`/api/v1/config/consumers/${encodeURIComponent(name)}/rotate?slot=${slot}`, { method: 'PUT' });
}

export function disableConsumer(name: string): Promise<Consumer> {
  return adminJson(`/api/v1/config/consumers/${encodeURIComponent(name)}/disable`, { method: 'PUT' });
}

export function enableConsumer(name: string): Promise<Consumer> {
  return adminJson(`/api/v1/config/consumers/${encodeURIComponent(name)}/enable`, { method: 'PUT' });
}

export function allowConsumerModel(name: string, modelName: string): Promise<Consumer> {
  return adminJson(
    `/api/v1/config/consumers/${encodeURIComponent(name)}/allowed-models/${encodeURIComponent(modelName)}`,
    { method: 'PUT' },
  );
}

export function revokeConsumerModel(name: string, modelName: string): Promise<Consumer> {
  return adminJson(
    `/api/v1/config/consumers/${encodeURIComponent(name)}/allowed-models/${encodeURIComponent(modelName)}`,
    { method: 'DELETE' },
  );
}

export function revokeConsumer(name: string): Promise<Consumer> {
  return adminJson(`/api/v1/config/consumers/${encodeURIComponent(name)}`, { method: 'DELETE' });
}

export function deleteConsumerPermanently(name: string): Promise<void> {
  return adminJson(`/api/v1/config/consumers/${encodeURIComponent(name)}/permanent`, { method: 'DELETE' });
}
