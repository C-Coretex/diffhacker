import type { Edge, Node } from '@xyflow/react';
import type {
  AnalysisContainerInfo,
  AnalysisEdgeInfo,
  AnalysisNodeInfo,
  AnalysisView,
  ChangedFileFactsInfo,
} from '@/contracts';
import { COLLAPSED_HEIGHT, COLLAPSED_WIDTH, mergedHeight, NODE_HEIGHT, NODE_WIDTH } from './elkOptions';
import {
  bundledEdges,
  edgesBetween,
  isSyntheticEdge,
  type BundledEdge,
  type ElkNode,
} from './elkGraph';
import { NO_MERGE, type MergePlan } from './implementationGroups';
import { colourSlots, assignProjectColours, type ProjectColour } from './palette';

/** What a file node's component is given. */
export interface FileNodeData extends Record<string, unknown> {
  readonly node: AnalysisNodeInfo;
  readonly facts: ChangedFileFactsInfo | undefined;
  readonly colourSlot: number | null;
  readonly isEntry: boolean;
  readonly isMatch: boolean;
  readonly isFocused: boolean;

  /** Whether the diff panel is showing this node — the reviewer's position (requirement 6). */
  readonly isCurrent: boolean;

  /** Whether the reviewer has marked it read (requirement 7). */
  readonly isReviewed: boolean;
}

/**
 * What a merged box is given: one row per node it stands for, the abstraction first.
 *
 * Each row is exactly what that node's own box would have been given, so every channel a box
 * carries — state, importance, the search ring, the reviewer's position, the reviewed mark — keeps
 * working per node rather than being averaged into something the box would have to invent.
 */
export interface ImplementationGroupNodeData extends Record<string, unknown> {
  readonly rows: readonly FileNodeData[];
}

export interface ContainerNodeData extends Record<string, unknown> {
  readonly container: AnalysisContainerInfo;
  readonly nodeCount: number;
  readonly collapsed: false;
  readonly isMatch: boolean;
}

export interface CollapsedContainerNodeData extends Record<string, unknown> {
  readonly container: AnalysisContainerInfo;
  readonly nodeCount: number;
  readonly collapsed: true;
  readonly isMatch: boolean;
  readonly entryLabel: string;
  readonly linesAdded: number | null;
  readonly linesRemoved: number | null;
  readonly riskCount: number;
  readonly colourSlots: readonly (number | null)[];
}

export interface ReadingEdgeData extends Record<string, unknown> {
  readonly kind: 'direct' | 'conceptual' | 'bundle';
  readonly count: number;
  /**
   * The model's own edges this line stands for — one, or several for a bundle.
   *
   * Iteration 9's edge card needs the explanation, the risks and whether the relationship crosses
   * a boundary, so the records travel whole rather than as two copied fields.
   */
  readonly edges: readonly AnalysisEdgeInfo[];
  /** Whether the pointer is on this line. Patched in place, the way node highlighting is. */
  readonly isHovered?: boolean;
}

export interface FlowGraph {
  readonly nodes: Node[];
  readonly edges: Edge[];
  readonly colours: readonly ProjectColour[];
}

/** What the search box matched, so the graph can highlight without recomputing per node. */
export interface GraphHighlight {
  readonly matchedNodeIds: ReadonlySet<string>;
  readonly matchedContainerIds: ReadonlySet<string>;
  readonly focusedNodeId: string | null;

  /** The node the diff panel is showing, if any. */
  readonly currentNodeId: string | null;

  /** Everything the reviewer has marked read. */
  readonly reviewedNodeIds: ReadonlySet<string>;
}

const NO_HIGHLIGHT: GraphHighlight = {
  matchedNodeIds: new Set(),
  matchedContainerIds: new Set(),
  focusedNodeId: null,
  currentNodeId: null,
  reviewedNodeIds: new Set(),
};

