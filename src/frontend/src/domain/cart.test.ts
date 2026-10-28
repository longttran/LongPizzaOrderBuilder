import { describe, expect, it } from 'vitest';
import { MAX_LINES, MAX_QUANTITY, cartReducer, createInitialState, selectedLine, toPricingRequest, toTipRequest, type CartAction, type CartState } from './cart';
import { findPricedLine } from './pricedLines';
import { fakeServerPricing } from '../test/fixtures';

const run = (actions: CartAction[], from: CartState = createInitialState()) => actions.reduce(cartReducer, from);

describe('UI-012: builder edits apply to the selected line immediately', () => {
  it('starts with one selected pizza so the running total is visible from the first moment', () => {
    const state = createInitialState();
    expect(state.lines).toHaveLength(1);
    expect(selectedLine(state)?.id).toBe(state.selectedId);
  });

  it('changes only the selected line', () => {
    const state = run([{ type: 'addLine' }, { type: 'setSize', size: 'Large' }, { type: 'setCrust', crust: 'Thin' }]);
    expect(state.lines.map(line => line.size)).toEqual(['Medium', 'Large']);
    expect(state.lines[1].crust).toBe('Thin');
    expect(state.lines[0].crust).toBe('Classic');
  });
});

describe('UI-013: each topping once per pizza; quantity stays within bounds', () => {
  it('toggling a topping twice removes it and never creates a duplicate', () => {
    const base = createInitialState();
    const added = run([{ type: 'toggleTopping', toppingId: 'bacon' }], base);
    expect(selectedLine(added)?.toppings).toEqual(['pepperoni', 'bacon']);
    expect(selectedLine(run([{ type: 'toggleTopping', toppingId: 'bacon' }], added))?.toppings).toEqual(['pepperoni']);
    for (const state of [added, run([{ type: 'toggleTopping', toppingId: 'pepperoni' }], added)]) {
      const toppings = selectedLine(state)?.toppings ?? [];
      expect(new Set(toppings).size).toBe(toppings.length);
    }
  });

  it('never grows past the maximum number of pizza lines', () => {
    let state = createInitialState();
    for (let n = 0; n < MAX_LINES + 5; n++) state = cartReducer(state, { type: 'addLine' });
    expect(state.lines).toHaveLength(MAX_LINES);
    expect(cartReducer(state, { type: 'duplicateLine', id: state.lines[0].id })).toBe(state);
  });

  it.each([[0, 1], [-4, 1], [3.9, 3], [MAX_QUANTITY + 5, MAX_QUANTITY], [Number.NaN, 1]])('quantity %s becomes %s', (input, expected) => {
    const state = createInitialState();
    expect(run([{ type: 'setQuantity', id: state.lines[0].id, quantity: input }], state).lines[0].quantity).toBe(expected);
  });
});

describe('UI-005: removing a line removes its whole contribution and keeps the selection valid', () => {
  const twoLines = () => run([{ type: 'addLine' }]);

  it('drops the line from the pricing request entirely', () => {
    const state = twoLines();
    const removed = run([{ type: 'removeLine', id: state.lines[0].id }], state);
    expect(toPricingRequest(removed).items).toHaveLength(1);
    expect(removed.lines.some(line => line.id === state.lines[0].id)).toBe(false);
  });

  it('moves the selection to a remaining line when the selected one is removed', () => {
    const state = twoLines();
    const removed = run([{ type: 'removeLine', id: state.selectedId! }], state);
    expect(removed.selectedId).toBe(removed.lines[0].id);
  });

  it('leaves an empty order with no selection after the last line is removed', () => {
    const state = createInitialState();
    const removed = run([{ type: 'removeLine', id: state.lines[0].id }], state);
    expect(removed.lines).toEqual([]);
    expect(removed.selectedId).toBeNull();
    expect(run([{ type: 'setSize', size: 'Large' }], removed)).toBe(removed);
  });

  it('duplicating copies the toppings instead of sharing them', () => {
    const state = run([{ type: 'duplicateLine', id: 'line-1' }, { type: 'toggleTopping', toppingId: 'bacon' }]);
    expect(state.lines[0].toppings).toEqual(['pepperoni']);
    expect(state.lines[1].toppings).toEqual(['pepperoni', 'bacon']);
  });
});

