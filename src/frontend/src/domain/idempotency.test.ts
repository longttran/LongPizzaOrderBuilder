import { afterEach, describe, expect, it, vi } from 'vitest';
import { newIdempotencyKey } from './idempotency';

afterEach(() => vi.unstubAllGlobals());

describe('UI-015: every checkout attempt gets its own unguessable key', () => {
  it('looks like a version 4 UUID and is unique', () => {
    const keys = new Set(Array.from({ length: 2000 }, newIdempotencyKey));
    expect(keys.size).toBe(2000);
    for (const key of keys) expect(key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  });

  it('satisfies the server’s key rules', () => {
    expect(newIdempotencyKey()).toMatch(/^[A-Za-z0-9_.:-]{1,100}$/);
  });

  it('still works on a plain-http page, where crypto.randomUUID does not exist', () => {
    vi.stubGlobal('crypto', { getRandomValues: globalThis.crypto.getRandomValues.bind(globalThis.crypto) });
    expect(newIdempotencyKey()).toMatch(/^[0-9a-f-]{36}$/);
  });
});
