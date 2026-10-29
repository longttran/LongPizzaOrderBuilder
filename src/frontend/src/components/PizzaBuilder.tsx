import type { Dispatch } from 'react';
import type { PizzaMenu, PizzaSize } from '../api/pizza';
import type { CartAction, CartLine } from '../domain/cart';
import { money } from '../format';
import { blurbForSize, emojiForTopping, infoForCrust, toppingPriceHint } from './presentation';
import { RadioCards } from './RadioCards';

type Props = { menu: PizzaMenu; line: CartLine | undefined; dispatch: Dispatch<CartAction> };

export function PizzaBuilder({ menu, line, dispatch }: Props) {
  if (!line) {
    return <div className="empty-builder">
      <h2>Your order is empty</h2>
      <p className="notice">Start a pizza and your total will appear as you build it.</p>
      <button type="button" className="add-pizza" onClick={() => dispatch({ type: 'addLine' })}>Start a pizza</button>
    </div>;
  }
  return <>
    <div className="section-heading"><div><p className="eyebrow">STEP 01</p><h2>Choose your size</h2></div><span className="step-count">01 / 03</span></div>
    <RadioCards<PizzaSize>
      label="Pizza size" groupClassName="size-grid" optionClassName="size-card"
      value={line.size} onChange={size => dispatch({ type: 'setSize', size })}
      options={menu.sizes.map(size => ({
        value: size.name,
        content: <>
          <span className={`size-pizza ${size.name.toLowerCase()}`} aria-hidden="true">🍕</span>
          <strong>{size.name}</strong><span className="muted">{blurbForSize(size.name)}</span><b>{money(size.price)}</b>
        </>,
      }))}
    />
    <div className="section-heading next-heading"><div><p className="eyebrow">STEP 02</p><h2>Pick your crust</h2></div><span className="step-count">02 / 03</span></div>
    <RadioCards<string>
      label="Crust" groupClassName="crust-list" optionClassName="crust-option"
      value={line.crust} onChange={crust => dispatch({ type: 'setCrust', crust })}
      options={menu.crusts.map(crust => {
        const info = infoForCrust(crust);
        return { value: crust, content: <><span className="crust-icon" aria-hidden="true">{info.icon}</span><span><strong>{crust}</strong><small>{info.blurb}</small></span></> };
      })}
    />
    <div className="section-heading next-heading"><div><p className="eyebrow">STEP 03</p><h2>Load up the toppings</h2><p className="section-subtitle">Pick as many as you like. Each topping can be added once per pizza.</p></div><span className="step-count">03 / 03</span></div>
    <div className="topping-grid" role="group" aria-label="Toppings">
      {menu.toppings.map(topping => {
        const selected = line.toppings.includes(topping.id);
        return <button
          type="button" key={topping.id} aria-pressed={selected} className={`topping-card ${selected ? 'selected' : ''}`}
          onClick={() => dispatch({ type: 'toggleTopping', toppingId: topping.id })}
        >
          <span className="topping-emoji" aria-hidden="true">{emojiForTopping(topping.id)}</span>
          <span className="topping-name">{topping.name}</span>
          <span className="topping-price">+{money(toppingPriceHint(menu, line.size, topping))}</span>
          <span className="topping-check" aria-hidden="true">{selected ? '✓' : '+'}</span>
        </button>;
      })}
    </div>
  </>;
}
