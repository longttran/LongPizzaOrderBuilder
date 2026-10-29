import type { PizzaMenu, PizzaOrderItemRequest, PizzaSize, PricingPreviewRequest, TipRequest } from '../api/pizza';

/**
 * Draft order state. It holds CHOICES only (sizes, toppings, quantities, promo text, tip choice).
 * It never holds a price: every amount shown to the customer comes from the server's pricing preview.
 */

/** Mirrors the server's MaxQuantityPerLine. The server remains the authority and rejects anything above it. */
export const MAX_QUANTITY = 10;
/** Mirrors the server's MaxLinesPerOrder. */
export const MAX_LINES = 50;
/** Presentation choices for the tip buttons. They are inputs to the server, not pricing logic. */
export const TIP_PERCENT_OPTIONS = [10, 15, 18, 20] as const;
export const DEFAULT_SIZE: PizzaSize = 'Medium';
export const DEFAULT_CRUST = 'Classic';

export type CartLine = { id: string; size: PizzaSize; crust: string; toppings: string[]; quantity: number };
export type TipMode = 'None' | 'Percent' | 'Custom';
export type TipDraft = { mode: TipMode; percent: number; customText: string };
export type CartState = {
  lines: CartLine[];
  /** The line the builder is editing. Edits apply to it immediately, so the running total always moves with them. */
  selectedId: string | null;
  promoCode: string;
  tip: TipDraft;
  nextId: number;
};

export type CartAction =
  | { type: 'addLine' }
  | { type: 'duplicateLine'; id: string }
  | { type: 'selectLine'; id: string }
  | { type: 'removeLine'; id: string }
  | { type: 'setSize'; size: PizzaSize }
  | { type: 'setCrust'; crust: string }
  | { type: 'toggleTopping'; toppingId: string }
  | { type: 'setQuantity'; id: string; quantity: number }
  | { type: 'setPromo'; code: string }
  | { type: 'setTipMode'; mode: TipMode }
  | { type: 'setTipPercent'; percent: number }
  | { type: 'setCustomTip'; text: string }
  /** Replace the draft with an existing order's contents, to edit it. */
  | { type: 'reset' };

export function createInitialState(): CartState {
  return {
    lines: [{ id: 'line-1', size: DEFAULT_SIZE, crust: DEFAULT_CRUST, toppings: ['pepperoni'], quantity: 1 }],
    selectedId: 'line-1',
    promoCode: '',
    tip: { mode: 'None', percent: 15, customText: '' },
    nextId: 2,
  };
}

const clampQuantity = (quantity: number): number =>
  Number.isFinite(quantity) ? Math.min(MAX_QUANTITY, Math.max(1, Math.trunc(quantity))) : 1;

function updateSelected(state: CartState, change: (line: CartLine) => CartLine): CartState {
  if (state.selectedId === null) return state;
  return { ...state, lines: state.lines.map(line => (line.id === state.selectedId ? change(line) : line)) };
}

export function cartReducer(state: CartState, action: CartAction): CartState {
  switch (action.type) {
    case 'addLine': {
      if (state.lines.length >= MAX_LINES) return state;
      const id = `line-${state.nextId}`;
      const line: CartLine = { id, size: DEFAULT_SIZE, crust: DEFAULT_CRUST, toppings: [], quantity: 1 };
      return { ...state, lines: [...state.lines, line], selectedId: id, nextId: state.nextId + 1 };
    }
    case 'duplicateLine': {
      const source = state.lines.find(line => line.id === action.id);
      if (!source || state.lines.length >= MAX_LINES) return state;
      const id = `line-${state.nextId}`;
      const copy: CartLine = { ...source, id, toppings: [...source.toppings] };
      const index = state.lines.indexOf(source);
      const lines = [...state.lines.slice(0, index + 1), copy, ...state.lines.slice(index + 1)];
      return { ...state, lines, selectedId: id, nextId: state.nextId + 1 };
    }
    case 'selectLine':
      return state.lines.some(line => line.id === action.id) ? { ...state, selectedId: action.id } : state;
    case 'removeLine': {
      const index = state.lines.findIndex(line => line.id === action.id);
      if (index < 0) return state;
      const lines = state.lines.filter(line => line.id !== action.id);
      // Keep the selection if it is still valid; otherwise move to the neighbour that took its place.
      const selectedId = state.selectedId !== action.id
        ? state.selectedId
        : (lines[Math.min(index, lines.length - 1)]?.id ?? null);
      return { ...state, lines, selectedId };
    }
    case 'setSize':
      return updateSelected(state, line => ({ ...line, size: action.size }));
    case 'setCrust':
      return updateSelected(state, line => ({ ...line, crust: action.crust }));
    case 'toggleTopping':
      // A topping can appear at most once per pizza (INV UI-013 / server rule).
      return updateSelected(state, line => ({
        ...line,
        toppings: line.toppings.includes(action.toppingId)
          ? line.toppings.filter(id => id !== action.toppingId)
          : [...line.toppings, action.toppingId],
      }));
    case 'setQuantity':
      return { ...state, lines: state.lines.map(line => (line.id === action.id ? { ...line, quantity: clampQuantity(action.quantity) } : line)) };
    case 'setPromo':
      return { ...state, promoCode: action.code.toUpperCase() };
    case 'setTipMode':
      return { ...state, tip: { ...state.tip, mode: action.mode } };
    case 'setTipPercent':
      return { ...state, tip: { ...state.tip, percent: action.percent } };
    case 'setCustomTip':
      return { ...state, tip: { ...state.tip, customText: action.text } };
    case 'reset':
      return createInitialState();
  }
}

export const selectedLine = (state: CartState): CartLine | undefined =>
  state.lines.find(line => line.id === state.selectedId);

/** Converts the tip the customer chose into the API shape. A custom amount stays exactly as entered. */
export function toTipRequest(tip: TipDraft): TipRequest {
  if (tip.mode === 'Percent') return { type: 'Percent', percent: tip.percent };
  if (tip.mode === 'Custom') {
    const amount = tip.customText.trim() === '' ? 0 : Number(tip.customText);
    return { type: 'Custom', amount: Number.isFinite(amount) ? amount : 0 };
  }
  return { type: 'None' };
}

export function toOrderItems(state: CartState): PizzaOrderItemRequest[] {
  return state.lines.map(({ size, crust, toppings, quantity }) => ({ size, crust, toppings: [...toppings], quantity }));
}

export function toPricingRequest(state: CartState): PricingPreviewRequest {
  return { items: toOrderItems(state), promoCode: state.promoCode.trim() || null, tip: toTipRequest(state.tip) };
}

/** Readable topping names for a line. Purely descriptive; contains no prices. */
export function describeToppings(line: CartLine, menu: PizzaMenu | null): string {
  if (line.toppings.length === 0) return 'Cheese only';
  return line.toppings.map(id => menu?.toppings.find(topping => topping.id === id)?.name ?? id).join(', ');
}
