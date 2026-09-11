import { ArrowDownIcon, ArrowUpIcon } from 'lucide-react';
import type { AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { Badge } from '@/components/ui/badge';
import { clamp } from '@/graph/truncate';
import { plainText } from '@/lib/markdown';
import { neighboursOf, type Neighbour } from './neighbours';

/**
 * Where to go next — requirement 5.
 *
 * Two ways through the change, offered together:
 *
 * - **The graph.** Every node with an edge into or out of this one, as a labelled choice. Not a
 *   "next" button: several nodes can point at one node, and collapsing that into a single arrow
 *   would quietly reimpose the alphabetical file list this product exists to replace. Each label
 *   carries the model's own account of the relationship, so it says something about *where it goes*
 *   rather than only naming a file.
 * - **The recommended reading order**, as a linear path, for a reviewer who would rather be told —
 *   which lives in the panel's header (`ReadingOrderNav`) rather than here, because it is the one a
 *   reviewer presses on every file and a control you press three hundred times belongs above the fold.
 *
 * Both go through `openDiffFor`, which expands the target's cluster and centres the diagram on it, so
 * the diagram and the panel never disagree about where the reviewer is.
 */
export function NodeNavigator({ view, nodeId }: { view: AnalysisView; nodeId: string }) {
  const t = useT();
  const openDiff = useAppStore((state) => state.openDiffFor);

  const { predecessors, successors } = neighboursOf(view, nodeId);

  const go = (id: string | null) => {
    if (!id) return;
    const node = view.nodes.find((candidate) => candidate.id === id);
    if (node) openDiff(node.id, node.containerId);
  };

  return (
    <section
      className="flex flex-col gap-3 border-t border-border px-3 py-3"
      data-testid="node-navigator"
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
          {t('analysis.diff.navigationHeading')}
        </h3>
      </header>

      {predecessors.length === 0 && successors.length === 0 ? (
        <p className="text-xs text-muted-foreground">{t('analysis.diff.noNeighbours')}</p>
      ) : (
        <div className="flex flex-col gap-3">
          <Group
            label={t('analysis.diff.predecessors')}
            icon={<ArrowUpIcon className="size-3" aria-hidden />}
            neighbours={predecessors}
            onChoose={go}
            testId="predecessors"
          />
          <Group
            label={t('analysis.diff.successors')}
            icon={<ArrowDownIcon className="size-3" aria-hidden />}
            neighbours={successors}
            onChoose={go}
            testId="successors"
          />
        </div>
      )}
    </section>
  );
}

function Group({
  label,
  icon,
  neighbours,
  onChoose,
  testId,
}: {
  label: string;
  icon: React.ReactNode;
  neighbours: readonly Neighbour[];
  onChoose(id: string): void;
  testId: string;
}) {
  if (neighbours.length === 0) return null;

  return (
    <div className="flex flex-col gap-1.5" data-testid={testId}>
      <h4 className="flex items-center gap-1 text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
        {icon}
        {label}
      </h4>

      <ul className="flex flex-col gap-1">
        {neighbours.map((neighbour) => (
          <li key={neighbour.node.id}>
            <NeighbourButton neighbour={neighbour} onChoose={onChoose} />
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * One choice.
 *
 * The verification step for this asks that "each label says something useful about where it goes",
 * so the button carries four things: the node's title, which cluster it is in when that differs,
 * whether the relationship is real code or the model's inference, and the model's sentence about the
 * relationship itself. The last one is what turns a list of filenames into a choice.
 */
function NeighbourButton({
  neighbour,
  onChoose,
}: {
  neighbour: Neighbour;
  onChoose(id: string): void;
}) {
  const t = useT();

  return (
    <button
      type="button"
      onClick={() => onChoose(neighbour.node.id)}
      data-testid={`neighbour-${neighbour.node.id}`}
      data-kind={neighbour.kind}
      className="flex w-full flex-col gap-1 rounded border border-border px-2 py-1.5 text-left hover:border-ring hover:bg-accent/50"
    >
      <span className="flex flex-wrap items-center gap-1.5">
        <span className="text-xs font-medium">{neighbour.node.title}</span>

        <Badge
          variant={neighbour.kind === 'direct' ? 'secondary' : 'outline'}
          className="px-1 py-0 text-[9px]"
        >
          {t(neighbour.kind === 'direct' ? 'analysis.edgeDirect' : 'analysis.edgeConceptual')}
        </Badge>

        {neighbour.containerTitle && (
          <span className="text-[10px] text-muted-foreground">
            {t('analysis.diff.crossesClusters', { container: neighbour.containerTitle })}
          </span>
        )}
      </span>

      <span className="break-all font-mono text-[10px] text-muted-foreground">
        {neighbour.node.filePath}
      </span>

      {neighbour.explanation && (
        <span className="text-[11px] leading-snug text-muted-foreground">
          {clamp(plainText(neighbour.explanation), 160)}
        </span>
      )}
    </button>
  );
}
