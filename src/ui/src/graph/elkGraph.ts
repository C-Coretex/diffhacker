import type { AnalysisEdgeInfo, AnalysisNodeInfo, AnalysisView } from '@/contracts';
import {
  COLLAPSED_HEIGHT,
  COLLAPSED_WIDTH,
  ELK_CONTAINER_OPTIONS,
  ELK_ROOT_OPTIONS,
  ENTRY_NODE_OPTIONS,
  mergedHeight,
  NODE_HEIGHT,
  NODE_WIDTH,
} from './elkOptions';
import { NO_MERGE, unitOf, type MergePlan } from './implementationGroups';

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
 * A collapsed container becomes one leaf node with no children and no internal edges. A merged box
 * — an abstraction and its implementations, when the reviewer has merging on — becomes one leaf
 * standing where its earliest member would have stood, and every edge into or out of a member is an
 * edge into or out of the box.
 */
export function buildElkGraph(
  view: AnalysisView,
  collapsed: ReadonlySet<string>,
  merge: MergePlan = NO_MERGE,
): ElkNode {
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

    const units = layoutUnits(
      membersInRankOrder(container.nodeIds, nodesById),
      container.entryNodeId,
      merge,
    );

    return {
      id: container.id,
      layoutOptions: { ...ELK_CONTAINER_OPTIONS },
      children: units.map((unit) => ({
        id: unit.id,
        width: NODE_WIDTH,
        height: unit.height,
        ...(unit.isEntry ? { layoutOptions: { ...ENTRY_NODE_OPTIONS } } : {}),
      })),
      edges: containerEdges(container.id, units, view.edges, containerOf, merge),
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

/** One box ELK places: a node, or a merged box standing for several. */
interface LayoutUnit {
  readonly id: string;
  readonly height: number;
  readonly isEntry: boolean;
  /** The nodes it stands for, in reading order. */
  readonly memberIds: readonly string[];
}

/**
 * A container's boxes in rank order. A merged box takes the place of its earliest member and stands
 * for the rest, so it is placed where the reading reaches the first of them — and it is the entry
 * box when any of its members is the entry node, because that is where the reviewer is told to start.
 */
function layoutUnits(
  members: readonly AnalysisNodeInfo[],
  entryNodeId: string,
  merge: MergePlan,
): LayoutUnit[] {
  const units: LayoutUnit[] = [];
  const placed = new Set<string>();

  for (const node of members) {
    const boxId = merge.boxOf.get(node.id);
    const box = boxId === undefined ? undefined : merge.boxes.get(boxId);

    if (!box) {
      units.push({ id: node.id, height: NODE_HEIGHT, isEntry: node.id === entryNodeId, memberIds: [node.id] });
      continue;
    }

    if (placed.has(box.id)) continue;
    placed.add(box.id);

    units.push({
      id: box.id,
      height: mergedHeight(box.memberIds.length),
      isEntry: box.memberIds.includes(entryNodeId),
      memberIds: box.memberIds,
    });
  }

  return units;
}

/** The edges inside one container: the model's own, plus the synthetic ones rank needs. */
function containerEdges(
  containerId: string,
  units: readonly LayoutUnit[],
  edges: readonly AnalysisEdgeInfo[],
  containerOf: ReadonlyMap<string, string>,
  merge: MergePlan,
): ElkEdge[] {
  const inside = new Set(units.flatMap((unit) => unit.memberIds));
  const withIncoming = new Set<string>();
  const result: ElkEdge[] = [];
  const seen = new Set<string>();

  for (const edge of edges) {
    // Both ends inside this container. `crossesContainers` is not consulted: membership is the
    // thing that decides, and the host computed both from the same source.
    if (!inside.has(edge.sourceNodeId) || !inside.has(edge.targetNodeId)) continue;
    if (containerOf.get(edge.sourceNodeId) !== containerId) continue;

    const source = unitOf(merge, edge.sourceNodeId);
    const target = unitOf(merge, edge.targetNodeId);

    // A self-edge is a warning the validator already raises, and handing one to a layered
    // algorithm produces a routing artefact rather than information. An edge between two members
    // of one merged box lands here too: the box already says they belong together.
    if (source === target) continue;

    // Two of the model's edges landing on the same pair of boxes are one line to lay out. The
    // surface still hands both to the card; this is only about where the boxes go.
    const id = `${source}->${target}`;
    if (seen.has(id)) continue;
    seen.add(id);

    result.push({ id, sources: [source], targets: [target] });
    withIncoming.add(target);
  }

  // Rank for the boxes edges cannot place. A box ranked after the first with nothing pointing at
  // it has no reason, as far as ELK is concerned, to be below anything — so it is chained to the
  // box the model ranked before it.
  for (let i = 1; i < units.length; i++) {
    const unit = units[i];
    const previous = units[i - 1];
    if (unit === undefined || previous === undefined) continue;
    if (withIncoming.has(unit.id)) continue;

    result.push({
      id: `${SYNTHETIC_EDGE_PREFIX}${previous.id}->${unit.id}`,
      sources: [previous.id],
      targets: [unit.id],
    });
  }

  return result;
}

/**
 * The model's edges a laid-out line inside a container stands for: every edge whose two ends are
 * drawn in those two boxes. One, unless merging folded several onto one pair.
 */
export function edgesBetween(
  view: AnalysisView,
  source: string,
  target: string,
  merge: MergePlan = NO_MERGE,
): AnalysisEdgeInfo[] {
  return view.edges.filter(
    (edge) =>
      unitOf(merge, edge.sourceNodeId) === source &&
      unitOf(merge, edge.targetNodeId) === target &&
      edge.sourceNodeId !== edge.targetNodeId,
  );
}

/**
 * Every edge that must be drawn but was kept out of the layout: the ones crossing a container
 * boundary, plus every edge with an end inside a container that is currently collapsed.
 *
 * Edges that collapse onto the same pair are merged into one, carrying how many they stand for.
 * A bundle is neither direct nor conceptual — claiming either would be inventing a fact — so it is
 * drawn as its own thing and labelled with its count.
 *
 * An end inside a merged box is drawn to the box. A collapsed container still wins over that: the
 * box is inside the container, and a collapsed container has nothing inside it to draw to.
 */
export function bundledEdges(
  view: AnalysisView,
  collapsed: ReadonlySet<string>,
  merge: MergePlan = NO_MERGE,
): BundledEdge[] {
  const containerOf = new Map(view.nodes.map((node) => [node.id, node.containerId]));
  const endpointOf = (nodeId: string): string => {
    const container = containerOf.get(nodeId);
    return container !== undefined && collapsed.has(container) ? container : unitOf(merge, nodeId);
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
      bundles.set(key, {
        ...existing,
        count: existing.count + 1,
        kind: null,
        members: [...existing.members, edge],
      });
      continue;
    }

    bundles.set(key, {
      id: key,
      source,
      target,
      count: 1,
      kind: edge.kind,
      members: [edge],
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
  /**
   * The model's own edges this line stands for, whole rather than summarised.
   *
   * Iteration 9's hover card has to show an explanation *and* the risks *and* whether the
   * relationship crosses a boundary, and a bundle has to show all of that for each edge it folded
   * up. Carrying the records themselves means the card reads the model's answer rather than a copy
   * of two of its fields.
   */
  readonly members: readonly AnalysisEdgeInfo[];
}