describe('UI-010: tip choices map to the API without changing the amount', () => {
  it('maps none, percent and custom', () => {
    expect(toTipRequest({ mode: 'None', percent: 15, customText: '9' })).toEqual({ type: 'None' });
    expect(toTipRequest({ mode: 'Percent', percent: 18, customText: '' })).toEqual({ type: 'Percent', percent: 18 });
    expect(toTipRequest({ mode: 'Custom', percent: 15, customText: '3.50' })).toEqual({ type: 'Custom', amount: 3.5 });
  });

  it('keeps a custom amount fixed when the order changes', () => {
    const state = run([{ type: 'setTipMode', mode: 'Custom' }, { type: 'setCustomTip', text: '4' }, { type: 'addLine' }, { type: 'setQuantity', id: 'line-1', quantity: 5 }]);
    expect(toPricingRequest(state).tip).toEqual({ type: 'Custom', amount: 4 });
  });

  it('passes a negative custom tip through so the server can reject it', () => {
    expect(toTipRequest({ mode: 'Custom', percent: 15, customText: '-2' })).toEqual({ type: 'Custom', amount: -2 });
  });

  it('treats blank custom input as zero', () => {
    expect(toTipRequest({ mode: 'Custom', percent: 15, customText: '  ' })).toEqual({ type: 'Custom', amount: 0 });
  });
});

describe('UI-002: a price is matched to a line by configuration, never by position', () => {
  it('finds the server price for an unchanged line and none for a changed one', () => {
    const before = createInitialState();
    const breakdown = fakeServerPricing(toPricingRequest(before)).breakdown;
    expect(findPricedLine(before.lines[0], breakdown)?.lineTotal).toBe(8);
    const changed = run([{ type: 'setSize', size: 'Large' }], before);
    expect(findPricedLine(changed.lines[0], breakdown)).toBeUndefined();
  });
});

describe('UI-018: an existing order can be loaded into the builder to be changed', () => {
  it('replaces the draft with the order’s items, promo and tip, as separate copies', () => {
    const items = [{ size: 'Large' as const, crust: 'Thin', toppings: ['bacon'], quantity: 2 }, { size: 'Small' as const, crust: 'Classic', toppings: [], quantity: 1 }];
    const state = cartReducer(createInitialState(), { type: 'load', items, promoCode: 'SAVE5', tip: { type: 'Percent', percent: 18 } });

    expect(state.lines.map(line => [line.size, line.crust, line.toppings, line.quantity])).toEqual([['Large', 'Thin', ['bacon'], 2], ['Small', 'Classic', [], 1]]);
    expect(state.selectedId).toBe(state.lines[0].id);
    expect(state.promoCode).toBe('SAVE5');
    expect(state.tip).toEqual({ mode: 'Percent', percent: 18, customText: '' });
    expect(new Set(state.lines.map(line => line.id)).size).toBe(2);

    const edited = cartReducer(state, { type: 'toggleTopping', toppingId: 'pepperoni' });
    expect(items[0].toppings).toEqual(['bacon']);                       // editing the draft never changes the order object
    expect(toPricingRequest(edited).tip).toEqual({ type: 'Percent', percent: 18 });
  });

  it('restores a custom tip and no tip', () => {
    const items = [{ size: 'Small' as const, crust: 'Classic', toppings: [], quantity: 1 }];
    expect(cartReducer(createInitialState(), { type: 'load', items, promoCode: null, tip: { type: 'Custom', amount: 3.5 } }).tip).toEqual({ mode: 'Custom', percent: 15, customText: '3.5' });
    expect(cartReducer(createInitialState(), { type: 'load', items, promoCode: null, tip: null }).tip.mode).toBe('None');
  });
});
