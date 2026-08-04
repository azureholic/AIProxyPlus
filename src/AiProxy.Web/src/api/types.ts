// Mirrors the backend's JSON shapes (camelCase — ConfigureHttpJsonOptions uses JsonStringEnumConverter, so
// enums serialize as strings). Kept in one file since the frontend has no code-gen from the backend's OpenAPI
// document yet — see specs/iterations/iteration-3/tasks.md for that as a possible follow-up.

export type Provider = 'OpenAI' | 'MicrosoftAI' | 'Anthropic';
export type AuthMode = 'ApiKey' | 'ManagedIdentity';
export type RoutingStrategy = 'Priority';
export type ModelCapability = 'Chat' | 'ImageGeneration' | 'Realtime' | 'SpeechToText' | 'TextToSpeech';
export type EndpointHealthState = 'Unknown' | 'Healthy' | 'Unhealthy';
export type OperationType =
  | 'ChatCompletion'
  | 'Completion'
  | 'Embedding'
  | 'ImageGeneration'
  | 'ImageEdit'
  | 'ImageVariation'
  | 'ResponsesApi'
  | 'MessagesApi'
  | 'RealtimeVoice'
  | 'SpeechToText'
  | 'TextToSpeech';

export interface Endpoint {
  id: string;
  modelId: string;
  endpointId: string;
  apiRouteId: string;
  apiRoute: ApiRoute;
  name: string;
  baseUrl: string;
  provider: Provider;
  priority: number;
  authMode: AuthMode;
  secretRef: string | null;
  managedIdentityScope: string | null;
  managedIdentityTenantId: string | null;
  enabled: boolean;
  timeoutSeconds: number;
  maxRetries: number;
  health: EndpointHealthState;
}

export interface EndpointConfig {
  id: string;
  name: string;
  baseUrl: string;
  authMode: AuthMode;
  secretRef: string | null;
  managedIdentityScope: string | null;
  managedIdentityTenantId: string | null;
  timeoutSeconds: number;
  maxRetries: number;
}

export interface ApiRoute {
  id: string;
  name: string;
  provider: Provider;
  pathPrefix: string;
  isBuiltIn: boolean;
}

export interface Model {
  id: string;
  name: string;
  strategy: RoutingStrategy;
  capabilities: ModelCapability[];
  createdAt: string;
  updatedAt: string;
  endpoints: Endpoint[];
}

export interface Consumer {
  id: string;
  name: string;
  authenticationType: 'apiKey' | 'oidc';
  enabled: boolean;
  createdAt: string;
  revokedAt: string | null;
}

export interface ConsumerWithApiKey {
  consumer: Consumer;
  apiKey: string;
}

export interface UsageRecord {
  id: string;
  timestamp: string;
  modelName: string;
  consumerId: string;
  consumerName: string;
  endpointId: string;
  provider: Provider;
  operationType: OperationType;
  requestId: string;
  sessionId: string | null;
  promptTokens: number | null;
  completionTokens: number | null;
  totalTokens: number | null;
  usageMetrics: Record<string, string> | null;
  streaming: boolean;
  httpStatusCode: number;
  durationMs: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface UsageRecordFilter {
  modelName?: string;
  consumerName?: string;
  operationType?: OperationType;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}
