import { useCallback, useEffect, useState } from 'react';
import { getOrder, type OrderStatus, type PizzaOrder } from '../api/pizza';

export const STATUS_POLL_MS = 5000;

/** Display-only: used to stop polling once an order can no longer change. The server owns the actual rules. */
export const isFinalStatus = (status: OrderStatus): boolean => status === 'Delivered' || status === 'Cancelled';

export type OrderTracking = {
  order: PizzaOrder | null;
  /** The last attempt to refresh the status failed. The order shown is the last one we know to be true. */
  refreshFailed: boolean;
  /** Start tracking a newly placed order. */
  track: (order: PizzaOrder) => void;
  /** Apply a newer snapshot of the tracked order (from a cancel reply or a refresh). Older snapshots are ignored. */
  accept: (order: PizzaOrder) => void;
  /** Ask the server for the order now. */
  refresh: (id: string) => Promise<void>;
  clear: () => void;
};

/**
 * Keeps the status of a placed order in step with the server.
 * - Polls until the order is Delivered or Cancelled.
 * - A snapshot with a shorter history than the one we hold is older, so it can never move the order backwards (UI-017).
 * - If a refresh fails, the last known status stays on screen and `refreshFailed` is set; it is never guessed (UI-017).
 */
export function useOrderTracking(pollMs: number = STATUS_POLL_MS): OrderTracking {
  const [order, setOrder] = useState<PizzaOrder | null>(null);
  const [refreshFailed, setRefreshFailed] = useState(false);

  const track = useCallback((next: PizzaOrder) => { setOrder(next); setRefreshFailed(false); }, []);
  const clear = useCallback(() => { setOrder(null); setRefreshFailed(false); }, []);
  const accept = useCallback((next: PizzaOrder) => {
    setOrder(current => (current && current.id === next.id && next.history.length < current.history.length ? current : next));
  }, []);

  const refresh = useCallback(async (id: string) => {
    try {
      accept(await getOrder(id));
      setRefreshFailed(false);
    } catch {
      setRefreshFailed(true);
    }
  }, [accept]);

  const id = order?.id;
  const final = order ? isFinalStatus(order.status) : true;
  useEffect(() => {
    if (!id || final) return;
    const controller = new AbortController();
    const timer = setInterval(() => {
      getOrder(id, controller.signal)
        .then(fresh => { if (!controller.signal.aborted) { accept(fresh); setRefreshFailed(false); } })
        .catch(() => { if (!controller.signal.aborted) setRefreshFailed(true); });
    }, pollMs);
    return () => { clearInterval(timer); controller.abort(); };
  }, [id, final, pollMs, accept]);

  return { order, refreshFailed, track, accept, refresh, clear };
}