/**
 * Turns ELK's answer into what React Flow draws.
 *
 * ELK positions children relative to their parent and React Flow expects exactly that for a child
 * node, so no coordinate arithmetic happens here — which is the point. §0.6 says the LLM decides
 * hierarchy and ELK decides pixels; a translation step in between is where a third opinion about
 * position would creep in.
 *
 * Order matters to React Flow: a parent must appear before its children, or the child is dropped.
 */
export function toFlowGraph(
  view: AnalysisView,
  laidOut: ElkNode,
  collapsed: ReadonlySet<string>,
  highlight: GraphHighlight = NO_HIGHLIGHT,
  merge: MergePlan = NO_MERGE,
): FlowGraph {
  const colours = assignProjectColours(view);
  const slots = colourSlots(colours);
  const factsByPath = new Map(view.changedFiles.map((file) => [file.path, file]));
  const containersById = new Map(view.containers.map((container) => [container.id, container]));
  const nodesById = new Map(view.nodes.map((node) => [node.id, node]));

  const nodes: Node[] = [];
  const edges: Edge[] = [];

  for (const laidOutContainer of laidOut.children ?? []) {
    const container = containersById.get(laidOutContainer.id);
    if (!container) continue;

    const isCollapsed = collapsed.has(container.id);
    const members = container.nodeIds
      .map((id) => nodesById.get(id))
      .filter((node): node is AnalysisNodeInfo => node !== undefined);

    if (isCollapsed) {
      nodes.push({
        id: container.id,
        type: 'collapsedContainer',
        position: { x: laidOutContainer.x ?? 0, y: laidOutContainer.y ?? 0 },
        width: COLLAPSED_WIDTH,
        height: COLLAPSED_HEIGHT,
        draggable: false,
        data: collapsedData(container, members, factsByPath, slots, highlight),
      });
      continue;
    }

    nodes.push({
      id: container.id,
      type: 'container',
      position: { x: laidOutContainer.x ?? 0, y: laidOutContainer.y ?? 0 },
      width: laidOutContainer.width ?? 0,
      height: laidOutContainer.height ?? 0,
      draggable: false,
      // Selectable, despite a container being a region rather than a thing anyone wants to select.
      // React Flow sets `pointer-events: none` on a node that is neither draggable nor selectable,
      // and a container with no pointer events is one whose collapse chevron cannot be clicked.
      // Selection is invisible here — the library styles a selected node only for its own built-in
      // types — so this buys the title bar its clicks and costs nothing on screen.
      selectable: true,
      data: {
        container,
        nodeCount: members.length,
        collapsed: false,
        isMatch: highlight.matchedContainerIds.has(container.id),
      } satisfies ContainerNodeData,
    });

    const rowFor = (node: AnalysisNodeInfo): FileNodeData => ({
      node,
      facts: factsByPath.get(node.filePath),
      colourSlot: slots.get(factsByPath.get(node.filePath)?.project ?? '') ?? null,
      isEntry: node.id === container.entryNodeId,
      isMatch: highlight.matchedNodeIds.has(node.id),
      isFocused: highlight.focusedNodeId === node.id,
      isCurrent: highlight.currentNodeId === node.id,
      isReviewed: highlight.reviewedNodeIds.has(node.id),
    });

    for (const laidOutNode of laidOutContainer.children ?? []) {
      const box = merge.boxes.get(laidOutNode.id);

      if (box) {
        const rows = box.memberIds
          .map((id) => nodesById.get(id))
          .filter((node): node is AnalysisNodeInfo => node !== undefined)
          .map(rowFor);

        nodes.push({
          id: box.id,
          type: 'implementationGroup',
          parentId: container.id,
          extent: 'parent',
          position: { x: laidOutNode.x ?? 0, y: laidOutNode.y ?? 0 },
          width: NODE_WIDTH,
          height: mergedHeight(rows.length),
          draggable: false,
          data: { rows } satisfies ImplementationGroupNodeData,
        });
        continue;
      }

      const node = nodesById.get(laidOutNode.id);
      if (!node) continue;

      nodes.push({
        id: node.id,
        type: 'file',
        parentId: container.id,
        extent: 'parent',
        position: { x: laidOutNode.x ?? 0, y: laidOutNode.y ?? 0 },
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        // Requirement 9: the user cannot move nodes. Set here as well as on the ReactFlow element,
        // because a node that says it is draggable is one a future prop change would set loose.
        draggable: false,
        data: rowFor(node) satisfies FileNodeData,
      });
    }

    for (const laidOutEdge of laidOutContainer.edges ?? []) {
      // The synthetic rank edges did their work in the layout engine. Drawing one would put an
      // arrow on the diagram that the model never claimed.
      if (isSyntheticEdge(laidOutEdge.id)) continue;

      const source = laidOutEdge.sources[0];
      const target = laidOutEdge.targets[0];
      if (source === undefined || target === undefined) continue;

      // Usually one. Several when a merged box gathered edges from more than one of its rows onto
      // the same neighbour — and then, like a bundle, the line is neither direct nor conceptual.
      const models = edgesBetween(view, source, target, merge);
      const only = models.length === 1 ? models[0] : undefined;

      edges.push({
        id: laidOutEdge.id,
        source,
        target,
        type: 'reading',
        data: {
          kind: models.length > 1 ? 'bundle' : (only?.kind ?? 'conceptual'),
          count: Math.max(models.length, 1),
          edges: models,
        } satisfies ReadingEdgeData,
      });
    }
  }

  for (const bundle of bundledEdges(view, collapsed, merge)) {
    edges.push(crossEdge(bundle));
  }

  return { nodes, edges, colours };
}

