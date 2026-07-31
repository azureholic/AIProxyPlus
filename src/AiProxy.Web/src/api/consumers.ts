import { adminJson } from './client';
import type { Consumer, ConsumerWithApiKey } from './types';

export function listConsumers(): Promise<Consumer[]> {
  return adminJson('/api/v1/config/consumers');
}

export function createConsumer(name: string): Promise<ConsumerWithApiKey> {
  return adminJson('/api/v1/config/consumers', { method: 'POST', body: JSON.stringify({ name }) });
}

export function rotateConsumerKey(name: string): Promise<ConsumerWithApiKey> {
  return adminJson(`/api/v1/config/consumers/${encodeURIComponent(name)}/rotate`, { method: 'PUT' });
}

export function revokeConsumer(name: string): Promise<Consumer> {
  return adminJson(`/api/v1/config/consumers/${encodeURIComponent(name)}`, { method: 'DELETE' });
}
