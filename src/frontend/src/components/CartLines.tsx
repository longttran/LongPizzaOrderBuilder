import type { Dispatch } from 'react';
import type { PizzaMenu, PricingBreakdown } from '../api/pizza';
import { MAX_LINES, MAX_QUANTITY, describeToppings, type CartAction, type CartLine } from '../domain/cart';
import { findPricedLine } from '../domain/pricedLines';
import { money } from '../format';

type Props = {
  menu: PizzaMenu;
  lines: CartLine[];
  selectedId: string | null;
  breakdown: PricingBreakdown | null;
  dispatch: Dispatch<CartAction>;
};

export function CartLines({ menu, lines, selectedId, breakdown, dispatch }: Props) {
  return <div className="cart">
    <ul className="cart-lines" aria-label="Pizzas in your order">
      {lines.map((line, index) => {
        const priced = findPricedLine(line, breakdown);
        const title = `Pizza ${index + 1}: ${line.size} ${line.crust}`;
        return <li key={line.id} className={`cart-line ${line.id === selectedId ? 'selected' : ''}`}>
          <button
            type="button" className="cart-line-main" aria-current={line.id === selectedId ? 'true' : undefined}
            aria-label={`Edit ${title}`} onClick={() => dispatch({ type: 'selectLine', id: line.id })}
          >
            <strong>{line.size} {line.crust}</strong>
            <small>{describeToppings(line, menu)}</small>
          </button>
          <span className="cart-line-total" aria-label={`${title} total`}>{priced ? money(priced.lineTotal) : '…'}</span>
          <div className="quantity-control" role="group" aria-label={`${title} quantity`}>
            <button type="button" aria-label={`Decrease quantity of ${title}`} disabled={line.quantity <= 1}
              onClick={() => dispatch({ type: 'setQuantity', id: line.id, quantity: line.quantity - 1 })}>−</button>
            <strong aria-live="polite">{line.quantity}</strong>
            <button type="button" aria-label={`Increase quantity of ${title}`} disabled={line.quantity >= MAX_QUANTITY}
              onClick={() => dispatch({ type: 'setQuantity', id: line.id, quantity: line.quantity + 1 })}>+</button>
          </div>
          <div className="cart-line-actions">
            <button type="button" className="link-button" aria-label={`Duplicate ${title}`} disabled={lines.length >= MAX_LINES} onClick={() => dispatch({ type: 'duplicateLine', id: line.id })}>Duplicate</button>
            <button type="button" className="link-button" aria-label={`Remove ${title}`} onClick={() => dispatch({ type: 'removeLine', id: line.id })}>Remove</button>
          </div>
        </li>;
      })}
    </ul>
    <button type="button" className="add-pizza" disabled={lines.length >= MAX_LINES} onClick={() => dispatch({ type: 'addLine' })}>
      {lines.length >= MAX_LINES ? `Limit of ${MAX_LINES} pizzas reached` : '+ Add another pizza'}
    </button>
  </div>;
}
