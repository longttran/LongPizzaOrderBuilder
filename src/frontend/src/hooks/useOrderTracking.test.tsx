import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../api/pizza';
import { deferred, orderWith } from '../test/fixtures';
import { useOrderTracking } from './useOrderTracking';

vi.mock('../api/pizza', async importOriginal => ({ ...(await importOriginal<typeof import('../api/pizza')>()), getOrder: vi.fn() }));
const getOrder = vi.mocked(api.getOrder);

beforeEach(() => {
  getOrder.mockReset();
});

describe('UI-017: the status on screen follows the server and never goes backwards', () => {
  it('moves forward as the server reports progress, then stops polling once the order is final', async () => {
    getOrder.mockResolvedValueOnce(orderWith('Started')).mockResolvedValue(orderWith('Delivered'));
    const { result } = renderHook(() => useOrderTracking(10));
    act(() => result.current.track(orderWith('NotStarted')));

    await waitFor(() => expect(result.current.order?.status).toBe('Delivered'));
    const callsWhenFinal = getOrder.mock.calls.length;
    await new Promise(resolve => setTimeout(resolve, 60));
    expect(getOrder.mock.calls.length).toBe(callsWhenFinal);   // no more requests for a finished order
  });

  it('ignores an older snapshot that arrives late', async () => {
    getOrder.mockResolvedValue(orderWith('NotStarted'));          // the server's reply was computed before the kitchen started
    const { result } = renderHook(() => useOrderTracking(10));
    act(() => result.current.track(orderWith('Started')));    // but we already know the kitchen started

    await waitFor(() => expect(getOrder).toHaveBeenCalled());
    await new Promise(resolve => setTimeout(resolve, 30));
    expect(result.current.order?.status).toBe('Started');
  });

  it('applies a newer snapshot given directly, such as a cancel reply, and ignores an older one', () => {
    getOrder.mockResolvedValue(orderWith('NotStarted'));
    const { result } = renderHook(() => useOrderTracking(60_000));
    act(() => result.current.track(orderWith('NotStarted')));
    act(() => result.current.accept(orderWith('Started')));
    expect(result.current.order?.status).toBe('Started');
    act(() => result.current.accept(orderWith('NotStarted')));
    expect(result.current.order?.status).toBe('Started');
  });

  it('keeps the last known status when a refresh fails, flags it, and recovers on the next success', async () => {
    const recovery = deferred<api.PizzaOrder>();
    getOrder.mockRejectedValueOnce(new api.ApiError('down')).mockReturnValue(recovery.promise);
    const { result } = renderHook(() => useOrderTracking(10));
    act(() => result.current.track(orderWith('NotStarted')));

    await waitFor(() => expect(result.current.refreshFailed).toBe(true));
    expect(result.current.order?.status).toBe('NotStarted');       // not guessed, not cleared

    await act(async () => { recovery.resolve(orderWith('Started')); });
    await waitFor(() => expect(result.current.order?.status).toBe('Started'));
    expect(result.current.refreshFailed).toBe(false);
  });

  it('does not poll when nothing is being tracked', async () => {
    renderHook(() => useOrderTracking(10));
    await new Promise(resolve => setTimeout(resolve, 40));
    expect(getOrder).not.toHaveBeenCalled();
  });
});
