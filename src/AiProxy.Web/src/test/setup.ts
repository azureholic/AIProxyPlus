import '@testing-library/jest-dom/vitest';

// jsdom doesn't implement ResizeObserver; FluentUI's MessageBar (ErrorBanner/InfoBanner/SecretRevealDialog)
// calls `new ResizeObserver(...)` unconditionally and crashes the render without this stub.
class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver;
