import * as Popover from '@radix-ui/react-popover';
import { ChevronsDownUp, ChevronsUpDown, Info, Maximize2 } from 'lucide-react';
import type { AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { Button } from '@/components/ui/button';
import type { ProjectColour } from '@/graph/palette';
import { useAppStore } from '@/store/appStore';
import { GraphLegend } from './GraphLegend';
import { NodeSearch } from './NodeSearch';

/**
 * The strip above the canvas: find a file, fit the whole change on screen, fold every cluster away,
 * and look up what a line or a colour means.
 *
 * Collapse-all is one button rather than two, and its label says which way it will go, because a
 * reviewer who has folded thirty clusters wants one click to get them back.
 */
export function GraphToolbar({
  view,
  colours,
  onSelectSearchHit,
  onFitView,
}: {
  view: AnalysisView;
  colours: readonly ProjectColour[];
  onSelectSearchHit: (nodeId: string) => void;
  onFitView: () => void;
}) {
  const t = useT();
  const collapsed = useAppStore((state) => state.graphCollapsed);
  const setAllCollapsed = useAppStore((state) => state.setAllContainersCollapsed);
  const legendOpen = useAppStore((state) => state.graphLegendOpen);
  const setLegendOpen = useAppStore((state) => state.setGraphLegendOpen);

  const containerIds = view.containers.map((container) => container.id);
  const allCollapsed = containerIds.length > 0 && containerIds.every((id) => collapsed.has(id));

  return (
    <div className="flex shrink-0 items-center gap-2 border-b border-border px-3 py-2">
      <NodeSearch view={view} onSelect={onSelectSearchHit} />

      <Button variant="ghost" size="sm" onClick={onFitView}>
        <Maximize2 className="size-4" aria-hidden />
        {t('analysis.graph.fitView')}
      </Button>

      <Button
        variant="ghost"
        size="sm"
        onClick={() => setAllCollapsed(!allCollapsed, containerIds)}
      >
        {allCollapsed ? (
          <ChevronsUpDown className="size-4" aria-hidden />
        ) : (
          <ChevronsDownUp className="size-4" aria-hidden />
        )}
        {allCollapsed ? t('analysis.graph.expandAll') : t('analysis.graph.collapseAll')}
      </Button>

      <div className="ml-auto flex items-center gap-2">
        <span className="text-xs text-muted-foreground">
          {t('analysis.graph.counts', {
            containers: view.containers.length,
            nodes: view.nodes.length,
            edges: view.edges.length,
          })}
        </span>

        <Popover.Root open={legendOpen} onOpenChange={setLegendOpen}>
          <Popover.Trigger asChild>
            <Button variant="ghost" size="sm">
              <Info className="size-4" aria-hidden />
              {t('analysis.graph.legend')}
            </Button>
          </Popover.Trigger>
          <Popover.Portal>
            <Popover.Content
              align="end"
              sideOffset={6}
              collisionPadding={16}
              /*
                The project list is unbounded — one entry per project in the change, plus whatever
                is behind "N other projects" — so the legend has no natural height, and on a
                repository with enough modules it ran off the bottom of the window with the rest
                unreachable. Capped to the room Radix measured between the trigger and the window
                edge, and scrolled inside that.
              */
              style={{ maxHeight: 'var(--radix-popover-content-available-height, 32rem)' }}
              className="z-50 overflow-y-auto rounded-md border border-border bg-popover p-4 text-popover-foreground shadow-md"
            >
              <GraphLegend colours={colours} />
              <Popover.Arrow className="fill-popover" />
            </Popover.Content>
          </Popover.Portal>
        </Popover.Root>
      </div>
    </div>
  );
}
