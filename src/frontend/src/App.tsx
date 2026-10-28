import { useEffect, useRef, useState, type FormEvent } from 'react';
import { getMenu, placeOrder, previewPricing, type Crust, type PizzaMenu, type PizzaOrder, type PizzaSize, type PricingPreviewResponse, type TipRequest } from './api/pizza';

const money = (value: number) => new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);
const emojis: Record<string, string> = { Pepperoni: '🍕', 'Italian sausage': '🌭', Mushrooms: '🍄', 'Green peppers': '🫑', Olives: '🫒', Onions: '🧅', 'Extra cheese': '🧀', Basil: '🌿' };

export default function App() {
  const [menu, setMenu] = useState<PizzaMenu | null>(null);
  const [size, setSize] = useState<PizzaSize>('Medium');
  const [crust, setCrust] = useState<Crust>('Classic');
  const [toppings, setToppings] = useState<string[]>(['pepperoni']);
  const [quantity, setQuantity] = useState(1);
  const [customerName, setCustomerName] = useState('');
  const [order, setOrder] = useState<PizzaOrder | null>(null);
  const [pricing, setPricing] = useState<PricingPreviewResponse | null>(null);
  const [promoCode, setPromoCode] = useState('');
  const [tipType, setTipType] = useState<'None' | 'Percent' | 'Custom'>('None');
  const [tipPercent, setTipPercent] = useState(15);
  const [customTip, setCustomTip] = useState('');
  const previewSequence = useRef(0);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    getMenu().then(setMenu).catch(() => setError('Could not reach the menu API. Start the stack with docker compose up --build.')).finally(() => setLoading(false));
  }, []);

  const tip: TipRequest = tipType === 'Percent'
    ? { type: 'Percent', percent: tipPercent }
    : tipType === 'Custom'
      ? { type: 'Custom', amount: Number(customTip || 0) }
      : { type: 'None' };
  const breakdown = pricing?.breakdown;
  const total = breakdown?.total ?? 0;

  useEffect(() => {
    if (!menu) return;
    const sequence = ++previewSequence.current;
    previewPricing({ items: [{ size, crust, toppings, quantity }], promoCode: promoCode.trim() || null, tip })
      .then(result => { if (sequence === previewSequence.current) setPricing(result); })
      .catch(() => { if (sequence === previewSequence.current) setPricing(null); });
    return () => { previewSequence.current++; };
  }, [menu, size, crust, toppings, quantity, promoCode, tipType, tipPercent, customTip]);

  function toggleTopping(id: string) {
    setToppings(current => current.includes(id) ? current.filter(item => item !== id) : [...current, id]);
    setOrder(null);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError(''); setSubmitting(true); setOrder(null);
    try { setOrder(await placeOrder({ size, crust, toppings, quantity, customerName: customerName.trim(), promoCode: promoCode.trim() || null, tip })); }
    catch { setError('We could not place your order. Please check the API and try again.'); }
    finally { setSubmitting(false); }
  }

  return <main className="app-shell">
    <header className="topbar"><a className="brand" href="#top"><span className="brand-mark">🍕</span><span>Long<span className="brand-accent">Pizza</span>OrderBuilder</span></a><span className="order-tag"><span className="live-dot"/> FRESHLY BUILT</span></header>
    <section className="hero" id="top"><div className="hero-copy"><p className="eyebrow">YOUR PIZZA, YOUR RULES</p><h1>Make it a<br/><span>pizza night.</span></h1><p className="hero-subtitle">Start with your favorite size, choose a crust, then pile on the good stuff.</p><div className="hero-perks"><span>✦ Made your way</span><span>✦ Big flavor energy</span></div></div><div className="hero-art" aria-label="Illustration of a pepperoni pizza" role="img"><div className="pizza-shadow"/><div className="pizza"><div className="pizza-cheese"/><span className="pepperoni p1"/><span className="pepperoni p2"/><span className="pepperoni p3"/><span className="pepperoni p4"/><span className="pepperoni p5"/><span className="pepperoni p6"/><span className="basil b1">✦</span><span className="basil b2">✦</span></div><span className="floating-note">100% YOU</span></div></section>
    <form className="builder-layout" onSubmit={submit}>
      <div className="builder-main">
        <div className="section-heading"><div><p className="eyebrow">STEP 01</p><h2>Choose your size</h2></div><span className="step-count">01 / 04</span></div>
        {loading ? <p className="notice">Loading the menu…</p> : <div className="size-grid">{menu?.sizes.map(item => <button type="button" key={item.name} className={`size-card ${size === item.name ? 'selected' : ''}`} onClick={() => {setSize(item.name); setOrder(null);}}><span className={`size-pizza ${item.name.toLowerCase()}`}>🍕</span><strong>{item.name}</strong><span className="muted">{item.name === 'Small' ? '8 inch · just for you' : item.name === 'Medium' ? '12 inch · crowd favorite' : '14 inch · go big'}</span><b>{money(item.price)}</b>{size === item.name && <span className="selected-check">✓</span>}</button>)}</div>}
        <div className="section-heading next-heading"><div><p className="eyebrow">STEP 02</p><h2>Pick your crust</h2></div><span className="step-count">02 / 04</span></div>
        <div className="crust-list">{(menu?.crusts ?? ['Classic', 'Thin', 'Gluten-free']).map(option => <button type="button" key={option} className={`crust-option ${crust === option ? 'selected' : ''}`} onClick={() => {setCrust(option as Crust); setOrder(null);}}><span className="crust-icon">{option === 'Classic' ? '🥖' : option === 'Thin' ? '🫓' : '🌾'}</span><span><strong>{option}</strong><small>{option === 'Classic' ? 'Golden, soft & chewy' : option === 'Thin' ? 'Light, crisp & crunchy' : 'Gluten-free option'}</small></span><span className="radio-dot">{crust === option ? '✓' : ''}</span></button>)}</div>
        <div className="section-heading next-heading"><div><p className="eyebrow">STEP 03</p><h2>Load up the toppings</h2><p className="section-subtitle">Pick as many as you like. No judgment here.</p></div><span className="step-count">03 / 04</span></div>
        <div className="topping-grid">{menu?.toppings.map(item => { const unitPrice = (menu.sizes.find(s => s.name === size)?.price ?? 0) * item.rate; return <button type="button" key={item.id} className={`topping-card ${toppings.includes(item.id) ? 'selected' : ''}`} onClick={() => toggleTopping(item.id)}><span className="topping-emoji">{emojis[item.name] ?? '✨'}</span><span className="topping-name">{item.name}</span><span className="topping-price">+{money(unitPrice)}</span><span className="topping-check">{toppings.includes(item.id) ? '✓' : '+'}</span></button>; })}</div>
        <div className="section-heading next-heading"><div><p className="eyebrow">STEP 04</p><h2>Make it yours</h2></div><span className="step-count">04 / 04</span></div>
        <label className="field-label" htmlFor="customerName">Name for the order</label><input id="customerName" className="name-input" value={customerName} onChange={e => setCustomerName(e.target.value)} placeholder="e.g. Long" required maxLength={80}/>
      </div>
      <aside className="order-card"><div className="order-card-header"><span className="receipt-icon">✦</span><div><p className="eyebrow">THE GOOD STUFF</p><h2>Your pizza</h2></div></div><div className="order-preview"><span className="preview-emoji">🍕</span><div><strong>{size} {crust} Crust</strong><p>{toppings.length ? (menu?.toppings.filter(t => toppings.includes(t.id)).map(t => t.name).join(', ') || 'Classic cheese') : 'Classic cheese'}</p></div></div><div className="quantity-row"><span>Quantity</span><div className="quantity-control"><button type="button" aria-label="Decrease quantity" disabled={quantity <= 1} onClick={() => {setQuantity(q => q - 1);setOrder(null);}}>−</button><strong>{quantity}</strong><button type="button" aria-label="Increase quantity" disabled={quantity >= 10} onClick={() => {setQuantity(q => q + 1);setOrder(null);}}>+</button></div></div><div className="price-line"><span>Pizza</span><span>{money((menu?.sizes.find(item => item.name === size)?.price ?? 0) * quantity)}</span></div><div className="price-line"><span>Toppings</span><span>{money((menu?.toppings.filter(item => toppings.includes(item.id)).reduce((sum, item) => sum + (menu.sizes.find(s => s.name === size)?.price ?? 0) * item.rate, 0) ?? 0) * quantity)}</span></div><label className="field-label" htmlFor="promoCode">Promotion code</label><input id="promoCode" className="name-input" value={promoCode} onChange={e => setPromoCode(e.target.value.toUpperCase())} placeholder="ILOVEPIZZA or SAVE5" maxLength={32}/>{promoCode.trim() && pricing && <p className={pricing.promotion.applied ? 'notice' : 'error'} role="status">{pricing.promotion.message}</p>}<label className="field-label" htmlFor="tipType">Tip</label><select id="tipType" className="name-input" value={tipType} onChange={e => setTipType(e.target.value as 'None' | 'Percent' | 'Custom')}><option value="None">No tip</option><option value="Percent">Percentage tip</option><option value="Custom">Custom amount</option></select>{tipType === 'Percent' && <select className="name-input" aria-label="Tip percentage" value={tipPercent} onChange={e => setTipPercent(Number(e.target.value))}><option value={10}>10%</option><option value={15}>15%</option><option value={18}>18%</option><option value={20}>20%</option></select>}{tipType === 'Custom' && <input className="name-input" aria-label="Custom tip amount" type="number" min="0" max="10000" step="0.01" value={customTip} onChange={e => setCustomTip(e.target.value)} placeholder="0.00"/>}{breakdown && <><div className="price-line"><span>Subtotal</span><span>{money(breakdown.subtotal)}</span></div>{breakdown.discount > 0 && <div className="price-line"><span>Discount</span><span>−{money(breakdown.discount)}</span></div>}<div className="price-line"><span>Tax (6.5%)</span><span>{money(breakdown.tax)}</span></div>{breakdown.tip > 0 && <div className="price-line"><span>Tip</span><span>{money(breakdown.tip)}</span></div>}</>}<div className="total-line"><span>Total</span><strong>{money(total)}</strong></div><p className="tax-note">{pricing ? 'Server-calculated estimate' : 'Calculating total…'}</p><button className="place-order" type="submit" disabled={loading || submitting || !menu}>{submitting ? 'Placing order…' : 'Place my order'} <span>→</span></button><p className="secure-note">♡ Made fresh, just for you</p>{pricing && pricing.errors.length > 0 && <p className="error" role="alert">{pricing.errors.join(' ')}</p>}{error && <p className="error" role="alert">{error}</p>}{order && <div className="success-box" role="status"><strong>Order received! 🎉</strong><p>Thanks, {order.customerName}. Your order <b>#{order.id.slice(0, 8).toUpperCase()}</b> is in the queue.</p><span>Total: {money(order.total)}</span></div>}</aside>
    </form>
    <footer><span>🍕 LongPizzaOrderBuilder</span><span>Built for big cravings & good times.</span></footer>
  </main>;
}
