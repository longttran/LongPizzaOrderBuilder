import type { PizzaMenu, PizzaSize, Topping } from '../api/pizza';

/**
 * Presentation-only decoration keyed by menu IDs/names. Unknown menu entries fall back gracefully,
 * so adding a topping or crust on the server never breaks the UI.
 */
const toppingEmoji: Record<string, string> = {
  pepperoni: '🍕', sausage: '🌭', 'extra-cheese': '🧀', onions: '🧅', 'black-olives': '🫒', 'green-peppers': '🫑',
  bacon: '🥓', chicken: '🍗', garlic: '🧄', 'artichoke-hearts': '🌿', 'sun-dried-tomatoes': '🍅', pineapple: '🍍',
};
export const emojiForTopping = (id: string): string => toppingEmoji[id] ?? '✨';

const sizeBlurb: Record<string, string> = {
  Small: '8 inch · just for you', Medium: '12 inch · crowd favorite', Large: '14 inch · go big',
};
export const blurbForSize = (name: string): string => sizeBlurb[name] ?? '';

const crustInfo: Record<string, { icon: string; blurb: string }> = {
  Classic: { icon: '🥖', blurb: 'Golden, soft & chewy' },
  Thin: { icon: '🫓', blurb: 'Light, crisp & crunchy' },
  'Gluten-free': { icon: '🌾', blurb: 'Gluten-free option' },
};
export const infoForCrust = (name: string): { icon: string; blurb: string } => crustInfo[name] ?? { icon: '🥖', blurb: '' };

/**
 * A per-topping price HINT for the topping cards (rate x size base price, in cents).
 * It is never added up and never used for a total; totals come only from the server (INV UI-001).
 */
export function toppingPriceHint(menu: PizzaMenu, size: PizzaSize, topping: Topping): number {
  const base = menu.sizes.find(option => option.name === size)?.price ?? 0;
  return Math.round(base * topping.rate * 100) / 100;
}
