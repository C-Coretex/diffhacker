import type { ReactNode, RefObject } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { PinIcon, PinOffIcon } from 'lucide-react';
import { useT } from '@/i18n/useT';
import { cn } from '@/lib/utils';
import type { HoverController } from './useHoverTarget';

/**
 * The frame every hover card is drawn in.
 *
 * One card, mounted once, moved to whatever the pointer is on — not one popover per node. At three
 * hundred boxes the difference between those two is the difference between a diagram and a
 * slideshow.
 *
 * Requirement 4 is four separate claims, and each one is a line here:
 *
 * - **Readable at any zoom**, because the card renders outside React Flow's transformed viewport.
 *   The diagram scales; this does not.
 * - **Never obscures what it describes**, because it is anchored to the element's own rectangle and
 *   offset from it, and Radix flips it to the other side rather than covering it when the window
 *   edge gets in the way.
 * - **Handles long text**, by scrolling inside a capped height rather than growing past the window.
 * - **Pinnable**, which is what makes scrolling and selecting possible at all: an unpinned card is
 *   the pointer's and closes when it leaves, a pinned one is the reviewer's until they say
 *   otherwise.
 *
 * The anchor is a zero-interaction `position: fixed` box holding the element's client rect. Simply
 * measuring the real element and drawing beside it is what keeps this correct at every zoom level
 * without knowing anything about the viewport transform.
 *
 * `boundary` is the diagram's own rectangle, and it is what keeps the card off the diff panel. The
 * card is portalled to the document body, so nothing in the layout can hold it back: without a
 * boundary a node near the right of the canvas puts its explanation squarely over the file the
 * reviewer opened, which is the one thing on screen it must never cover. Given the canvas, Radix
 * treats the panel's edge the way it treats the window's — it flips the card to the other side of
 * its node and shrinks it to fit rather than crossing over.
 */
export function GraphHoverCard({
  controller,
  boundary,
  children,
}: {
  controller: HoverController;
  boundary?: RefObject<HTMLElement | null>;
  children: ReactNode;
}) {
  const t = useT();
  const { target, pinned, hold, hide, pin, unpin } = controller;

  if (!target) return null;

  const { rect } = target;

  return (
    <Popover.Root
      open
      modal={false}
      onOpenChange={(next) => {
        // Only Escape reaches here — see `onInteractOutside` below. Closing has to clear the pin,
        // or a dismissed card would leave the pointer locked out.
        if (!next) unpin();
      }}
    >
      <Popover.Anchor
        aria-hidden
        style={{
          position: 'fixed',
          left: rect.left,
          top: rect.top,
          width: rect.width,
          height: rect.height,
          pointerEvents: 'none',
        }}
      />

      <Popover.Portal>
        <Popover.Content
          side="right"
          align="start"
          sideOffset={12}
          collisionPadding={16}
          avoidCollisions
          collisionBoundary={boundary?.current ?? undefined}
          hideWhenDetached
          // Nothing here takes focus on its own. The card follows a pointer, and stealing focus
          // from the search box every time one appears would make the diagram unusable from the
          // keyboard.
          onOpenAutoFocus={(event) => event.preventDefault()}
          onCloseAutoFocus={(event) => event.preventDefault()}
          /*
            Outside clicks are the diagram's business, not Radix's.

            Radix dismisses on a *deferred* pointer-down outside the card, and clicking a second box
            while one card is open is exactly that: the click pinned the new card and the deferred
            dismissal then closed it again, so a pinned card could never be moved from one node to
            the next. The surface already knows what an outside click means — another node pins its
            card, the background puts it away — so the layer is told to leave it alone. Escape still
            closes, through `onOpenChange`.
          */
          onInteractOutside={(event) => event.preventDefault()}
          onPointerEnter={hold}
          onPointerLeave={hide}
          className={cn(
            'z-50 flex w-120 flex-col overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-lg',
            pinned && 'ring-2 ring-primary',
          )}
          // Flipping to the other side is not enough on its own: with the diff panel open wide, 480
          // pixels may not fit on either side of the node, and a card that cannot fit is placed
          // straddling the boundary rather than inside it. Radix measures the room it has and reports
          // it here, so the card narrows instead of spilling over the panel.
          style={{
            maxHeight: 'min(70vh, 34rem)',
            maxWidth: 'var(--radix-popper-available-width, calc(100vw - 2rem))',
          }}
          data-testid="graph-hover-card"
          data-pinned={pinned ? 'true' : undefined}
        >
          <div className="flex min-h-0 flex-1 select-text flex-col overflow-y-auto">{children}</div>

          <div className="flex shrink-0 items-center gap-2 border-t border-border bg-muted/40 px-3 py-1.5">
            <span className="text-[10px] text-muted-foreground">
              {t(pinned ? 'analysis.hover.pinned' : 'analysis.hover.clickToKeep')}
            </span>

            <button
              type="button"
              onClick={() => (pinned ? unpin() : pin())}
              aria-label={t(pinned ? 'analysis.hover.unpin' : 'analysis.hover.pin')}
              className="ml-auto flex items-center gap-1 rounded px-1.5 py-0.5 text-[11px] text-muted-foreground hover:bg-accent hover:text-foreground"
            >
              {pinned ? (
                <PinOffIcon className="size-3.5" aria-hidden />
              ) : (
                <PinIcon className="size-3.5" aria-hidden />
              )}
              {t(pinned ? 'analysis.hover.unpin' : 'analysis.hover.pin')}
            </button>
          </div>

          <Popover.Arrow className="fill-popover" />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
