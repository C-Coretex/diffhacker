import type { ComponentProps } from 'react';
import { cn } from '@/lib/utils';

/**
 * A styled native `<input type="checkbox">`, deliberately not the Radix primitive.
 *
 * Same reasoning as `select.tsx`: this is one plain boolean with no indeterminate state and no
 * custom keyboard behaviour to reproduce, so the native control gets the platform's own focus
 * ring, its own screen-reader announcement and its own label association for free — and keeps
 * another Radix dependency out of the tree.
 */
export function Checkbox({ className, ...props }: ComponentProps<'input'>) {
  return (
    <input
      type="checkbox"
      data-slot="checkbox"
      className={cn(
        'border-input accent-primary size-4 shrink-0 cursor-pointer rounded-[4px] border',
        'focus-visible:ring-ring/50 focus-visible:ring-[3px] focus-visible:outline-none',
        'disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      {...props}
    />
  );
}
