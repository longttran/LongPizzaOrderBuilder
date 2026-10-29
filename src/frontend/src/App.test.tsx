import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from './api/pizza';
import App from './App';
import { acceptedOrder, deferred, fakeServerPricing, menu, orderWith } from './test/fixtures';

vi.mock('./api/pizza', async importOriginal => ({
  ...(await importOriginal<typeof import('./api/pizza')>()),
  getMenu: vi.fn(), previewPricing: vi.fn(), placeOrder: vi.fn(), getOrder: vi.fn(), cancelOrder: vi.fn(),
}));
const getMenu = vi.mocked(api.getMenu);
const previewPricing = vi.mocked(api.previewPricing);
const placeOrder = vi.mocked(api.placeOrder);
const getOrder = vi.mocked(api.getOrder);
const cancelOrder = vi.mocked(api.cancelOrder);

beforeEach(() => {
  getMenu.mockReset().mockResolvedValue(menu);
  previewPricing.mockReset().mockImplementation(async request => fakeServerPricing(request));
  placeOrder.mockReset();
  getOrder.mockReset().mockImplementation(async () => orderWith('NotStarted'));
  cancelOrder.mockReset();
});

async function setup(statusPollMs = 60_000) {
  const user = userEvent.setup();
  render(<App previewDebounceMs={0} statusPollMs={statusPollMs} />);
  await screen.findByRole('radio', { name: /Medium/ });
  await expectTotal('$8.80'); // default draft: Medium + pepperoni under the fake server's rules
  return user;
}
const total = () => screen.getByTestId('order-total');
const expectTotal = (text: string) => waitFor(() => expect(total()).toHaveTextContent(text));
const placeButton = () => screen.getByRole('button', { name: /Place my order/ });
const topping = (name: RegExp) => screen.getByRole('button', { name });

describe('UI-012: the running total follows every change to the pizza being built', () => {
  it('updates after each size and topping change', async () => {
    const user = await setup();
    await user.click(screen.getByRole('radio', { name: /Large/ }));
    await expectTotal('$14.30');
    await user.click(topping(/^Bacon/));
    await expectTotal('$15.40');
    await user.click(topping(/^Pepperoni/));
    await expectTotal('$14.30');
  });

  it('builds several pizzas and prices them together', async () => {
    const user = await setup();
    await user.click(screen.getByRole('button', { name: /Add another pizza/ }));
    await user.click(screen.getByRole('radio', { name: /Large/ }));
    await expectTotal('$22.00');
    expect(screen.getAllByRole('listitem').filter(item => item.className.includes('cart-line'))).toHaveLength(2);
  });
});

describe('UI-001: every amount shown is the server’s, never computed in the browser', () => {
  it('shows the server’s line total, tax rate and total even though they differ from the menu’s rules', async () => {
    await setup();
    // Menu data alone (Medium $7.00 + 8% pepperoni, 6.5% tax) would give different numbers.
    expect(screen.getByLabelText('Pizza 1: Medium Classic total')).toHaveTextContent('$8.00');
    expect(screen.getByText('Tax (10%)')).toBeInTheDocument();
    expect(screen.queryByText(/6\.5/)).not.toBeInTheDocument();
    const row = (label: string) => screen.getByText(label).parentElement!;
    expect(row('Subtotal')).toHaveTextContent('$8.00');
    expect(row('Tax (10%)')).toHaveTextContent('$0.80');
  });

  it('shows the server’s tip amount and total for a percentage tip', async () => {
    const user = await setup();
    await user.selectOptions(screen.getByLabelText('Tip'), 'Percent');
    await expectTotal('$10.12'); // server: 15% of ($8.00 + $0.80) = $1.32
    expect(within(total().closest('.breakdown') as HTMLElement).getByText('Tip').parentElement).toHaveTextContent('$1.32');
  });
});

