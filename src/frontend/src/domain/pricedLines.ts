import type { PricedLine, PricingBreakdown } from '../api/pizza';
import type { CartLine } from './cart';

const sameToppings = (a: string[], b: string[]): boolean => a.length === b.length && a.every((id, index) => id === b[index]);

/**
 * Finds the server-priced line for a cart line by comparing the configuration, not the position.
 * A line whose configuration changed since the last response has no match, so the UI shows a
 * placeholder instead of a stale price (INV UI-002). A match is always the true server price
 * for that exact configuration, even if the response is otherwise out of date.
 */
export function findPricedLine(line: CartLine, breakdown: PricingBreakdown | null | undefined): PricedLine | undefined {
  return breakdown?.lines.find(priced =>
    priced.size === line.size && priced.crust === line.crust && priced.quantity === line.quantity && sameToppings(priced.toppings, line.toppings));
}
