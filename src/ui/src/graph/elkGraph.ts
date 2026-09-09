import type { AnalysisEdgeInfo, AnalysisNodeInfo, AnalysisView } from '@/contracts';
import {
  COLLAPSED_HEIGHT,
  COLLAPSED_WIDTH,
  ELK_CONTAINER_OPTIONS,
  ELK_ROOT_OPTIONS,
  ENTRY_NODE_OPTIONS,
  NODE_HEIGHT,
  NODE_WIDTH,
} from './elkOptions';

/**
 * The subset of ELK's graph shape this code produces. Declared here rather than imported from
 * `elkjs` so that `elkGraph.ts` stays a pure function over plain data — it is the thing the
 * snapshot test reads, and it is posted to a worker, so it has to be structurally cloneable.
 */
export interface ElkNode {
  id: string;
  width?: number;
  height?: number;
  layoutOptions?: Record<string, string>;
  children?: ElkNode[];
  edges?: ElkEdge[];
  x?: number;
  y?: number;
}

export interface ElkEdge {
  id: string;
  sources: string[];
  targets: string[];
  sections?: ElkEdgeSection[];
}

export interface ElkEdgeSection {
  startPoint: { x: number; y: number };
  endPoint: { x: number; y: number };
  bendPoints?: { x: number; y: number }[];
}

/**
 * The prefix on every edge this file invents rather than takes from the model.
 *
 * A rank is an ordering the model asserted, but ELK orders by *edges* — so a node the model ranked
 * third with nothing pointing at it would float into the first layer and read as a starting point.
 * A synthetic edge from the rank-1 node says "after that one" in the only language the layout
 * engine speaks. It is never drawn: `flowGraph` filters on this prefix, because an edge on the
 * diagram is a claim the model made and this is not one.
 */
export const SYNTHETIC_EDGE_PREFIX = 'rank:';

export function isSyntheticEdge(id: string): boolean {
  return id.startsWith(SYNTHETIC_EDGE_PREFIX);
}

/**
 * Builds the graph handed to ELK.
 *
 * Three decisions live here, and all three are checkable by reading the returned object rather
 * than by looking at a picture — which is the point of keeping this separate from rendering:
 *
 * 1. **Cross-container edges are absent entirely** (requirement 7). Not weighted down, not marked:
 *    absent. They are drawn afterwards by React Flow, faintly. Verification step 6 asks for this to
 *    be confirmed "in the ELK input, not by eye", and it can be.
 * 2. **The entry node carries a FIRST layer constraint**, so it is at the top of its container.
 * 3. **Children are emitted in rank order**, which the container's model-order options make ELK
 *    honour within a layer.
 *
 * A collapsed container becomes one leaf node with no children and no internal edges.
 */
export function buildElkGraph(view: AnalysisView, collapsed: ReadonlySet<string>): ElkNode {
  const containerOf = new Map(view.nodes.map((node) => [node.id, node.containerId]));
  const nodesById = new Map(view.nodes.map((node) => [node.id, node]));

  const children = view.containers.map((container) => {
    if (collapsed.has(container.id)) {
      return {
        id: container.id,
        width: COLLAPSED_WIDTH,
        height: COLLAPSED_HEIGHT,
      } satisfies ElkNode;
    }

    const members = membersInRankOrder(container.nodeIds, nodesById);

    return {
      id: container.id,
      layoutOptions: { ...ELK_CONTAINER_OPTIONS },
      children: members.map((node) => ({
        id: node.id,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        ...(node.id === container.entryNodeId ? { layoutOptions: { ...ENTRY_NODE_OPTIONS } } : {}),
      })),
      edges: containerEdges(container.id, members, view.edges, containerOf),
    } satisfies ElkNode;
  });

  return {
    id: 'root',
    layoutOptions: { ...ELK_ROOT_OPTIONS },
    children,
  };
}

/**
 * The container's members, in the order the model ranked them.
 *
 * `nodeIds` is documented as being in rank order already, but it is the model's array and rank is
 * the model's number, and the two disagreeing is exactly the sort of thing the validator warns
 * about rather than rejects. Sorting by the rank field makes the layout follow the number the box
 * prints, so a reader comparing the two never sees them contradict each other.
 */
