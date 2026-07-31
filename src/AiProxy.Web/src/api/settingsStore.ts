// Stand-in for real auth (architecture.md §17 already documents admin auth as a placeholder static key) —
// keys are kept in sessionStorage only (cleared when the tab closes), never localStorage, never logged.
const ADMIN_KEY_STORAGE_KEY = 'aiproxy.adminApiKey';
const CONSUMER_KEY_STORAGE_KEY = 'aiproxy.consumerApiKey';

export function getAdminApiKey(): string {
  return sessionStorage.getItem(ADMIN_KEY_STORAGE_KEY) ?? '';
}

export function setAdminApiKey(value: string): void {
  sessionStorage.setItem(ADMIN_KEY_STORAGE_KEY, value);
}

export function getConsumerApiKey(): string {
  return sessionStorage.getItem(CONSUMER_KEY_STORAGE_KEY) ?? '';
}

export function setConsumerApiKey(value: string): void {
  sessionStorage.setItem(CONSUMER_KEY_STORAGE_KEY, value);
}
