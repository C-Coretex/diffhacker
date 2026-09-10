import type { AnalysisNodeInfo, AnalysisView } from '@/contracts';

/**
 * Every file in one cluster, in the order the analysis says to read them.
 *
 * The order is the recommended reading order restricted to the cluster, which is the whole reason a
 * cluster is worth opening as a unit: the model already decided what to read first and what follows
 * from it, and a queue that ignored that would be an alphabetical file list with a smaller N.
 *
 * A node the reading order never mentions still appears — §0.2.5 puts every changed file on the
 * diagram and the same promise has to hold for a list of them — after the ones it does, in the order
 * the container declared. Ranked members sort before unranked ones for the same reason: a file the
 * model placed is a better place to start than one it did not.
 */
export function containerQueue(
  view: AnalysisView,
  containerId: string | undefined,
): readonly AnalysisNodeInfo[] {
  if (!containerId) return [];

  const container = view.containers.find((candidate) => candidate.id === containerId);
  if (!container) return [];

  const rank = new Map(view.readingOrder.map((id, index) => [id, index]));
  const nodes = new Map(view.nodes.map((node) => [node.id, node]));

  return container.nodeIds
    .map((id) => nodes.get(id))
    .filter((node): node is AnalysisNodeInfo => node !== undefined)
    .map((node, index) => ({ node, index }))
    .sort((a, b) => {
      const left = rank.get(a.node.id);
      const right = rank.get(b.node.id);

      if (left === undefined && right === undefined) return a.index - b.index;
      if (left === undefined) return 1;
      if (right === undefined) return -1;
      return left - right;
    })
    .map((entry) => entry.node);
}
