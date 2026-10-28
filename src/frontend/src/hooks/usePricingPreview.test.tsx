import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../api/pizza';
import type { PricingPreviewRequest } from '../api/pizza';
import { deferred, fakeServerPricing } from '../test/fixtures';
import { usePricingPreview } from './usePricingPreview';

vi.mock('../api/pizza', async importOriginal => ({ ...(await importOriginal<typeof import('../api/pizza')>()), previewPricing: vi.fn() }));
const previewPricing = vi.mocked(api.previewPricing);

const draft = (quantity: number): PricingPreviewRequest => ({ items: [{ size: 'Medium', crust: 'Classic', toppings: [], quantity }], promoCode: null, tip: { type: 'None' } });
const totalOf = (quantity: number) => fakeServerPricing(draft(quantity)).breakdown!.total;

beforeEach(() => {
  previewPricing.mockReset();
});

describe('UI-003: a slow, superseded response can never replace a newer one', () => {
  it('ignores the first response when it arrives after the second', async () => {
    const first = deferred<api.PricingPreviewResponse>();
    const second = deferred<api.PricingPreviewResponse>();
    previewPricing.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);

    const { result, rerender } = renderHook(({ request }) => usePricingPreview(request, 0), { initialProps: { request: draft(1) as PricingPreviewRequest | null } });
    await waitFor(() => expect(previewPricing).toHaveBeenCalledTimes(1));
    rerender({ request: draft(2) });
    await waitFor(() => expect(previewPricing).toHaveBeenCalledTimes(2));

    await act(async () => { second.resolve(fakeServerPricing(draft(2))); });
    await act(async () => { first.resolve(fakeServerPricing(draft(1))); });

    expect(result.current.isCurrent).toBe(true);
    expect(result.current.response?.breakdown?.total).toBe(totalOf(2));
  });

  it('aborts the superseded request', async () => {
    previewPricing.mockImplementation(() => new Promise(() => {}));
    const { rerender } = renderHook(({ request }) => usePricingPreview(request, 0), { initialProps: { request: draft(1) as PricingPreviewRequest | null } });
    await waitFor(() => expect(previewPricing).toHaveBeenCalledTimes(1));
    const firstSignal = previewPricing.mock.calls[0][1] as AbortSignal;
    rerender({ request: draft(2) });
    expect(firstSignal.aborted).toBe(true);
  });
});

describe('UI-002: a response is current only for the draft it was calculated for', () => {
  it('reports pending, not current, while the new draft is being priced', async () => {
    const next = deferred<api.PricingPreviewResponse>();
    previewPricing.mockResolvedValueOnce(fakeServerPricing(draft(1))).mockReturnValueOnce(next.promise);
    const { result, rerender } = renderHook(({ request }) => usePricingPreview(request, 0), { initialProps: { request: draft(1) as PricingPreviewRequest | null } });
    await waitFor(() => expect(result.current.isCurrent).toBe(true));

    rerender({ request: draft(3) });
    expect(result.current.isCurrent).toBe(false);
    expect(result.current.pending).toBe(true);

    await waitFor(() => expect(previewPricing).toHaveBeenCalledTimes(2));
    await act(async () => { next.resolve(fakeServerPricing(draft(3))); });
    await waitFor(() => expect(result.current.isCurrent).toBe(true));
    expect(result.current.pending).toBe(false);
  });

  it('reports unavailable when the pricing call fails, never a made-up total', async () => {
    previewPricing.mockRejectedValue(new api.ApiError('down'));
    const { result } = renderHook(() => usePricingPreview(draft(1), 0));
    await waitFor(() => expect(result.current.unavailable).toBe(true));
    expect(result.current.isCurrent).toBe(false);
    expect(result.current.response).toBeNull();
  });

  it('debounces rapid changes into a single request', async () => {
    vi.useFakeTimers();
    try {
      previewPricing.mockResolvedValue(fakeServerPricing(draft(1)));
      const { rerender } = renderHook(({ request }) => usePricingPreview(request, 250), { initialProps: { request: draft(1) as PricingPreviewRequest | null } });
      rerender({ request: draft(2) });
      rerender({ request: draft(3) });
      await act(async () => { await vi.advanceTimersByTimeAsync(300); });
      expect(previewPricing).toHaveBeenCalledTimes(1);
      expect(previewPricing.mock.calls[0][0]).toEqual(draft(3));
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('UI-004: an empty order makes no pricing request', () => {
  it('stays idle when there is nothing to price', async () => {
    const { result } = renderHook(() => usePricingPreview(null, 0));
    await new Promise(resolve => setTimeout(resolve, 20));
    expect(previewPricing).not.toHaveBeenCalled();
    expect(result.current).toMatchObject({ isCurrent: false, pending: false, unavailable: false });
  });
});
