import type { PricingBreakdown } from '../api/pizza';
import { money } from '../format';

type Props = {
  hasLines: boolean;
  breakdown: PricingBreakdown | null;
  /** The breakdown was calculated for the draft on screen. */
  isCurrent: boolean;
  pending: boolean;
  unavailable: boolean;
  errors: string[];
};

/** Renders server-calculated amounts only. Nothing here adds, multiplies or rounds money (INV UI-001). */
export function PriceBreakdown({ hasLines, breakdown, isCurrent, pending, unavailable, errors }: Props) {
  if (!hasLines) {
    return <div className="breakdown">
      <div className="total-line"><span>Total</span><strong data-testid="order-total">{money(0)}</strong></div>
      <p className="tax-note">Add a pizza to see your total.</p>
    </div>;
  }
  const shown = breakdown && errors.length === 0 ? breakdown : null;
  const stale = !isCurrent && shown !== null;
  return <div className="breakdown" aria-busy={pending}>
    {errors.length > 0 && <p className="error" role="alert">{errors.join(' ')}</p>}
    {unavailable && <p className="error" role="alert">We can’t reach the pricing service right now. Your total will appear when it’s back.</p>}
    <div className={stale ? 'is-stale' : undefined}>
      {shown && <>
        <div className="price-line"><span>Subtotal</span><span>{money(shown.subtotal)}</span></div>
        {shown.discount > 0 && <div className="price-line"><span>Discount ({shown.promotion.code})</span><span>−{money(shown.discount)}</span></div>}
        <div className="price-line"><span>Tax ({+(shown.taxRate * 100).toFixed(2)}%)</span><span>{money(shown.tax)}</span></div>
        {shown.tip > 0 && <div className="price-line"><span>Tip</span><span>{money(shown.tip)}</span></div>}
      </>}
      <div className="total-line"><span>Total</span><strong data-testid="order-total">{shown ? money(shown.total) : '—'}</strong></div>
    </div>
    <p className="tax-note" role="status">{pending ? 'Updating…' : isCurrent && shown ? 'Server-calculated estimate' : ''}</p>
  </div>;
}
