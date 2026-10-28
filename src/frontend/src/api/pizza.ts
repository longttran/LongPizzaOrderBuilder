const baseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080';

export type PizzaSize = 'Small' | 'Medium' | 'Large';
export type Crust = 'Classic' | 'Thin' | 'Gluten-free';
export type Topping = { id: string; name: string; category: 'Basic' | 'Premium'; rate: number };
export type PizzaMenu = {
  sizes: { name: PizzaSize; price: number }[];
  crusts: string[];
  toppings: Topping[];
  taxRate: number;
  promotions: { code: string; description: string; type: string; rate: number | null; amount: number | null }[];
};
export type TipRequest = { type: 'None' | 'Percent' | 'Custom'; percent?: number | null; amount?: number | null };
export type PizzaOrderItemRequest = { size: PizzaSize; crust: Crust; toppings: string[]; quantity: number };
export type PizzaOrderRequest = PizzaOrderItemRequest & {
  customerName: string;
  promoCode?: string | null;
  tip?: TipRequest | null;
};
export type PromotionEvaluation = { code: string | null; applied: boolean; eligible: boolean; message: string; discountAmount: number };
export type PricingBreakdown = {
  lines: { size: string; crust: string; toppings: string[]; quantity: number; baseUnitPrice: number; pricedToppings: { id: string; name: string; unitPrice: number; rate: number }[]; unitPrice: number; lineTotal: number }[];
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
export type PizzaOrder = {
  id: string;
  customerName: string;
  items: PizzaOrderItemRequest[];
  pricing: PricingBreakdown;
  total: number;
  status: string;
  createdAt: string;
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  });
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `API request failed (${response.status})`);
  }
  return response.json() as Promise<T>;
}

export const getMenu = () => request<PizzaMenu>('/api/menu');
export const previewPricing = (preview: PricingPreviewRequest) => request<PricingPreviewResponse>('/api/pricing/preview', {
  method: 'POST', body: JSON.stringify(preview),
});
export const placeOrder = (order: PizzaOrderRequest) => request<PizzaOrder>('/api/orders', {
  method: 'POST', body: JSON.stringify(order),
});
