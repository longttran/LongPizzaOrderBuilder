import { useCallback, useEffect, useMemo, useReducer, useRef, useState, type FormEvent } from 'react';
import { ApiError, cancelOrder, getMenu, placeOrder, updateOrder, type PizzaMenu, type PizzaOrder, type PizzaOrderRequest } from './api/pizza';
import { CartLines } from './components/CartLines';
import { OrderDetailsDialog } from './components/OrderDetailsDialog';
import { PizzaBuilder } from './components/PizzaBuilder';
import { PriceBreakdown } from './components/PriceBreakdown';
import { PromoAndTip } from './components/PromoAndTip';
import { cartReducer, createInitialState, selectedLine, toOrderItems, toPricingRequest, toTipRequest, type CartAction } from './domain/cart';
import { newIdempotencyKey } from './domain/idempotency';
import { useOrderTracking } from './hooks/useOrderTracking';
import { usePricingPreview } from './hooks/usePricingPreview';
import { OrderStatusPanel } from './components/OrderStatusPanel';

export default function App({ previewDebounceMs, statusPollMs }: { previewDebounceMs?: number; statusPollMs?: number } = {}) {
  const [menu, setMenu] = useState<PizzaMenu | null>(null);
  const [menuError, setMenuError] = useState('');
  const [state, dispatchCart] = useReducer(cartReducer, undefined, createInitialState);
  const [customerName, setCustomerName] = useState('');
  const tracking = useOrderTracking(statusPollMs);
  const [cancelling, setCancelling] = useState(false);
  /** A message about the last change or cancel attempt, shown with the placed order. */
  const [cancelNote, setCancelNote] = useState('');
  /** Set while the customer is changing a placed order. Saving sends this version, so a delayed or repeated save cannot overwrite newer changes (UI-018). */
  const [editing, setEditing] = useState<{ id: string; version: number } | null>(null);
  /** One key per checkout attempt. Kept across retries of the same order, dropped when the order changes or is accepted (UI-015). */
  const submissionKey = useRef<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState('');
  const [detailsOpen, setDetailsOpen] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getMenu(controller.signal).then(setMenu).catch(() => {
      if (!controller.signal.aborted) setMenuError('We couldn’t load the menu. Check that the API is running, then refresh the page.');
    });
    return () => controller.abort();
  }, []);

  // If the restaurant starts (or the customer cancels elsewhere) while the order is being edited, it can no longer be changed.
  useEffect(() => {
    const current = tracking.order;
    if (editing && current && current.id === editing.id && !current.canCustomerChange) {
      setEditing(null);
      dispatchCart({ type: 'reset' });
      setCustomerName('');
      setSubmitError('');
      setCancelNote('The restaurant has started this order while you were changing it, so it can no longer be changed.');
    }
  }, [tracking.order, editing]);

  // Any change to the draft is a different request, so it needs a new idempotency key and clears the last error.
  const dispatch = useCallback((action: CartAction) => {
    submissionKey.current = null;
    setSubmitError('');
    dispatchCart(action);
  }, []);

  const hasLines = state.lines.length > 0;
  const request = useMemo(() => (menu && hasLines ? toPricingRequest(state) : null), [menu, hasLines, state]);
  const preview = usePricingPreview(request, previewDebounceMs);
  const response = hasLines ? preview.response : null;
  const breakdown = response?.breakdown ?? null;
  const promotion = preview.isCurrent ? response?.promotion : undefined;

  const canSubmit = Boolean(menu) && hasLines && preview.isCurrent && breakdown !== null && response?.isValid === true
    && customerName.trim().length > 0 && !submitting;

  const requestBody = (): PizzaOrderRequest => ({
    customerName: customerName.trim(),
    items: toOrderItems(state),
    promoCode: state.promoCode.trim() || null,
    tip: toTipRequest(state.tip),
  });

  function loadIntoBuilder(order: PizzaOrder) {
    dispatchCart({ type: 'load', items: order.items, promoCode: order.promoCode, tip: order.tip });
    setCustomerName(order.customerName);
    setEditing({ id: order.id, version: order.version });
  }

  function startEditing() {
    const current = tracking.order;
    if (!current || !current.canCustomerChange) return;
    submissionKey.current = null;
    setSubmitError('');
    setCancelNote('');
    loadIntoBuilder(current);
  }

  function stopEditing() {
    setEditing(null);
    dispatchCart({ type: 'reset' });
    setCustomerName('');
    setSubmitError('');
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSubmit) return;
    setSubmitting(true);
    setSubmitError('');
    try {
      if (editing) await saveChanges(editing);
      else await sendNewOrder();
    } finally {
      setSubmitting(false);
    }
  }

  async function sendNewOrder() {
    const key = submissionKey.current ?? (submissionKey.current = newIdempotencyKey());
    try {
      // Only choices are sent. The service prices the order itself (UI-009).
      const order = await placeOrder(requestBody(), { idempotencyKey: key });
      submissionKey.current = null;
      dispatchCart({ type: 'reset' });
      setCustomerName('');
      setCancelNote('');
      tracking.track(order);
    } catch (error) {
      if (error instanceof ApiError && error.status !== undefined && error.status < 500) {
        // The service clearly refused, so no order exists and the next attempt is a fresh one.
        submissionKey.current = null;
        setSubmitError(error.message);
      } else {
        // A timeout, a dropped connection or a server error: the order may exist. Keep the key so pressing the button again is safe (UI-011, UI-015).
        setSubmitError('We couldn’t confirm your order, so it may or may not have been received. Press “Place my order” again. We’ll use the same order reference, so you won’t get a duplicate.');
      }
    }
  }

  async function saveChanges(target: { id: string; version: number }) {
    try {
      const updated = await updateOrder(target.id, requestBody(), target.version);
      tracking.accept(updated);
      setEditing(null);
      dispatchCart({ type: 'reset' });
      setCustomerName('');
      setCancelNote('');
    } catch (error) {
      if (error instanceof ApiError && error.status === 409 && error.order) {
        // The restaurant started first. Show the service's reason and the order as it now is (UI-018).
        tracking.accept(error.order);
        stopEditing();
        setCancelNote(error.message);
      } else if (error instanceof ApiError && error.status === 412 && error.order) {
        // The order changed since this version was loaded (for example on another phone). Load the latest so nothing is overwritten.
        tracking.accept(error.order);
        loadIntoBuilder(error.order);
        setSubmitError('This order changed since you opened it. We loaded the latest version. Review it and save again.');
      } else if (error instanceof ApiError && error.status !== undefined && error.status < 500) {
        setSubmitError(error.message);
      } else {
        // Unknown outcome. Saving again is safe: the same update sent twice changes the order once (UI-018, ORD-006).
        setSubmitError('We couldn’t confirm your change, so it may or may not have been saved. Press “Save changes” again. It is safe to repeat.');
      }
    }
  }

  async function cancel() {
    const current = tracking.order;
    if (!current) return;
    setCancelling(true);
    setCancelNote('');
    try {
      tracking.accept(await cancelOrder(current.id));
    } catch (error) {
      if (error instanceof ApiError && error.status === 409 && error.order) {
        // The restaurant got there first. Show the service's reason and the order as it now is (UI-016).
        tracking.accept(error.order);
        setCancelNote(error.message);
      } else {
        setCancelNote('We couldn’t confirm the cancellation. The status above is the latest we have, and pressing Cancel again is safe.');
        await tracking.refresh(current.id);
      }
    } finally {
      setCancelling(false);
    }
  }

  const closeDetails = useCallback(() => setDetailsOpen(false), []);
  const line = selectedLine(state);

  return <main className="app-shell">
    <header className="topbar"><a className="brand" href="#top"><span className="brand-mark" aria-hidden="true">🍕</span><span>Long<span className="brand-accent">Pizza</span>OrderBuilder</span></a></header>
    <section className="hero" id="top"><div className="hero-copy"><h1>Make it a<br /><span>pizza night.</span></h1><p className="hero-subtitle">Build as many pizzas as you like and watch your total update with every choice.</p></div></section>
    <form className="builder-layout" onSubmit={submit}>
      <div className="builder-main">
        {menuError ? <p className="error" role="alert">{menuError}</p>
          : menu ? <PizzaBuilder menu={menu} line={line} dispatch={dispatch} />
          : <p className="notice" role="status">Loading the menu…</p>}
      </div>
      <aside className="order-card" aria-label="Your order">
        <div className="order-card-header"><div><h2>{editing ? 'Change your order' : 'Your order'}</h2></div></div>
        {editing && <p className="editing-banner" role="status">You are changing order #{editing.id.slice(0, 8).toUpperCase()}. <button type="button" className="link-button" onClick={stopEditing}>Stop editing</button></p>}
        {menu && hasLines && <CartLines menu={menu} lines={state.lines} selectedId={state.selectedId} breakdown={breakdown} dispatch={dispatch} />}
        {menu && <PromoAndTip state={state} dispatch={dispatch} promotion={promotion} />}
        <PriceBreakdown hasLines={hasLines} breakdown={breakdown} isCurrent={preview.isCurrent} pending={preview.pending}
          unavailable={preview.unavailable} errors={preview.isCurrent && response ? response.errors : []} />
        {menu && breakdown && preview.isCurrent && response?.isValid &&
          <button type="button" className="link-button details-link" onClick={() => setDetailsOpen(true)}>View order details</button>}
        <label className="field-label" htmlFor="customerName">Name for the order</label>
        <input id="customerName" className="name-input" value={customerName} maxLength={80} placeholder="e.g. Long"
          onChange={event => { setCustomerName(event.target.value); submissionKey.current = null; setSubmitError(''); }} />
        <button className="place-order" type="submit" disabled={!canSubmit}>
          {editing ? (submitting ? 'Saving…' : 'Save changes') : (submitting ? 'Placing order…' : 'Place my order')} <span aria-hidden="true">→</span>
        </button>
        {submitError && <p className="error" role="alert">{submitError}</p>}
        {tracking.order && <OrderStatusPanel order={tracking.order} refreshFailed={tracking.refreshFailed} cancelling={cancelling}
          note={cancelNote} onChange={startEditing} onCancel={cancel} onDismiss={() => { tracking.clear(); setCancelNote(''); }} />}
      </aside>
    </form>
    {detailsOpen && menu && breakdown && <OrderDetailsDialog menu={menu} breakdown={breakdown} onClose={closeDetails} />}
    <footer><span>🍕 LongPizzaOrderBuilder</span><span>Built for big cravings & good times.</span></footer>
  </main>;
}
