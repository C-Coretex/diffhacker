import type { ComponentProps } from 'react';
import { ChevronDownIcon } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * A styled native `<select>`, deliberately not the Radix listbox.
 *
 * The only choice in this iteration is the provider type — six fixed options, no search, no
 * grouping. A native control gets the platform's own keyboard behaviour and its own popup
 * rendering for free, and keeps another Radix dependency out of the tree.
 */
export function Select({ className, children, ...props }: ComponentProps<'select'>) {
  return (
    <div className="relative">
      <select
        data-slot="select"
        className={cn(
          'border-input bg-background flex h-9 w-full appearance-none rounded-md border py-1 pr-9 pl-3 text-sm shadow-xs transition-[color,box-shadow] outline-none',
          'focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]',
          'disabled:cursor-not-allowed disabled:opacity-50',
          className,
        )}
        {...props}
      >
        {children}
      </select>
      <ChevronDownIcon
        aria-hidden
        className="text-muted-foreground pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2"
      />
    </div>
  );
}
