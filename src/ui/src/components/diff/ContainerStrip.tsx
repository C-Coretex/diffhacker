import { CheckIcon, FilesIcon, XIcon } from 'lucide-react';
import type { AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { cn } from '@/lib/utils';
import { basename } from '@/graph/truncate';
import { containerQueue } from './containerQueue';

/**
 * The cluster the reviewer opened whole, as a list they can see all of.
 *
 * Opening a container means opening every file in it, and "every file" has to be visible or it is
 * just a differently-shaped single file: the strip lists them in the analysis's reading order, shows
 * which have been marked read, and lets any of them be jumped to directly. The panel still shows one
 * diff at a time — that is what a diff panel is — but the reviewer can see the whole of what they
 * took on and how much of it is left.
 *
 * Leaving the queue is one button, and following an edge out of the cluster leaves it too (see
 * `openDiffFor`). A queue you cannot put down is a queue that stops being a choice.
 */
export function ContainerStrip({ view, nodeId }: { view: AnalysisView; nodeId: string }) {
  const t = useT();

  const containerId = useAppStore((state) => state.diffContainerId);
  const reviewed = useAppStore((state) => state.reviewedNodeIds);
  const openContainerDiff = useAppStore((state) => state.openContainerDiff);
  const leaveQueue = useAppStore((state) => state.leaveContainerQueue);

  const container = view.containers.find((candidate) => candidate.id === containerId);
  const queue = containerQueue(view, containerId);

  if (!container || queue.length === 0) return null;

  return (
    <div
      className="flex shrink-0 flex-col gap-1 border-b border-border bg-muted/40 px-3 py-1.5"
      data-testid="container-strip"
      data-container-id={container.id}
    >
      <div className="flex items-center gap-2">
        <FilesIcon className="size-3.5 shrink-0 text-muted-foreground" aria-hidden />

        <span className="min-w-0 truncate text-[11px] font-medium" title={container.title}>
          {container.title}
        </span>

        <span className="shrink-0 text-[10px] tabular-nums text-muted-foreground">
          {t('analysis.graph.fileCount', { count: queue.length })}
        </span>

        <button
          type="button"
          onClick={leaveQueue}
          aria-label={t('analysis.diff.leaveContainer')}
          title={t('analysis.diff.leaveContainer')}
          data-testid="leave-container"
          className="ml-auto shrink-0 rounded p-0.5 text-muted-foreground hover:bg-accent hover:text-foreground"
        >
          <XIcon className="size-3.5" aria-hidden />
        </button>
      </div>

      <ul className="flex flex-wrap gap-1">
        {queue.map((node) => {
          const isCurrent = node.id === nodeId;
          const isRead = reviewed.has(node.id);

          return (
            <li key={node.id}>
              <button
                type="button"
                onClick={() => openContainerDiff(container.id, node.id)}
                title={node.filePath}
                data-testid={`queue-${node.id}`}
                data-current={isCurrent ? 'true' : undefined}
                data-reviewed={isRead ? 'true' : undefined}
                className={cn(
                  'flex max-w-52 items-center gap-1 rounded border px-1.5 py-0.5 text-[10px]',
                  isCurrent
                    ? 'border-primary bg-primary/10 font-medium'
                    : 'border-border hover:bg-accent',
                  isRead && !isCurrent && 'text-muted-foreground',
                )}
              >
                {isRead && <CheckIcon className="size-3 shrink-0 text-primary" aria-hidden />}
                <span className="truncate">{basename(node.filePath)}</span>
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