/** An edge ELK never saw: faint, and routed differently so it reads as a different kind of line. */
function crossEdge(bundle: BundledEdge): Edge {
  return {
    id: `cross:${bundle.id}`,
    source: bundle.source,
    target: bundle.target,
    type: bundle.count > 1 ? 'bundle' : 'crossContainer',
    // Under the intra-container edges. A faint line that draws over a solid one is a faint line
    // the eye still lands on first.
    zIndex: 0,
    data: {
      kind: bundle.count > 1 ? 'bundle' : (bundle.kind ?? 'conceptual'),
      count: bundle.count,
      edges: bundle.members,
    } satisfies ReadingEdgeData,
  };
}

function collapsedData(
  container: AnalysisContainerInfo,
  members: readonly AnalysisNodeInfo[],
  factsByPath: ReadonlyMap<string, ChangedFileFactsInfo>,
  slots: ReadonlyMap<string, number | null>,
  highlight: GraphHighlight,
): CollapsedContainerNodeData {
  const entry = members.find((node) => node.id === container.entryNodeId);
  const facts = members.map((node) => factsByPath.get(node.filePath));

  // Absent, not zero, when nothing in the cluster had a countable line change — a container of
  // binaries has unknown line counts, and "+0 −0" would claim it was touched and unchanged.
  const added = sumOrNull(facts.map((file) => file?.linesAdded));
  const removed = sumOrNull(facts.map((file) => file?.linesRemoved));

  const present: (number | null)[] = [];
  for (const file of facts) {
    const slot = slots.get(file?.project ?? '') ?? null;
    if (!present.includes(slot)) present.push(slot);
  }

  return {
    container,
    nodeCount: members.length,
    collapsed: true,
    isMatch: highlight.matchedContainerIds.has(container.id),
    entryLabel: entry ? basenameOf(entry.filePath) : '',
    linesAdded: added,
    linesRemoved: removed,
    riskCount:
      container.risks.length + members.reduce((total, node) => total + node.risks.length, 0),
    colourSlots: present,
  };
}

function sumOrNull(values: readonly (number | null | undefined)[]): number | null {
  const known = values.filter((value): value is number => typeof value === 'number');
  return known.length === 0 ? null : known.reduce((total, value) => total + value, 0);
}

function basenameOf(path: string): string {
  const cut = path.lastIndexOf('/');
  return cut < 0 ? path : path.slice(cut + 1);
}
