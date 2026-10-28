import type { Dispatch } from 'react';
import type { PromotionEvaluation } from '../api/pizza';
import { TIP_PERCENT_OPTIONS, type CartAction, type CartState, type TipMode } from '../domain/cart';

type Props = {
  state: CartState;
  dispatch: Dispatch<CartAction>;
  /** The server's verdict on the entered code, or undefined while it is being re-checked. */
  promotion: PromotionEvaluation | undefined;
};

export function PromoAndTip({ state, dispatch, promotion }: Props) {
  const code = state.promoCode.trim();
  return <>
    <label className="field-label" htmlFor="promoCode">Promotion code</label>
    <input id="promoCode" className="name-input" value={state.promoCode} maxLength={32} placeholder="ILOVEPIZZA or SAVE5"
      aria-describedby="promoStatus" onChange={event => dispatch({ type: 'setPromo', code: event.target.value })} />
    <p id="promoStatus" role="status" className={code && promotion && !promotion.applied ? 'error' : 'notice'}>
      {!code ? '' : !promotion ? 'Checking code…' : promotion.applied ? promotion.message : `Not applied: ${promotion.message}`}
    </p>

    <label className="field-label" htmlFor="tipMode">Tip</label>
    <select id="tipMode" className="name-input" value={state.tip.mode}
      onChange={event => dispatch({ type: 'setTipMode', mode: event.target.value as TipMode })}>
      <option value="None">No tip</option>
      <option value="Percent">Percentage tip</option>
      <option value="Custom">Custom amount</option>
    </select>
    {state.tip.mode === 'Percent' && <select className="name-input" aria-label="Tip percentage" value={state.tip.percent}
      onChange={event => dispatch({ type: 'setTipPercent', percent: Number(event.target.value) })}>
      {TIP_PERCENT_OPTIONS.map(percent => <option key={percent} value={percent}>{percent}%</option>)}
    </select>}
    {state.tip.mode === 'Custom' && <input className="name-input" aria-label="Custom tip amount" type="number" min="0" max="10000" step="0.01"
      value={state.tip.customText} placeholder="0.00" onChange={event => dispatch({ type: 'setCustomTip', text: event.target.value })} />}
  </>;
}
