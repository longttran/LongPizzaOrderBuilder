const formatter = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

/** Display-only. The UI never derives prices; it formats amounts the server returned (INV UI-001). */
export const money = (value: number): string => formatter.format(value);
