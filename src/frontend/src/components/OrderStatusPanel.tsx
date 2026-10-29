import type { OrderStatus, PizzaOrder } from '../api/pizza';
import { money } from '../format';
import { isFinalStatus } from '../hooks/useOrderTracking';

const text: Record<OrderStatus, { icon: string; label: string; detail: string }> = {
  NotStarted: { icon: '●', label: 'Not Started', detail: 'The restaurant hasn’t started yet. You can still cancel it.' },
  Started: { icon: '◐', label: 'Started', detail: 'The restaurant is making your order.' },
  ReadyForPickup: { icon: '◕', label: 'Ready for Pickup', detail: 'Your order is ready.' },
  Delivered: { icon: '✓', label: 'Delivered', detail: 'Your order was delivered.' },
  Cancelled: { icon: '✕', label: 'Cancelled', detail: 'This order was cancelled.' },
};

const eventLabel = (event: string, status: OrderStatus): string =>
  event === 'Placed' ? 'Placed' : event === 'Updated' ? 'Changed' : text[status].label;

type Props = {
  order: PizzaOrder;
  refreshFailed: boolean;
  cancelling: boolean;
  /** A message about the last change or cancel attempt (for example "the restaurant has already started"). */
  note: string;
  onCancel: () => void;
  onDismiss: () => void;
};

const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });

/**
 * Shows an order's status exactly as the order service last reported it. The status is always written out in words, with a symbol,
 * never by colour alone. The Change and Cancel buttons appear only when the service says the customer can still change the order.
 */
export function OrderStatusPanel({ order, refreshFailed, cancelling, note, onCancel, onDismiss }: Props) {
  const shown = text[order.status];
  return <section className="order-status" aria-label="Your placed order">
    <div className="order-status-head">
      <strong>Order #{order.id.slice(0, 8).toUpperCase()}</strong>
      {isFinalStatus(order.status) && <button type="button" className="link-button" onClick={onDismiss}>Dismiss</button>}
    </div>
    <p className="status-line" role="status">
      <span aria-hidden="true">{shown.icon}</span> <strong>{shown.label}</strong>. {shown.detail}
    </p>
    <p className="notice">Thanks, {order.customerName}. Total: {money(order.total)}</p>
    {order.canCustomerCancel && <button type="button" className="cancel-order" disabled={cancelling} onClick={onCancel}>
      {cancelling ? 'Cancelling…' : 'Cancel this order'}
    </button>}
    {note && <p className="error" role="alert">{note}</p>}
    {refreshFailed && <p className="notice" role="status">We couldn’t refresh the status just now. This is the last update we have, from {time(order.updatedAt)}.</p>}
    <details>
      <summary>Order history</summary>
      <ol className="history">
        {order.history.map((change, index) => <li key={index}>
          {eventLabel(change.event, change.status)} at {time(change.at)}{change.actor === 'Restaurant' ? ' (restaurant)' : ''}
        </li>)}
      </ol>
    </details>
  </section>;
}