describe('UI-002: a stale price is never presented as current', () => {
  it('marks the total as updating and blocks ordering until the new price arrives', async () => {
    const user = await setup();
    await user.type(screen.getByLabelText('Name for the order'), 'Sam');
    expect(placeButton()).toBeEnabled();

    const next = deferred<api.PricingPreviewResponse>();
    previewPricing.mockReturnValueOnce(next.promise);
    await user.click(screen.getByRole('radio', { name: /Large/ }));

    expect(await screen.findByText('Updating…')).toBeInTheDocument();
    expect(total().closest('.is-stale')).not.toBeNull();
    expect(placeButton()).toBeDisabled();
    expect(screen.queryByRole('button', { name: /View order details/ })).not.toBeInTheDocument();

    next.resolve(fakeServerPricing({ items: [{ size: 'Large', crust: 'Classic', toppings: ['pepperoni'], quantity: 1 }] }));
    await expectTotal('$14.30');
    expect(total().closest('.is-stale')).toBeNull();
    expect(screen.getByText('Server-calculated estimate')).toBeInTheDocument();
    expect(placeButton()).toBeEnabled();
  });

  it('shows a placeholder for a pizza whose price has not been calculated yet, not an old price', async () => {
    const user = await setup();
    previewPricing.mockReturnValueOnce(new Promise(() => {}));
    await user.click(screen.getByRole('radio', { name: /Large/ }));
    expect(screen.getByLabelText('Pizza 1: Large Classic total')).toHaveTextContent('…');
  });
});

describe('UI-004: an empty order shows zero and cannot be submitted', () => {
  it('returns to $0.00, disables checkout and sends no further pricing request', async () => {
    const user = await setup();
    await user.type(screen.getByLabelText('Name for the order'), 'Sam');
    const callsBefore = previewPricing.mock.calls.length;

    await user.click(screen.getByRole('button', { name: 'Remove Pizza 1: Medium Classic' }));

    expect(screen.getByText('Your order is empty')).toBeInTheDocument();
    expect(total()).toHaveTextContent('$0.00');
    expect(screen.getByText('Add a pizza to see your total.')).toBeInTheDocument();
    expect(placeButton()).toBeDisabled();
    expect(screen.queryByRole('button', { name: /View order details/ })).not.toBeInTheDocument();
    await new Promise(resolve => setTimeout(resolve, 30));
    expect(previewPricing.mock.calls.length).toBe(callsBefore);

    await user.click(screen.getByRole('button', { name: 'Start a pizza' }));
    await waitFor(() => expect(previewPricing.mock.calls.length).toBeGreaterThan(callsBefore));
  });
});

describe('UI-005: removing a pizza removes its whole contribution', () => {
  it('reprices from the remaining pizzas only', async () => {
    const user = await setup();
    await user.click(screen.getByRole('button', { name: /Add another pizza/ }));
    await user.click(screen.getByRole('radio', { name: /Large/ }));
    await expectTotal('$22.00');

    await user.click(screen.getByRole('button', { name: 'Remove Pizza 1: Medium Classic' }));
    await expectTotal('$13.20'); // one Large, no toppings
    expect(screen.queryByText(/Medium Classic/)).not.toBeInTheDocument();
  });
});

describe('UI-006: failures are shown to the customer, not swallowed', () => {
  it('shows the server’s validation message and withholds the total', async () => {
    const user = await setup();
    await user.type(screen.getByLabelText('Name for the order'), 'Sam');
    await user.selectOptions(screen.getByLabelText('Tip'), 'Custom');
    await user.type(screen.getByLabelText('Custom tip amount'), '-2');

    expect(await screen.findByRole('alert')).toHaveTextContent('Custom tip must be between $0 and $10,000.');
    expect(total()).toHaveTextContent('—');
    expect(placeButton()).toBeDisabled();
    expect(screen.queryByRole('button', { name: /View order details/ })).not.toBeInTheDocument();
  });

  it('says so when the pricing service cannot be reached, and shows no made-up total', async () => {
    previewPricing.mockRejectedValue(new api.ApiError('down'));
    render(<App previewDebounceMs={0} />);
    expect(await screen.findByRole('alert')).toHaveTextContent('can’t reach the pricing service');
    expect(total()).toHaveTextContent('—');
    expect(placeButton()).toBeDisabled();
  });

  it('says so when the menu cannot be loaded', async () => {
    getMenu.mockRejectedValue(new api.ApiError('down'));
    render(<App previewDebounceMs={0} />);
    expect(await screen.findByRole('alert')).toHaveTextContent('couldn’t load the menu');
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
  });
});

