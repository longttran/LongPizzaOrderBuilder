import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, cancelOrder, getMenu, getOrder, placeOrder, previewPricing, updateOrder } from './pizza';
import { fakeServerPricing, orderWith } from '../test/fixtures';

const item = { size: 'Medium' as const, crust: 'Classic', toppings: [], quantity: 1 };
const reply = (status: number, body: unknown) => vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status })));
afterEach(() => vi.unstubAllGlobals());

describe('UI-006: validation failures are returned, not swallowed', () => {
  it('returns the server body for HTTP 400 so its errors can be shown', async () => {
    const invalid = { breakdown: null, promotion: fakeServerPricing({ items: [item] }).promotion, errors: ['Item 1: choose a valid pizza size.'], isValid: false };
    reply(400, invalid);
    await expect(previewPricing({ items: [item] })).resolves.toMatchObject({ errors: ['Item 1: choose a valid pizza size.'], isValid: false });
  });

  it('throws an ApiError for server failures and for unreadable bodies', async () => {
    reply(500, { title: 'boom' });
    await expect(previewPricing({ items: [item] })).rejects.toBeInstanceOf(ApiError);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('not json', { status: 200 })));
    await expect(previewPricing({ items: [item] })).rejects.toBeInstanceOf(ApiError);
  });

  it('surfaces the first server error message when an order is rejected', async () => {
    reply(400, { errors: ['Customer name is required.'] });
    await expect(placeOrder({ customerName: '', items: [item] })).rejects.toMatchObject({ status: 400, message: 'Customer name is required.' });
  });
});

describe('UI-015: an order request carries its idempotency key and times out into an unknown outcome', () => {
  it('sends the key in the Idempotency-Key header and only choices in the body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(orderWith('NotStarted')), { status: 201 }));
    vi.stubGlobal('fetch', fetchMock);
    await placeOrder({ customerName: 'Sam', items: [item] }, { idempotencyKey: 'key-123' });
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('key-123');
    expect(String(init.body)).not.toMatch(/total|price|tax/i);
  });

  it('sends no key header when none is given', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(orderWith('NotStarted')), { status: 201 }));
    vi.stubGlobal('fetch', fetchMock);
    await placeOrder({ customerName: 'Sam', items: [item] });
    expect((fetchMock.mock.calls[0][1] as RequestInit).headers as Record<string, string>).not.toHaveProperty('Idempotency-Key');
  });

  it('turns a server that never answers into an ApiError with no status (unknown outcome)', async () => {
    vi.useFakeTimers();
    try {
      vi.stubGlobal('fetch', vi.fn((_url: string, init: RequestInit) => new Promise((_resolve, reject) => {
        init.signal!.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
      })));
      const pending = placeOrder({ customerName: 'Sam', items: [item] }, { idempotencyKey: 'k', timeoutMs: 50 });
      const caught = pending.catch((error: unknown) => error);
      await vi.advanceTimersByTimeAsync(60);
      const error = await caught;
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBeUndefined();
    } finally {
      vi.useRealTimers();
    }
  });

  it('treats an HTTP 422 key conflict as a clear refusal with the server’s message', async () => {
    reply(422, { errors: ['This Idempotency-Key was already used for a different order request.'] });
    await expect(placeOrder({ customerName: 'Sam', items: [item] }, { idempotencyKey: 'k' })).rejects.toMatchObject({ status: 422 });
  });
});

describe('UI-016: cancelling and looking up an order report what the server says', () => {
  it('returns the cancelled order', async () => {
    reply(200, orderWith('Cancelled'));
    await expect(cancelOrder('abc')).resolves.toMatchObject({ status: 'Cancelled', canCustomerChange: false });
  });

  it('on 409 throws an ApiError that carries the order as it now is', async () => {
    reply(409, { errors: ['The restaurant has already started this order, so it can no longer be cancelled.'], order: orderWith('Started') });
    await expect(cancelOrder('abc')).rejects.toMatchObject({ status: 409, message: expect.stringContaining('restaurant'), order: { status: 'Started' } });
  });

  it('looks an order up, and throws for an unknown one', async () => {
    reply(200, orderWith('Started'));
    await expect(getOrder('abc')).resolves.toMatchObject({ status: 'Started' });
    reply(404, {});
    await expect(getOrder('nope')).rejects.toMatchObject({ status: 404 });
  });
});

describe('UI-018: updating an order names the version it is based on', () => {
  it('sends If-Match with the version and only choices in the body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(orderWith('NotStarted', { updates: 1 })), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await updateOrder('abc', { customerName: 'Sam', items: [item] }, 3);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toMatch(/\/api\/orders\/abc$/);
    expect(init.method).toBe('PUT');
    expect((init.headers as Record<string, string>)['If-Match']).toBe('"3"');
    expect(String(init.body)).not.toMatch(/total|price|tax/i);
  });

  it('throws an ApiError carrying the order for 409 (restaurant started) and 412 (order changed since)', async () => {
    reply(409, { errors: ['The restaurant has already started this order, so it can no longer be changed.'], order: orderWith('Started') });
    await expect(updateOrder('abc', { customerName: 'Sam', items: [item] }, 1)).rejects.toMatchObject({ status: 409, order: { status: 'Started' } });
    reply(412, { errors: ['This order has changed since you loaded it.'], order: orderWith('NotStarted', { updates: 1 }) });
    await expect(updateOrder('abc', { customerName: 'Sam', items: [item] }, 1)).rejects.toMatchObject({ status: 412, order: { version: 2 } });
  });
});

describe('Two services: the menu and price preview come from the order builder, orders go to the order service', () => {
  it('uses separate addresses for the two', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(orderWith('NotStarted')), { status: 200 })));
    vi.stubGlobal('fetch', fetchMock);
    await getMenu().catch(() => undefined);
    await getOrder('abc');
    await placeOrder({ customerName: 'Sam', items: [item] }, { idempotencyKey: 'k' });
    await cancelOrder('abc');
    const urls = fetchMock.mock.calls.map(call => String(call[0]));
    expect(urls[0]).toContain(':8080/api/menu');                  // the order builder's API
    for (const url of urls.slice(1)) expect(url).toContain(':8090/api/orders');   // the order service
  });
});
