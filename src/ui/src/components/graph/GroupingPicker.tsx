import { GitBranchIcon, LayoutGridIcon } from 'lucide-react';
import type { AnalysisGroupingMode } from '@/contracts';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';
import { cn } from '@/lib/utils';

/**
 * Which of the two groupings the diagram is showing.
 *
 * Two buttons rather than a cycling one, for the same reasons {@link ThemePicker} gives — and one
 * more that matters here: the reviewer has to be able to tell what the *other* view is for before
 * they commit to looking at it. So each button carries its own one-line explanation as its title,
 * the active one's is printed under the toolbar, and nothing about the difference is left to be
 * discovered by switching and comparing two pictures from memory.
 *
 * A grouping the stored analysis does not hold — one produced before groupings existed, or by a run
 * that was told not to bother — is offered as a disabled button with the reason, rather than being
 * hidden. Hiding it would make the feature look absent instead of unbought.
 */
export function GroupingPicker({ active, available, onChange, busy }: GroupingPickerProps) {
  const t = useT();

  return (
    <div
      role="group"
      aria-label={t('analysis.graph.grouping.label')}
      className="flex items-center gap-0.5 rounded-md border border-border p-0.5"
      data-testid="grouping-picker"
    >
      {OPTIONS.map(({ value, label, body, unavailable, Icon }) => {
        const offered = available.includes(value);
        const isActive = active === value;

        return (
          <button
            key={value}
            type="button"
            onClick={() => onChange(value)}
            disabled={!offered || busy || isActive}
            aria-pressed={isActive}
            title={offered ? `${t(label)} — ${t(body)}` : t(unavailable)}
            data-testid={`grouping-${value}`}
            className={cn(
              'flex items-center gap-1.5 rounded px-2 py-1 text-xs text-muted-foreground',
              'hover:bg-accent hover:text-foreground',
              // The active button is disabled — clicking it would ask the host for the picture
              // already on screen — so it must not also look unavailable.
              isActive && 'bg-accent text-foreground disabled:opacity-100',
              !offered && 'disabled:opacity-50',
            )}
          >
            <Icon className="size-3.5" aria-hidden />
            {t(label)}
          </button>
        );
      })}
    </div>
  );
}

export interface GroupingPickerProps {
  readonly active: AnalysisGroupingMode;

  /** The groupings the stored analysis holds. Anything else is offered disabled, with its reason. */
  readonly available: readonly AnalysisGroupingMode[];

  readonly onChange: (grouping: AnalysisGroupingMode) => void;

  /** True while a switch is in flight, so a second click cannot race the first. */
  readonly busy: boolean;
}

/** The line explaining the grouping on screen, for the strip under the toolbar. */
export function groupingBodyKey(grouping: AnalysisGroupingMode): ResourceKey {
  return OPTIONS.find((option) => option.value === grouping)?.body ?? 'analysis.graph.grouping.dependencyFlowBody';
}

const OPTIONS: {
  value: AnalysisGroupingMode;
  label: ResourceKey;
  body: ResourceKey;
  unavailable: ResourceKey;
  Icon: typeof GitBranchIcon;
}[] = [
  {
    value: 'dependency_flow',
    label: 'analysis.graph.grouping.dependencyFlow',
    body: 'analysis.graph.grouping.dependencyFlowBody',
    unavailable: 'analysis.graph.grouping.unavailable',
    Icon: GitBranchIcon,
  },
  {
    value: 'change_clusters',
    label: 'analysis.graph.grouping.changeClusters',
    body: 'analysis.graph.grouping.changeClustersBody',
    unavailable: 'analysis.graph.grouping.unavailable',
    Icon: LayoutGridIcon,
  },
];
