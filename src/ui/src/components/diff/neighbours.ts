import type { AnalysisEdgeInfo, AnalysisNodeInfo, AnalysisView } from '@/contracts';

/** One place the reviewer could go from here, and enough about it to choose. */
export interface Neighbour {
  readonly node: AnalysisNodeInfo;

  /** The cluster it sits in, when that is a different one. Null when it is the same cluster. */
  readonly containerTitle: string | null;

  /** Whether code backs the relationship or the model inferred it. Both are drawn, differently. */
  readonly kind: AnalysisEdgeInfo['kind'];

  /** The model's own account of the relationship. What makes the label say where it goes. */
  readonly explanation: string;

  /** Risks the model recorded against the relationship itself, not against either end. */
  readonly risks: readonly string[];
}

export interface Neighbours {
  /** Nodes with an edge into this one — what a reviewer arrives from. */
  readonly predecessors: readonly Neighbour[];

  /** Nodes this one points at — what reading it leads to. */
  readonly successors: readonly Neighbour[];
}

/**
 * Who is next to this node in the graph, in both directions.
 *
 * Requirement 5's whole point: "graph traversal is not linear — several nodes can point at one node —
 * so 'next' cannot be a single arrow. Forcing it into one would quietly reimpose the linear file list
 * this product exists to escape." So this returns two lists and the caller draws a choice.
 *
 * Built here rather than in the host. Every field it needs is already on the `AnalysisView` the screen
 * is holding — the edges carry their own kind, explanation, risks and whether they cross a cluster —
 * so asking the host would be a round trip for an answer already in memory.
 *
 * Ordered by the reading order the analysis carries, so the choice a reviewer is offered is presented
 * in the sequence the analysis itself recommends rather than in whatever order the model happened to
 * emit its edges.
 */
export function neighboursOf(view: AnalysisView, nodeId: string): Neighbours {
  const nodes = new Map(view.nodes.map((node) => [node.id, node]));
  const containers = new Map(view.containers.map((container) => [container.id, container]));
  const rank = new Map(view.readingOrder.map((id, index) => [id, index]));

  const self = nodes.get(nodeId);
  const predecessors: Neighbour[] = [];
  const successors: Neighbour[] = [];

  for (const edge of view.edges) {
    const isIncoming = edge.targetNodeId === nodeId;
    const isOutgoing = edge.sourceNodeId === nodeId;

    if (!isIncoming && !isOutgoing) continue;

    // A self-edge is a validation warning the analysis already carries. Offering "go to where you
    // already are" as one of the choices would be noise on top of it.
    if (isIncoming && isOutgoing) continue;

    const other = nodes.get(isIncoming ? edge.sourceNodeId : edge.targetNodeId);
    if (!other) continue;

    const neighbour: Neighbour = {
      node: other,
      containerTitle:
        self && other.containerId !== self.containerId
          ? (containers.get(other.containerId)?.title ?? null)
          : null,
      kind: edge.kind,
      explanation: edge.explanation,
      risks: edge.risks,
    };

    (isIncoming ? predecessors : successors).push(neighbour);
  }

  // A node reached by two edges is one choice, not two. The first edge wins its explanation, which
  // matches how the diagram bundles duplicate lines rather than drawing them on top of each other.
  const order = (a: Neighbour, b: Neighbour) =>
    (rank.get(a.node.id) ?? Number.MAX_SAFE_INTEGER) - (rank.get(b.node.id) ?? Number.MAX_SAFE_INTEGER);

  return {
    predecessors: dedupe(predecessors).sort(order),
    successors: dedupe(successors).sort(order),
  };
}

function dedupe(neighbours: readonly Neighbour[]): Neighbour[] {
  const seen = new Set<string>();

  return neighbours.filter((neighbour) => {
    if (seen.has(neighbour.node.id)) return false;
    seen.add(neighbour.node.id);
    return true;
  });
}

/** Where this node sits in the recommended reading order, and what is on either side of it. */
export interface ReadingPosition {
  /** Counting from 1, or 0 when the reading order does not mention this node. */
  readonly position: number;
  readonly total: number;
  readonly previousId: string | null;
  readonly nextId: string | null;
}

/**
 * Requirement 5's second half: the reading order as a linear path, offered *beside* the graph choice
 * rather than instead of it. One reviewer wants to be told where to go; another wants to follow the
 * consequence they just read about. Both are supported, and neither is the only way through.
 */
export function readingPosition(view: AnalysisView, nodeId: string): ReadingPosition {
  const order = view.readingOrder;
  const index = order.indexOf(nodeId);

  return {
    position: index < 0 ? 0 : index + 1,
    total: order.length,
    previousId: (index > 0 ? order[index - 1] : null) ?? null,
    nextId: (index >= 0 && index < order.length - 1 ? order[index + 1] : null) ?? null,
  };
}
