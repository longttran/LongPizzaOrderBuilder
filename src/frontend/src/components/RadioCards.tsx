import { useRef, type KeyboardEvent, type ReactNode } from 'react';

type Option<T extends string> = { value: T; className?: string; content: ReactNode };

type Props<T extends string> = {
  label: string;
  value: T | undefined;
  options: Option<T>[];
  onChange: (value: T) => void;
  groupClassName: string;
  optionClassName: string;
};

/** Single-choice picker with the standard radio keyboard pattern (arrow keys move and select). */
export function RadioCards<T extends string>({ label, value, options, onChange, groupClassName, optionClassName }: Props<T>) {
  const group = useRef<HTMLDivElement>(null);
  const activeIndex = Math.max(0, options.findIndex(option => option.value === value));

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const step = ['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : ['ArrowLeft', 'ArrowUp'].includes(event.key) ? -1 : 0;
    if (step === 0) return;
    event.preventDefault();
    const next = (activeIndex + step + options.length) % options.length;
    onChange(options[next].value);
    group.current?.querySelectorAll<HTMLElement>('[role="radio"]')[next]?.focus();
  }

  return <div ref={group} role="radiogroup" aria-label={label} className={groupClassName} onKeyDown={onKeyDown}>
    {options.map((option, index) => {
      const checked = option.value === value;
      return <button
        type="button" role="radio" key={option.value} aria-checked={checked} tabIndex={index === activeIndex ? 0 : -1}
        className={`${optionClassName} ${checked ? 'selected' : ''} ${option.className ?? ''}`.trim()}
        onClick={() => onChange(option.value)}
      >{option.content}</button>;
    })}
  </div>;
}
