import { ChevronDown, FilesIcon } from 'lucide-react';
import type { Node, NodeProps } from '@xyflow/react';
import { translate as t } from '@/i18n/translate';
import { cn } from '@/lib/utils';
import { CONTAINER_HEADER } from '@/graph/elkOptions';
import type { ContainerNodeData } from '@/graph/flowGraph';
import { useAppStore } from '@/store/appStore';
import { useGraphActions } from './graphActions';

/**
 * An expanded cluster: the region its nodes sit in, and a title bar above them.
 *
 * A React Flow parent node, which is what the iteration's fixed decisions require — containers are
 * first-class in the library and building them by hand out of background rectangles is how they
 * stop moving with their children.
 *
 * The header height is `CONTAINER_HEADER`, the same constant ELK pads the container's top by, so
 * the title bar never sits over the first node.
 *
 * The title bar carries the cluster's own action beside the chevron: **open every file in it**. A
 * cluster is the unit a reviewer actually reads — it is what the model decided belongs together — and
 * making them pick its twelve boxes off the canvas one at a time is the alphabetical file list with
 * extra steps. Opening it loads the whole cluster into the panel as a queue, in the analysis's own
 * reading order.
 *
 * It also carries the cluster's **explanation card**, and it is the only part of the container that
 * does. The region below it is working canvas — the reviewer pans across it, drags over it and reads
 * the boxes inside it — so the title bar is the one part of a container that is about the container,
 * and the part that explains it, opened with a click the same as a node or an edge.
 */
export function ContainerNode({ data }: NodeProps<Node<ContainerNodeData>>) {
  const { container, nodeCount, isMatch } = data;
  const toggle = useAppStore((state) => state.toggleContainerCollapsed);
  const actions = useGraphActions();

  return (
    <div
      className={cn(
        'size-full rounded-lg border border-dashed border-border bg-muted/30',
        isMatch && 'ring-2 ring-ring',
      )}
      data-testid={`graph-container-${container.id}`}
    >
      <div
        className="flex items-center gap-2 px-3"
        style={{ height: CONTAINER_HEADER }}
        data-testid={`graph-container-header-${container.id}`}
        // The surface deliberately ignores container nodes (@see AnalysisGraph), so this click is the
        // whole of a cluster's card behaviour. The two buttons below stop their own clicks, so neither
        // collapsing a cluster nor opening it also opens the card.
        onClick={(event) => actions?.toggleContainerCard(container, event.currentTarget)}
      >
        <button
          type="button"
          // Stopped here, or clicking the chevron would also bubble to the header's own click and
          // open the cluster's card over the cluster that just folded.
          onClick={(event) => {
            event.stopPropagation();
            toggle(container.id);
          }}
          className="flex min-w-0 items-center gap-2 rounded px-1 py-0.5 text-left hover:bg-accent"
          aria-expanded
          aria-label={t('analysis.graph.collapseContainer', { title: container.title })}
        >
          <ChevronDown className="size-4 shrink-0 text-muted-foreground" aria-hidden />
          <span className="truncate text-sm font-semibold" title={container.title}>
            {container.title}
          </span>
        </button>

        <span className="shrink-0 rounded bg-background px-1.5 py-0.5 text-[10px] tabular-nums text-muted-foreground">
          {t('analysis.graph.fileCount', { count: nodeCount })}
        </span>

        {actions && nodeCount > 0 && (
          <button
            type="button"
            // Stopped for the same reason the chevron stops it: a click on the header opens the
            // cluster's card, and a card over the cluster whose files just opened is a card in the
            // way.
            onClick={(event) => {
              event.stopPropagation();
              actions.openContainer(container);
            }}
            onDoubleClick={(event) => event.stopPropagation()}
            data-testid={`container-open-all-${container.id}`}
            className="nodrag nopan flex shrink-0 items-center gap-1 rounded border border-border bg-background px-1.5 py-0.5 text-[10px] hover:bg-accent"
          >
            <FilesIcon className="size-3" aria-hidden />
            {t('analysis.graph.openContainer')}
          </button>
        )}
      </div>
    </div>
  );
}
