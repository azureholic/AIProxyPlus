import { adminJson } from './client';
import type { ApiRoute, Provider } from './types';

export interface ApiRouteInput {
  name: string;
  provider: Provider;
  pathPrefix: string;
}

export function listApiRoutes(): Promise<ApiRoute[]> {
  return adminJson('/api/v1/config/api-routes');
}

export function createApiRoute(input: ApiRouteInput): Promise<ApiRoute> {
  return adminJson('/api/v1/config/api-routes', { method: 'POST', body: JSON.stringify(input) });
}

export function updateApiRoute(id: string, input: ApiRouteInput): Promise<ApiRoute> {
  return adminJson(`/api/v1/config/api-routes/${id}`, { method: 'PUT', body: JSON.stringify(input) });
}

export function deleteApiRoute(id: string): Promise<void> {
  return adminJson(`/api/v1/config/api-routes/${id}`, { method: 'DELETE' });
}