function membersInRankOrder(
  nodeIds: readonly string[],
  nodesById: ReadonlyMap<string, AnalysisNodeInfo>,
): AnalysisNodeInfo[] {
  return nodeIds
    .map((id) => nodesById.get(id))
    .filter((node): node is AnalysisNodeInfo => node !== undefined)
    .sort((a, b) => a.rank - b.rank || a.id.localeCompare(b.id));
}

/** The edges inside one container: the model's own, plus the synthetic ones rank needs. */
function containerEdges(
  containerId: string,
  members: readonly AnalysisNodeInfo[],
  edges: readonly AnalysisEdgeInfo[],
  containerOf: ReadonlyMap<string, string>,
): ElkEdge[] {
  const inside = new Set(members.map((node) => node.id));
  const withIncoming = new Set<string>();
  const result: ElkEdge[] = [];

  for (const edge of edges) {
    // Both ends inside this container. `crossesContainers` is not consulted: membership is the
    // thing that decides, and the host computed both from the same source.
    if (!inside.has(edge.sourceNodeId) || !inside.has(edge.targetNodeId)) continue;
    if (containerOf.get(edge.sourceNodeId) !== containerId) continue;

    // A self-edge is a warning the validator already raises, and handing one to a layered
    // algorithm produces a routing artefact rather than information.
    if (edge.sourceNodeId === edge.targetNodeId) continue;

    result.push({
      id: `${edge.sourceNodeId}->${edge.targetNodeId}`,
      sources: [edge.sourceNodeId],
      targets: [edge.targetNodeId],
    });

    withIncoming.add(edge.targetNodeId);
  }

  // Rank for the nodes edges cannot place. A node ranked after the first with nothing pointing at
  // it has no reason, as far as ELK is concerned, to be below anything — so it is chained to the
  // node the model ranked before it.
  for (let i = 1; i < members.length; i++) {
    const node = members[i];
    const previous = members[i - 1];
    if (node === undefined || previous === undefined) continue;
    if (withIncoming.has(node.id)) continue;

    result.push({
      id: `${SYNTHETIC_EDGE_PREFIX}${previous.id}->${node.id}`,
      sources: [previous.id],
      targets: [node.id],
    });
  }

  return result;
}

/**
 * Every edge that must be drawn but was kept out of the layout: the ones crossing a container
 * boundary, plus every edge with an end inside a container that is currently collapsed.
 *
 * Edges that collapse onto the same pair are merged into one, carrying how many they stand for.
 * A bundle is neither direct nor conceptual — claiming either would be inventing a fact — so it is
 * drawn as its own thing and labelled with its count.
 */
export function bundledEdges(
  view: AnalysisView,
  collapsed: ReadonlySet<string>,
): BundledEdge[] {
  const containerOf = new Map(view.nodes.map((node) => [node.id, node.containerId]));
  const endpointOf = (nodeId: string): string => {
    const container = containerOf.get(nodeId);
    return container !== undefined && collapsed.has(container) ? container : nodeId;
  };

  const bundles = new Map<string, BundledEdge>();

  for (const edge of view.edges) {
    const source = endpointOf(edge.sourceNodeId);
    const target = endpointOf(edge.targetNodeId);

    // Wholly inside one collapsed container, or a self-edge: there is nothing left to draw between.
    if (source === target) continue;

    const sourceContainer = containerOf.get(edge.sourceNodeId);
    const targetContainer = containerOf.get(edge.targetNodeId);
    const bothExpanded =
      sourceContainer !== undefined &&
      targetContainer !== undefined &&
      !collapsed.has(sourceContainer) &&
      !collapsed.has(targetContainer);

    // An intra-container edge between two expanded containers is ELK's to route, not ours.
    if (bothExpanded && sourceContainer === targetContainer) continue;

    const key = `${source}->${target}`;
    const existing = bundles.get(key);

    if (existing) {
      bundles.set(key, { ...existing, count: existing.count + 1, kind: null });
      continue;
    }

    bundles.set(key, {
      id: key,
      source,
      target,
      count: 1,
      kind: edge.kind,
      explanation: edge.explanation,
    });
  }

  return [...bundles.values()];
}

/** An edge drawn outside the layout: cross-container, or into or out of a collapsed container. */
export interface BundledEdge {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  /** How many of the model's edges this one stands for. */
  readonly count: number;
  /** The kind, or null once two or more edges merged — a bundle is neither. */
  readonly kind: AnalysisEdgeInfo['kind'] | null;
  readonly explanation?: string;
}
