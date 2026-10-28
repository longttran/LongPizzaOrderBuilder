import { useEffect, useRef } from 'react';
import type { PizzaMenu, PricingBreakdown } from '../api/pizza';
import { money } from '../format';

type Props = { menu: PizzaMenu; breakdown: PricingBreakdown; onClose: () => void };

const FOCUSABLE = 'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';

/**
 * Read-only order description. It is rendered only when requested (INV UI-008), takes everything
 * from the server's breakdown, and closing it changes nothing about the order.
 */
export function OrderDetailsDialog({ menu, breakdown, onClose }: Props) {
  const dialog = useRef<HTMLDivElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    closeButton.current?.focus();
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
      } else if (event.key === 'Tab' && dialog.current) {
        const items = [...dialog.current.querySelectorAll<HTMLElement>(FOCUSABLE)];
        if (items.length === 0) return;
        const first = items[0];
        const last = items[items.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
      }
    }
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      opener?.focus();
    };
  }, [onClose]);

  const toppingName = (id: string) => menu.toppings.find(topping => topping.id === id)?.name ?? id;

  return <div className="dialog-backdrop" onClick={onClose}>
    <div ref={dialog} className="dialog" role="dialog" aria-modal="true" aria-labelledby="orderDetailsTitle" onClick={event => event.stopPropagation()}>
      <div className="dialog-header">
        <h2 id="orderDetailsTitle">Order details</h2>
        <button ref={closeButton} type="button" className="link-button" onClick={onClose}>Close</button>
      </div>
      <p className="notice">Estimate only. Nothing has been ordered yet.</p>
      <ol className="detail-lines">
        {breakdown.lines.map((line, index) => <li key={index}>
          <div className="price-line"><strong>{line.quantity} × {line.size} {line.crust}</strong><span>{money(line.lineTotal)}</span></div>
          <small>{line.toppings.length ? line.toppings.map(toppingName).join(', ') : 'Cheese only'} · {money(line.unitPrice)} each</small>
        </li>)}
      </ol>
      <div className="price-line"><span>Subtotal</span><span>{money(breakdown.subtotal)}</span></div>
      <div className="price-line"><span>Promotion</span><span>{breakdown.promotion.applied ? `${breakdown.promotion.code}: −${money(breakdown.discount)}` : 'None applied'}</span></div>
      <div className="price-line"><span>Tax</span><span>{money(breakdown.tax)}</span></div>
      <div className="price-line"><span>Tip</span><span>{money(breakdown.tip)}</span></div>
      <div className="total-line"><span>Total</span><strong>{money(breakdown.total)}</strong></div>
    </div>
  </div>;
}
