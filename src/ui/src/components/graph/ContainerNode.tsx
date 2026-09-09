import { ChevronDown } from 'lucide-react';
import type { Node, NodeProps } from '@xyflow/react';
import { translate as t } from '@/i18n/translate';
import { cn } from '@/lib/utils';
import { CONTAINER_HEADER } from '@/graph/elkOptions';
import type { ContainerNodeData } from '@/graph/flowGraph';
import { useAppStore } from '@/store/appStore';

/**
 * An expanded cluster: the region its nodes sit in, and a title bar above them.
 *
 * A React Flow parent node, which is what the iteration's fixed decisions require — containers are
 * first-class in the library and building them by hand out of background rectangles is how they
 * stop moving with their children.
 *
 * The header height is `CONTAINER_HEADER`, the same constant ELK pads the container's top by, so
 * the title bar never sits over the first node.
 */
export function ContainerNode({ data }: NodeProps<Node<ContainerNodeData>>) {
  const { container, nodeCount, isMatch } = data;
  const toggle = useAppStore((state) => state.toggleContainerCollapsed);

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
      >
        <button
          type="button"
          // Stopped here, or clicking the chevron would also reach the surface's `onNodeClick` and
          // pin the cluster's card open over the cluster that just folded.
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
      </div>
    </div>
  );
}
