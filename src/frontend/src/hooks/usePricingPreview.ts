import { useEffect, useMemo, useState } from 'react';
import { previewPricing, type PricingPreviewRequest, type PricingPreviewResponse } from '../api/pizza';

export const PREVIEW_DEBOUNCE_MS = 250;

export type PricingPreviewState = {
  /** The most recent server response, which may belong to an older draft. Check `isCurrent` before presenting it as the total. */
  response: PricingPreviewResponse | null;
  /** True only when `response` was calculated for exactly the draft on screen. */
  isCurrent: boolean;
  /** A request for the current draft is scheduled or in flight. */
  pending: boolean;
  /** The pricing service could not be reached for the current draft. */
  unavailable: boolean;
};

type Settled = { key: string; response: PricingPreviewResponse | null };

/**
 * Keeps a server-calculated price in step with the draft order.
 * - Debounced, so typing a promo code or tip does not send a request per keystroke.
 * - Superseded requests are aborted and their results ignored (INV UI-003).
 * - A response is only "current" if it was computed for the exact draft shown (INV UI-002).
 * - Pass `request = null` for an empty order; no request is made (INV UI-004).
 */
export function usePricingPreview(request: PricingPreviewRequest | null, debounceMs = PREVIEW_DEBOUNCE_MS): PricingPreviewState {
  const key = useMemo(() => (request ? JSON.stringify(request) : null), [request]);
  const [settled, setSettled] = useState<Settled | null>(null);

  useEffect(() => {
    if (key === null) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      previewPricing(JSON.parse(key) as PricingPreviewRequest, controller.signal)
        .then(response => {
          // A superseded request must not apply its result even if its promise still resolves.
          if (!controller.signal.aborted) setSettled({ key, response });
        })
        .catch(() => {
          if (!controller.signal.aborted) setSettled({ key, response: null });
        });
    }, debounceMs);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [key, debounceMs]);

  const isCurrent = key !== null && settled?.key === key && settled.response !== null;
  const unavailable = key !== null && settled?.key === key && settled.response === null;
  return {
    response: settled?.response ?? null,
    isCurrent,
    pending: key !== null && settled?.key !== key,
    unavailable,
  };
}