describe('UI-007: the promotion verdict comes from the server and is re-checked after every edit', () => {
  it('keeps the code visible, explains why it is not applied, and updates as the order changes', async () => {
    const user = await setup();
    const code = screen.getByLabelText('Promotion code');
    await user.type(code, 'save5');
    expect(code).toHaveValue('SAVE5');
    expect(await screen.findByText(/Not applied: SAVE5 requires a pre-discount subtotal greater than \$20\.00\./)).toBeInTheDocument();

    const increase = screen.getByRole('button', { name: 'Increase quantity of Pizza 1: Medium Classic' });
    await user.click(increase);
    await user.click(increase); // quantity 3 -> subtotal 24
    expect(await screen.findByText('$5 discount applied.')).toBeInTheDocument();
    expect(screen.getByText('Discount (SAVE5)')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Decrease quantity of Pizza 1: Medium Classic' })); // subtotal 16
    expect(await screen.findByText(/Not applied: SAVE5 requires/)).toBeInTheDocument();
    expect(screen.queryByText('Discount (SAVE5)')).not.toBeInTheDocument();
    expect(code).toHaveValue('SAVE5');
  });

  it('explains an unrecognised code', async () => {
    const user = await setup();
    await user.type(screen.getByLabelText('Promotion code'), 'NOPE');
    expect(await screen.findByText('Not applied: This promotion code is not recognized.')).toBeInTheDocument();
  });
});

describe('UI-008: order details appear only on request and have no side effects', () => {
  it('is hidden by default, opens on request, and closes with Escape returning focus', async () => {
    const user = await setup();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    const opener = screen.getByRole('button', { name: /View order details/ });
    await user.click(opener);
    const dialog = screen.getByRole('dialog', { name: 'Order details' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(within(dialog).getByText('1 × Medium Classic')).toBeInTheDocument();
    expect(within(dialog).getByText('$8.80')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Close' })).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
    expect(total()).toHaveTextContent('$8.80');
    expect(placeOrder).not.toHaveBeenCalled();
  });

  it('keeps keyboard focus inside the dialog while it is open', async () => {
    const user = await setup();
    await user.click(screen.getByRole('button', { name: /View order details/ }));
    const close = within(screen.getByRole('dialog')).getByRole('button', { name: 'Close' });
    await user.tab();
    expect(close).toHaveFocus();
    await user.tab({ shift: true });
    expect(close).toHaveFocus();
  });
});

describe('UI-009: submitting sends choices only, and only when the order can be priced', () => {
  it('sends no prices, then shows the total the server accepted', async () => {
    placeOrder.mockResolvedValue(acceptedOrder('Sam', 9.99));
    const user = await setup();
    expect(placeButton()).toBeDisabled(); // no name yet
    await user.type(screen.getByLabelText('Name for the order'), '  Sam  ');
    await user.click(placeButton());

    await waitFor(() => expect(placeOrder).toHaveBeenCalledTimes(1));
    const sent = placeOrder.mock.calls[0][0];
    expect(sent).toEqual({
      customerName: 'Sam',
      items: [{ size: 'Medium', crust: 'Classic', toppings: ['pepperoni'], quantity: 1 }],
      promoCode: null,
      tip: { type: 'None' },
    });
    expect(JSON.stringify(sent)).not.toMatch(/total|price|subtotal|tax|discount/i);

    expect(await screen.findByText('Order #ABCDEF01')).toBeInTheDocument();
    expect(screen.getByText('Not Started')).toBeInTheDocument();
    expect(screen.getByText(/Total: \$9\.99/)).toBeInTheDocument(); // the server's accepted total, not ours
    expect(screen.getByLabelText('Name for the order')).toHaveValue('');
  });
});

describe('UI-011: an unknown outcome is not reported as a plain failure', () => {
  it('warns the order may have been received when the connection drops', async () => {
    placeOrder.mockRejectedValue(new TypeError('Failed to fetch'));
    const user = await setup();
    await user.type(screen.getByLabelText('Name for the order'), 'Sam');
    await user.click(placeButton());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('may or may not have been received');
    expect(alert).not.toHaveTextContent(/could(n’t| not) place/i);
  });

  it('shows the server’s reason when the order is clearly rejected', async () => {
    placeOrder.mockRejectedValue(new api.ApiError('Customer name must be 80 characters or fewer.', 400));
    const user = await setup();
    await user.type(screen.getByLabelText('Name for the order'), 'Sam');
    await user.click(placeButton());
    expect(await screen.findByRole('alert')).toHaveTextContent('Customer name must be 80 characters or fewer.');
    expect(placeButton()).toBeEnabled();
  });
});

describe('UI-014: pickers work with the keyboard and expose their state', () => {
  it('moves the size with arrow keys and reflects it in aria-checked', async () => {
    const user = await setup();
    const medium = screen.getByRole('radio', { name: /Medium/ });
    expect(medium).toHaveAttribute('aria-checked', 'true');
    medium.focus();
    await user.keyboard('{ArrowRight}');
    expect(screen.getByRole('radio', { name: /Large/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: /Large/ })).toHaveFocus();
    await expectTotal('$14.30');
  });

  it('exposes topping selection through aria-pressed', async () => {
    const user = await setup();
    expect(topping(/^Pepperoni/)).toHaveAttribute('aria-pressed', 'true');
    expect(topping(/^Bacon/)).toHaveAttribute('aria-pressed', 'false');
    await user.click(topping(/^Bacon/));
    expect(topping(/^Bacon/)).toHaveAttribute('aria-pressed', 'true');
  });
});


async function placeAnOrder(user: ReturnType<typeof userEvent.setup>, name = 'Sam') {
  await user.type(screen.getByLabelText('Name for the order'), name);
  await user.click(placeButton());
}
const keyOf = (call: number) => placeOrder.mock.calls[call][1]?.idempotencyKey;

describe('UI-015: pressing Place order again after an unknown outcome cannot create a second order', () => {
  const timeout = () => new api.ApiError('The server did not answer in time.');

  it('retries the same order with the same key, so the server can recognise it', async () => {
    placeOrder.mockRejectedValueOnce(timeout()).mockResolvedValueOnce(acceptedOrder('Sam', 8.8));
    const user = await setup();
    await placeAnOrder(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('same order reference');

    await user.click(placeButton());
    expect(await screen.findByText('Order #ABCDEF01')).toBeInTheDocument();

    expect(placeOrder).toHaveBeenCalledTimes(2);
    expect(keyOf(0)).toBeTruthy();
    expect(keyOf(1)).toBe(keyOf(0));
    expect(placeOrder.mock.calls[1][0]).toEqual(placeOrder.mock.calls[0][0]);   // and it is the very same request
  });

  it('uses a new key if the customer changed the order after the failure', async () => {
    placeOrder.mockRejectedValueOnce(timeout()).mockResolvedValueOnce(acceptedOrder('Sam', 14.3));
    const user = await setup();
    await placeAnOrder(user);
    await screen.findByRole('alert');

    await user.click(screen.getByRole('radio', { name: /Large/ }));
    await expectTotal('$14.30');
    await user.click(placeButton());
    await screen.findByText('Order #ABCDEF01');

    expect(keyOf(1)).not.toBe(keyOf(0));   // a different request must not reuse the key
  });

  it('uses a new key if the customer changed their name after the failure', async () => {
    placeOrder.mockRejectedValueOnce(timeout()).mockResolvedValueOnce(acceptedOrder('Sam B', 8.8));
    const user = await setup();
    await placeAnOrder(user);
    await screen.findByRole('alert');

    await user.type(screen.getByLabelText('Name for the order'), ' B');
    await user.click(placeButton());
    await screen.findByText('Order #ABCDEF01');
    expect(keyOf(1)).not.toBe(keyOf(0));
  });

  it('keeps the key after a server error, because the order may have been created', async () => {
    placeOrder.mockRejectedValueOnce(new api.ApiError('Order request failed (500)', 500)).mockResolvedValueOnce(acceptedOrder('Sam', 8.8));
    const user = await setup();
    await placeAnOrder(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('may or may not have been received');
    await user.click(placeButton());
    await screen.findByText('Order #ABCDEF01');
    expect(keyOf(1)).toBe(keyOf(0));
  });

  it('starts fresh after the server clearly refused the order', async () => {
    placeOrder.mockRejectedValueOnce(new api.ApiError('Customer name is required.', 400)).mockResolvedValueOnce(acceptedOrder('Sam', 8.8));
    const user = await setup();
    await placeAnOrder(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('Customer name is required.');
    await user.click(placeButton());
    await screen.findByText('Order #ABCDEF01');
    expect(keyOf(1)).not.toBe(keyOf(0));   // nothing was created, so there is nothing to protect
  });

  it('gives the next order after a successful one its own key', async () => {
    placeOrder.mockResolvedValue(acceptedOrder('Sam', 8.8));
    const user = await setup();
    await placeAnOrder(user);
    await screen.findByText('Order #ABCDEF01');
    await expectTotal('$8.80');
    await placeAnOrder(user, 'Sam');
    await waitFor(() => expect(placeOrder).toHaveBeenCalledTimes(2));
    expect(keyOf(1)).not.toBe(keyOf(0));
  });

  it('does not allow a second press while the first request is still in flight', async () => {
    const slow = deferred<api.PizzaOrder>();
    placeOrder.mockReturnValueOnce(slow.promise);
    const user = await setup();
    await placeAnOrder(user);
    expect(screen.getByRole('button', { name: /Placing order/ })).toBeDisabled();
    expect(placeOrder).toHaveBeenCalledTimes(1);
    slow.resolve(acceptedOrder('Sam', 8.8));
    await screen.findByText('Order #ABCDEF01');
  });
});

describe('UI-016: the status comes from the server, and cancelling tells the truth', () => {
  async function withPlacedOrder(order = orderWith('NotStarted'), pollMs = 60_000) {
    placeOrder.mockResolvedValue(order);
    const user = await setup(pollMs);
    await placeAnOrder(user);
    await screen.findByText('Order #ABCDEF01');
    return user;
  }
  const cancelButton = () => screen.queryByRole('button', { name: 'Cancel this order' });

  it('shows the status in words and offers cancel while the server allows it', async () => {
    await withPlacedOrder();
    expect(screen.getByText('Not Started')).toBeInTheDocument();
    expect(screen.getByText(/restaurant hasn’t started yet/)).toBeInTheDocument();
    expect(cancelButton()).toBeInTheDocument();
  });

  it('cancels, shows Cancelled, and removes the cancel button', async () => {
    cancelOrder.mockResolvedValue(orderWith('Cancelled'));
    const user = await withPlacedOrder();
    await user.click(cancelButton()!);

    expect(await screen.findByText('Cancelled')).toBeInTheDocument();
    expect(cancelOrder).toHaveBeenCalledWith('abcdef0123456789');
    expect(cancelButton()).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Dismiss' })).toBeInTheDocument();
  });

  it('when the restaurant got there first, shows the server’s reason and the real status instead of a false success', async () => {
    cancelOrder.mockRejectedValue(new api.ApiError(
      'The restaurant has already started this order, so it can no longer be cancelled.', 409,
      ['The restaurant has already started this order, so it can no longer be cancelled.'], orderWith('Started')));
    const user = await withPlacedOrder();
    await user.click(cancelButton()!);

    expect(await screen.findByRole('alert')).toHaveTextContent('restaurant has already started');
    expect(screen.getByText('Started')).toBeInTheDocument();
    expect(screen.queryByText('Cancelled')).not.toBeInTheDocument();
    expect(cancelButton()).not.toBeInTheDocument();
  });

  it('when the cancel cannot be confirmed, says so, checks the real status, and allows a safe retry', async () => {
    cancelOrder.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    getOrder.mockResolvedValue(orderWith('NotStarted'));
    const user = await withPlacedOrder();
    await user.click(cancelButton()!);

    expect(await screen.findByRole('alert')).toHaveTextContent('couldn’t confirm the cancellation');
    await waitFor(() => expect(getOrder).toHaveBeenCalledWith('abcdef0123456789'));
    expect(screen.getByText('Not Started')).toBeInTheDocument();     // still what the server says
    expect(cancelButton()).toBeEnabled();                         // pressing again is safe

    cancelOrder.mockResolvedValueOnce(orderWith('Cancelled'));
    await user.click(cancelButton()!);
    expect(await screen.findByText('Cancelled')).toBeInTheDocument();
  });

});

describe('UI-017: the status follows the kitchen without moving backwards', () => {
  it('updates by itself when the kitchen starts, and withdraws the cancel button', async () => {
    getOrder.mockResolvedValue(orderWith('Started'));
    placeOrder.mockResolvedValue(orderWith('NotStarted'));
    const user = await setup(15);
    await placeAnOrder(user);

    expect(await screen.findByText('Started')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel this order' })).not.toBeInTheDocument();
  });

  it('keeps the last known status and says so when the status cannot be refreshed', async () => {
    getOrder.mockRejectedValue(new api.ApiError('down'));
    placeOrder.mockResolvedValue(orderWith('NotStarted'));
    const user = await setup(15);
    await placeAnOrder(user);

    expect(await screen.findByText(/couldn’t refresh the status/)).toBeInTheDocument();
    expect(screen.getByText('Not Started')).toBeInTheDocument();
  });

  it('lists the order history, with the restaurant’s steps marked', async () => {
    getOrder.mockResolvedValue(orderWith('Delivered'));
    placeOrder.mockResolvedValue(orderWith('NotStarted'));
    const user = await setup(15);
    await placeAnOrder(user);

    expect(await screen.findByText('Delivered')).toBeInTheDocument();
    const items = screen.getAllByRole('listitem').filter(item => /at \d/.test(item.textContent ?? ''));
    expect(items.map(item => item.textContent)).toEqual([
      expect.stringMatching(/^Placed at/), expect.stringMatching(/^Started at .*\(restaurant\)$/),
      expect.stringMatching(/^Ready for Pickup at .*\(restaurant\)$/), expect.stringMatching(/^Delivered at .*\(restaurant\)$/),
    ]);
  });
});
