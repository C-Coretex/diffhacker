import { useState } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { FilterIcon, TriangleAlertIcon } from 'lucide-react';
import { useT } from '@/i18n/useT';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { projectColourStyle, type ProjectColour } from '@/graph/palette';

/**
 * The toolbar control for narrowing the diagram to a subset of projects.
 *
 * Unlike everything else on this screen, unchecking a project here genuinely removes its files from
 * the diagram — a deliberate exception to §0.2.5's "nothing dropped, hidden or summarised away",
 * made because the reviewer asked for exactly that rather than the application deciding it for them.
 * The analysis itself is untouched (`projectFilter.ts` runs on a copy fed only to this surface), and
 * `ProjectFilterBanner` stands for as long as anything is hidden so the reviewer is never left
 * wondering why a file they know changed is missing from the picture.
 */
export function ProjectFilterControl({
  colours,
  hidden,
  onToggle,
  onShowAll,
}: {
  colours: readonly ProjectColour[];
  hidden: ReadonlySet<string>;
  onToggle: (project: string) => void;
  onShowAll: () => void;
}) {
  const t = useT();
  const [open, setOpen] = useState(false);

  if (colours.length === 0) return null;

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <Button variant="ghost" size="sm" data-testid="project-filter-trigger">
          <FilterIcon className="size-4" aria-hidden />
          {hidden.size > 0
            ? t('analysis.graph.projectFilter.labelFiltered', { count: hidden.size })
            : t('analysis.graph.projectFilter.label')}
        </Button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          align="start"
          sideOffset={6}
          collisionPadding={16}
          style={{ maxHeight: 'var(--radix-popover-content-available-height, 32rem)' }}
          className="z-50 w-72 overflow-y-auto rounded-md border border-border bg-popover p-3 text-popover-foreground shadow-md"
        >
          <div className="mb-2 flex items-center justify-between gap-2">
            <h3 className="text-xs font-semibold">{t('analysis.graph.projectFilter.heading')}</h3>
            <Button
              variant="ghost"
              size="sm"
              onClick={onShowAll}
              disabled={hidden.size === 0}
              data-testid="project-filter-show-all"
            >
              {t('analysis.graph.projectFilter.showAll')}
            </Button>
          </div>

          <ul className="space-y-1.5 text-xs">
            {colours.map((entry) => {
              const id = `project-filter-${entry.project}`;
              const visible = !hidden.has(entry.project);
              const colour = projectColourStyle(entry.slot);

              return (
                <li key={entry.project} className="flex items-center gap-2">
                  <Checkbox
                    id={id}
                    checked={visible}
                    onChange={() => onToggle(entry.project)}
                    data-testid={`project-filter-item-${entry.project}`}
                  />
                  <span
                    aria-hidden
                    className="size-3 shrink-0 rounded-sm border"
                    style={{ background: colour.fill, borderColor: colour.rail }}
                  />
                  <label htmlFor={id} className="min-w-0 flex-1 cursor-pointer truncate" title={entry.project}>
                    {entry.project}
                  </label>
                  <span className="tabular-nums text-muted-foreground">{entry.nodeCount}</span>
                </li>
              );
            })}
          </ul>

          <Popover.Arrow className="fill-popover" />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}

/**
 * The standing warning that some projects are hidden right now, drawn for as long as they are.
 *
 * Above the canvas rather than folded into the toolbar's own muted text: this is the one place on
 * screen where "some of this diagram is missing" is genuinely true, and it stays true until the
 * reviewer either unchecks every project or presses the button this banner puts beside the message.
 */
export function ProjectFilterBanner({
  colours,
  hidden,
  hiddenFileCount,
  onShowAll,
}: {
  colours: readonly ProjectColour[];
  hidden: ReadonlySet<string>;
  hiddenFileCount: number;
  onShowAll: () => void;
}) {
  const t = useT();

  if (hidden.size === 0) return null;

  const project = colours.find((entry) => hidden.has(entry.project))?.project ?? '';

  return (
    <div className="shrink-0 px-3 pt-2">
      <Alert variant="warning" role="status" data-testid="project-filter-banner">
        <TriangleAlertIcon aria-hidden />
        <AlertDescription className="w-full">
          <div className="flex w-full flex-wrap items-center gap-3">
            <p className="min-w-0 flex-1">
              {hidden.size === 1
                ? t('analysis.graph.projectFilter.bannerSingle', { project, count: hiddenFileCount })
                : t('analysis.graph.projectFilter.bannerMultiple', {
                    count: hidden.size,
                    files: hiddenFileCount,
                  })}
            </p>

            <Button size="sm" variant="outline" onClick={onShowAll} data-testid="project-filter-banner-show-all">
              {t('analysis.graph.projectFilter.showAll')}
            </Button>
          </div>
        </AlertDescription>
      </Alert>
    </div>
  );
}
