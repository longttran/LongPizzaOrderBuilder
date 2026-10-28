import type { OrderEvent, OrderStatus, PizzaMenu, PizzaOrder, PizzaOrderItemRequest, PricingPreviewRequest, PricingPreviewResponse, PromotionEvaluation } from '../api/pizza';

export const menu: PizzaMenu = {
  sizes: [{ name: 'Small', price: 5 }, { name: 'Medium', price: 7 }, { name: 'Large', price: 12 }],
  crusts: ['Classic', 'Thin', 'Gluten-free'],
  toppings: [
    { id: 'pepperoni', name: 'Pepperoni', category: 'Basic', rate: 0.08 },
    { id: 'sausage', name: 'Sausage', category: 'Basic', rate: 0.08 },
    { id: 'bacon', name: 'Bacon', category: 'Premium', rate: 0.11 },
  ],
  taxRate: 0.065,
  promotions: [],
};

/**
 * A stand-in for the pricing server in UI tests. Its numbers are DELIBERATELY unlike the real rules
 * (a flat $1.00 per topping and 10% tax, while the menu says 8%/11% and 6.5%). If the UI ever
 * computed a price itself, these tests would show a different number than the one the "server" returned.
 */
const BASE: Record<string, number> = { Small: 5, Medium: 7, Large: 12 };
const cents = (value: number) => Math.round(value * 100) / 100;

export function fakeServerPricing(request: PricingPreviewRequest): PricingPreviewResponse {
  const none: PromotionEvaluation = { code: null, applied: false, eligible: true, message: 'No promotion selected.', discountAmount: 0 };
  const tip = request.tip;
  if (tip?.type === 'Custom' && (tip.amount ?? 0) < 0) {
    return { breakdown: null, promotion: none, errors: ['Custom tip must be between $0 and $10,000.'], isValid: false };
  }
  const lines = request.items.map(item => {
    const unitPrice = BASE[item.size] + item.toppings.length;
    return {
      size: item.size, crust: item.crust, toppings: item.toppings, quantity: item.quantity,
      baseUnitPrice: BASE[item.size], pricedToppings: item.toppings.map(id => ({ id, name: id, unitPrice: 1, rate: 0.1 })),
      unitPrice, lineTotal: unitPrice * item.quantity,
    };
  });
  const subtotal = lines.reduce((sum, line) => sum + line.lineTotal, 0);
  const code = request.promoCode?.trim().toUpperCase() ?? null;
  let promotion = none;
  let discount = 0;
  if (code === 'SAVE5') {
    const applied = subtotal > 20;
    promotion = { code, applied, eligible: applied, discountAmount: applied ? 5 : 0,
      message: applied ? '$5 discount applied.' : 'SAVE5 requires a pre-discount subtotal greater than $20.00.' };
    discount = promotion.discountAmount;
  } else if (code) {
    promotion = { code, applied: false, eligible: false, discountAmount: 0, message: 'This promotion code is not recognized.' };
  }
  const discounted = subtotal - discount;
  const taxAmount = cents(discounted * 0.1);
  const tipAmount = tip?.type === 'Percent' ? cents(((discounted + taxAmount) * (tip.percent ?? 0)) / 100)
    : tip?.type === 'Custom' ? cents(tip.amount ?? 0) : 0;
  return {
    breakdown: {
      lines, subtotal, promotion, discount, discountedSubtotal: discounted, taxRate: 0.1, tax: taxAmount, tip: tipAmount,
      total: cents(discounted + taxAmount + tipAmount), roundingPolicy: 'test double',
    },
    promotion, errors: [], isValid: true,
  };
}

const PATHS: Record<OrderStatus, OrderStatus[]> = {
  NotStarted: [],
  Started: ['Started'],
  ReadyForPickup: ['Started', 'ReadyForPickup'],
  Delivered: ['Started', 'ReadyForPickup', 'Delivered'],
  Cancelled: ['Cancelled'],
};

const DEFAULT_ITEMS: PizzaOrderItemRequest[] = [{ size: 'Medium', crust: 'Classic', toppings: ['pepperoni'], quantity: 1 }];

/** An order as the order service would return it in the given status, with a consistent history and version. */
export function orderWith(status: OrderStatus, options: { id?: string; name?: string; total?: number; items?: PizzaOrderItemRequest[]; updates?: number } = {}): PizzaOrder {
  const base = Date.parse('2026-10-29T12:00:00Z');
  const events: OrderEvent[] = [{ event: 'Placed', status: 'NotStarted', at: new Date(base).toISOString(), actor: 'Customer' }];
  for (let i = 0; i < (options.updates ?? 0); i++) events.push({ event: 'Updated', status: 'NotStarted', at: new Date(base + (i + 1) * 30_000).toISOString(), actor: 'Customer' });
  PATHS[status].forEach(to => events.push({ event: to, status: to, at: new Date(base + (events.length + 1) * 60_000).toISOString(), actor: to === 'Cancelled' ? 'Customer' : 'Restaurant' }));
  const items = options.items ?? DEFAULT_ITEMS;
  return {
    id: options.id ?? 'abcdef0123456789', customerName: options.name ?? 'Sam', items, promoCode: null, tip: null,
    status, total: options.total ?? 8.8, version: events.length, createdAt: events[0].at, updatedAt: events[events.length - 1].at, history: events,
    canCustomerChange: status === 'NotStarted',
    pricing: fakeServerPricing({ items, promoCode: null, tip: { type: 'None' } }).breakdown!,
  };
}

export const acceptedOrder = (customerName: string, total: number): PizzaOrder => orderWith('NotStarted', { name: customerName, total });

export function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}
