// Empty string (production image behind Nginx) means "same origin"; undefined means local dev.
// The order builder's API (menu and price preview) and the order service are two separate processes with their own addresses.
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080';
const orderServiceUrl = import.meta.env.VITE_ORDER_SERVICE_URL ?? 'http://localhost:8090';

export type PizzaSize = 'Small' | 'Medium' | 'Large';
export type Topping = { id: string; name: string; category: 'Basic' | 'Premium'; rate: number };
export type PizzaMenu = {
  sizes: { name: PizzaSize; price: number }[];
  crusts: string[];
  toppings: Topping[];
  taxRate: number;
  promotions: { code: string; description: string; type: string; rate: number | null; amount: number | null; minimumSubtotalExclusive?: number | null }[];
};
export type TipRequest = { type: 'None' | 'Percent' | 'Custom'; percent?: number | null; amount?: number | null };
export type PizzaOrderItemRequest = { size: PizzaSize; crust: string; toppings: string[]; quantity: number };
export type PizzaOrderRequest = {
  customerName: string;
  items: PizzaOrderItemRequest[];
  promoCode?: string | null;
  tip?: TipRequest | null;
};
export type PromotionEvaluation = { code: string | null; applied: boolean; eligible: boolean; message: string; discountAmount: number };
export type PricedLine = {
  size: string;
  crust: string;
  toppings: string[];
  quantity: number;
  baseUnitPrice: number;
  pricedToppings: { id: string; name: string; unitPrice: number; rate: number }[];
  unitPrice: number;
  lineTotal: number;
};
export type PricingBreakdown = {
  lines: PricedLine[];
  subtotal: number;
  promotion: PromotionEvaluation;
  discount: number;
  discountedSubtotal: number;
  taxRate: number;
  tax: number;
  tip: number;
  total: number;
  roundingPolicy: string;
};
export type PricingPreviewRequest = { items: PizzaOrderItemRequest[]; promoCode?: string | null; tip?: TipRequest | null };
export type PricingPreviewResponse = { breakdown: PricingBreakdown | null; promotion: PromotionEvaluation; errors: string[]; isValid: boolean };
/** The order service's statuses. Cancelled is the final status of an order the customer cancelled before it started. */
export type OrderStatus = 'NotStarted' | 'Started' | 'ReadyForPickup' | 'Delivered' | 'Cancelled';
/** An entry in an order's history: "Placed", "Updated", or the name of the status the order moved to. */
export type OrderEvent = { event: string; status: OrderStatus; at: string; actor: 'Customer' | 'Restaurant' };
export type PizzaOrder = {
  id: string;
  customerName: string;
  items: PizzaOrderItemRequest[];
  pricing: PricingBreakdown;
  total: number;
  status: OrderStatus;
  createdAt: string;
  updatedAt: string;
  history: OrderEvent[];
  /** The service decides whether the customer can still cancel, so the browser never copies that rule. */
  canCustomerCancel: boolean;
};

/** Thrown for transport failures and unexpected HTTP errors. Validation failures are NOT thrown (see previewPricing). */
export class ApiError extends Error {
  constructor(message: string, readonly status?: number, readonly errors: string[] = [], readonly order?: PizzaOrder) {
    super(message);
    this.name = 'ApiError';
  }
}

/** How long to wait for the server to answer an order request before treating the outcome as unknown. */
export const ORDER_TIMEOUT_MS = 10_000;

/** A timeout surfaces as an ApiError with no status, which callers treat as "outcome unknown". */
async function send(path: string, init?: RequestInit, timeoutMs?: number, base: string = baseUrl): Promise<Response> {
  const controller = new AbortController();
  const forwardAbort = () => controller.abort();
  init?.signal?.addEventListener('abort', forwardAbort);
  let timedOut = false;
  const timer = timeoutMs === undefined ? undefined : setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
  try {
    return await fetch(`${base}${path}`, {
      ...init,
      signal: controller.signal,
      headers: { 'Content-Type': 'application/json', ...init?.headers },
    });
  } catch (error) {
    if (timedOut) throw new ApiError('The server did not answer in time.');
    throw error;
  } finally {
    clearTimeout(timer);
    init?.signal?.removeEventListener('abort', forwardAbort);
  }
}

async function readJson<T>(response: Response): Promise<T | null> {
  try {
    return (await response.json()) as T;
  } catch {
    return null;
  }
}

export async function getMenu(signal?: AbortSignal): Promise<PizzaMenu> {
  const response = await send('/api/menu', { signal });
  const body = await readJson<PizzaMenu>(response);
  if (!response.ok || !body) throw new ApiError(`Menu request failed (${response.status})`, response.status);
  return body;
}

/**
 * The API answers 400 with a full PricingPreviewResponse when selections are invalid.
 * That is a normal, displayable outcome, so it is returned rather than thrown (INV UI-006).
 */
export async function previewPricing(preview: PricingPreviewRequest, signal?: AbortSignal): Promise<PricingPreviewResponse> {
  const response = await send('/api/pricing/preview', { method: 'POST', body: JSON.stringify(preview), signal });
  const body = await readJson<PricingPreviewResponse>(response);
  if ((response.ok || response.status === 400) && body && Array.isArray(body.errors)) return body;
  throw new ApiError(`Pricing request failed (${response.status})`, response.status);
}

export type OrderCallOptions = {
  /** One unique value per checkout attempt. Sending the same key again returns the original order instead of creating another. */
  idempotencyKey?: string;
  signal?: AbortSignal;
  timeoutMs?: number;
};

async function failure(response: Response, fallback: string): Promise<ApiError> {
  const body = await readJson<{ errors?: string[]; order?: PizzaOrder }>(response);
  const errors = Array.isArray(body?.errors) ? body.errors : [];
  return new ApiError(errors[0] ?? `${fallback} (${response.status})`, response.status, errors, body?.order);
}

/** Sends an order to the order service. Safe to repeat with the same idempotency key. */
export async function placeOrder(order: PizzaOrderRequest, options: OrderCallOptions = {}): Promise<PizzaOrder> {
  const headers: Record<string, string> = options.idempotencyKey ? { 'Idempotency-Key': options.idempotencyKey } : {};
  const response = await send('/api/orders', { method: 'POST', body: JSON.stringify(order), headers, signal: options.signal }, options.timeoutMs ?? ORDER_TIMEOUT_MS, orderServiceUrl);
  if (response.ok) {
    const body = await readJson<PizzaOrder>(response);
    if (body) return body;
  }
  throw await failure(response, 'Order request failed');
}

export async function getOrder(id: string, signal?: AbortSignal): Promise<PizzaOrder> {
  const response = await send(`/api/orders/${encodeURIComponent(id)}`, { signal }, undefined, orderServiceUrl);
  const body = await readJson<PizzaOrder>(response);
  if (!response.ok || !body) throw new ApiError(`Order lookup failed (${response.status})`, response.status);
  return body;
}

/**
 * Cancels as the customer. Succeeds (even if already cancelled), or throws an ApiError with status 409 and the order as it
 * now is when the restaurant got there first.
 */
export async function cancelOrder(id: string, signal?: AbortSignal): Promise<PizzaOrder> {
  const response = await send(`/api/orders/${encodeURIComponent(id)}/cancel`, { method: 'POST', signal }, ORDER_TIMEOUT_MS, orderServiceUrl);
  if (response.ok) {
    const body = await readJson<PizzaOrder>(response);
    if (body) return body;
  }
  throw await failure(response, 'Cancel request failed');
}
