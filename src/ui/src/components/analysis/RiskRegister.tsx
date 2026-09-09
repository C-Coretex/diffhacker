import { TriangleAlertIcon } from 'lucide-react';
import type { AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import type { ResourceKey } from '@/i18n/translate';
import { useAppStore } from '@/store/appStore';

/** One risk, and where in the result it was written. */
export interface RegisteredRisk {
  readonly text: string;
  readonly sourceKey: ResourceKey;
  readonly sourceArgs: Record<string, string>;
  /** The node to jump to, when the risk belongs to one. */
  readonly nodeId?: string;
  readonly containerId?: string;
}

/**
 * Every risk in the result, from all four places one can be written.
 *
 * Requirement 5 asks for them "collected in one place", and the point of collecting them is that
 * they are otherwise scattered across four levels of a document and reachable only by hovering the
 * right box. A reviewer asking "what could go wrong here" should get one list, not a search.
 *
 * Exported separately from the component so a test can assert the count against the persisted
 * document rather than against the rendering — verification step 6 is "assert it against the
 * persisted document", and this is the seam that lets it.
 */
export function collectRisks(view: AnalysisView): RegisteredRisk[] {
  const risks: RegisteredRisk[] = [];
  const nodeById = new Map(view.nodes.map((node) => [node.id, node]));

  for (const text of view.overallRisks) {
    risks.push({ text, sourceKey: 'analysis.overview.registerFromOverall', sourceArgs: {} });
  }

  for (const container of view.containers) {
    for (const text of container.risks) {
      risks.push({
        text,
        sourceKey: 'analysis.overview.registerFromContainer',
        sourceArgs: { title: container.title },
        nodeId: container.entryNodeId,
        containerId: container.id,
      });
    }
  }

  // Nodes in the order the view already sorted them — container display order, then rank — so the
  // register reads down the change the same way the diagram does.
  for (const node of view.nodes) {
    for (const text of node.risks) {
      risks.push({
        text,
        sourceKey: 'analysis.overview.registerFromNode',
        sourceArgs: { path: node.filePath },
        nodeId: node.id,
        containerId: node.containerId,
      });
    }
  }

  for (const edge of view.edges) {
    for (const text of edge.risks) {
      const source = nodeById.get(edge.sourceNodeId);

      risks.push({
        text,
        sourceKey: 'analysis.overview.registerFromEdge',
        sourceArgs: { source: edge.sourceNodeId, target: edge.targetNodeId },
        nodeId: source?.id,
        containerId: source?.containerId,
      });
    }
  }

  return risks;
}

export function RiskRegister({ view }: { view: AnalysisView }) {
  const t = useT();
  const reveal = useAppStore((state) => state.revealGraphNode);
  const risks = collectRisks(view);

  return (
    <section className="flex min-w-0 flex-col gap-2">
      <div>
        <h3 className="text-sm font-semibold">{t('analysis.overview.registerHeading')}</h3>
        <p className="text-xs text-muted-foreground">{t('analysis.overview.registerBody')}</p>
      </div>

      {risks.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('analysis.overview.registerEmpty')}</p>
      ) : (
        <ul className="flex max-h-64 flex-col gap-2 overflow-y-auto pr-1" data-testid="risk-register">
          {risks.map((risk, index) => (
            <li key={index} className="flex gap-2">
              <TriangleAlertIcon
                className="mt-0.5 size-3.5 shrink-0 text-warning-foreground"
                aria-hidden
              />
              <div className="min-w-0">
                <p className="text-sm">{risk.text}</p>
                {risk.nodeId && risk.containerId ? (
                  <button
                    type="button"
                    onClick={() => reveal(risk.nodeId!, risk.containerId!)}
                    className="truncate text-left text-[11px] text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
                  >
                    {t(risk.sourceKey, risk.sourceArgs)}
                  </button>
                ) : (
                  <p className="text-[11px] text-muted-foreground">
                    {t(risk.sourceKey, risk.sourceArgs)}
                  </p>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
