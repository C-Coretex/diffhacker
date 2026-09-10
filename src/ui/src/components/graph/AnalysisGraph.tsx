import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Edge,
  type Node,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import type { AnalysisContainerInfo, AnalysisGroupingMode, AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { containerQueue } from '@/components/diff/containerQueue';
import { useEditors, useOpenInEditor } from '@/components/diff/useEditors';
import { useReviewMarks } from '@/components/diff/useReviewMarks';
import { buildElkGraph } from '@/graph/elkGraph';
import {
  toFlowGraph,
  type FlowGraph,
  type GraphHighlight,
  type ReadingEdgeData,
} from '@/graph/flowGraph';
import { NODE_HEIGHT, NODE_WIDTH } from '@/graph/elkOptions';
import { projectColourStyle } from '@/graph/palette';
import { createWorkerLayout, inProcessLayout, type LayoutRunner } from '@/graph/runLayout';
import { searchGraph } from '@/graph/search';
import { useAppStore } from '@/store/appStore';
import { CollapsedContainerNode } from './CollapsedContainerNode';
import { ContainerNode } from './ContainerNode';
import { BundleEdge, CrossContainerEdge, EdgeMarkers, ReadingEdge } from './edges';
import { FileNode } from './FileNode';
import { GraphActionsProvider, type GraphActions } from './graphActions';
import { GraphHoverCard } from './GraphHoverCard';
import { GraphToolbar } from './GraphToolbar';
import { ContainerHoverCard, EdgeHoverCard, NodeHoverCard } from './hoverCards';
import { useEdgePan } from './useEdgePan';
import { useHoverTarget, type HoverTarget } from './useHoverTarget';

/**
 * The diagram. Iteration 8's whole point, and CLAUDE.md's "the diagram is the product".
 *
 * Defined outside the component: React Flow warns — correctly — that a new object here on every
 * render remounts every node on the canvas, which at three hundred nodes is the difference between
 * a smooth pan and a stutter.
 */
const NODE_TYPES = {
  file: FileNode,
  container: ContainerNode,
  collapsedContainer: CollapsedContainerNode,
};

const EDGE_TYPES = {
  reading: ReadingEdge,
  crossContainer: CrossContainerEdge,
  bundle: BundleEdge,
};

const EMPTY_GRAPH: FlowGraph = { nodes: [], edges: [], colours: [] };

export function AnalysisGraph({ view, onChangeGrouping, groupingBusy }: GraphProps) {
  return (
    <ReactFlowProvider>
      <GraphSurface view={view} onChangeGrouping={onChangeGrouping} groupingBusy={groupingBusy} />
    </ReactFlowProvider>
  );
}

interface GraphProps {
  readonly view: AnalysisView;

  /**
   * Switching grouping is the screen's business, not the diagram's: it is a host call that replaces
   * the view, and this surface only ever draws the view it is given. Passed down rather than reached
   * for through the store so the diagram stays a function of its props.
   */
  readonly onChangeGrouping: (grouping: AnalysisGroupingMode) => void;

  readonly groupingBusy: boolean;
}

function GraphSurface({ view, onChangeGrouping, groupingBusy }: GraphProps) {
  const t = useT();
  const collapsed = useAppStore((state) => state.graphCollapsed);
  const search = useAppStore((state) => state.graphSearch);
  const focusedNodeId = useAppStore((state) => state.graphFocusedNodeId);
  const revealNode = useAppStore((state) => state.revealGraphNode);
  const onlyRenderVisible = useAppStore((state) => state.graphOnlyRenderVisible);
  const currentNodeId = useAppStore((state) => state.diffNodeId);
  const reviewedNodeIds = useAppStore((state) => state.reviewedNodeIds);
  const openDiff = useAppStore((state) => state.openDiffFor);
  const openContainerDiff = useAppStore((state) => state.openContainerDiff);
  const diffPanelWidth = useAppStore((state) => state.diffPanelWidth);
  const diffFullScreen = useAppStore((state) => state.diffFullScreen);

  /** The width the last centring was done at, so a drag pans without animating. */
  const lastWidth = useRef(diffPanelWidth);

  /**
   * The canvas itself, which is two things at once: what `useEdgePan` listens on, and the box a hover
   * card is not allowed out of. The second is why the card no longer lands on top of the diff panel —
   * Radix flips it to the other side of its node rather than crossing this rectangle.
   */
  const canvas = useRef<HTMLDivElement>(null);

  const { setCenter, fitView } = useReactFlow();

  const [graph, setGraph] = useState<FlowGraph>(EMPTY_GRAPH);
  const [layoutError, setLayoutError] = useState<string | null>(null);
  const [laidOutOnce, setLaidOutOnce] = useState(false);

  const hover = useHoverTarget();

  // A press that lands on one of the diagram's lines still pans it. @see useEdgePan
  useEdgePan(canvas, hover.hide);

  // Asked once for the whole diagram rather than once per box. @see graphActions.ts
  const editors = useEditors();
  const openInEditor = useOpenInEditor();
  const marks = useReviewMarks(view.repositoryPath);

  /**
   * Reading a cluster whole, from wherever that was asked for — its title bar, or a double-click on
   * it. The queue's first file is the analysis's own reading order restricted to the cluster, so
   * "open every file" opens them in the sequence the model recommended rather than in the order it
   * happened to list them.
   */
  const openWholeContainer = useCallback(
    (container: AnalysisContainerInfo) => {
      const first = containerQueue(view, container.id)[0];
      if (first) openContainerDiff(container.id, first.id);
    },
    [view, openContainerDiff],
  );

  /**
   * What the boxes and the title bars are allowed to do.
   *
   * Every one of them dismisses the pinned card first. Iteration 9 made a click on a box keep that
   * box's card open; pressing a button *on* the box says the reading is over and something should
   * happen, and leaving the card up would drop it over the panel that just opened.
   */
  const actions = useMemo<GraphActions>(
    () => ({
      editors,
      openDiff: (node) => {
        hover.unpin();
        openDiff(node.id, node.containerId);
      },
      openContainer: (container) => {
        hover.unpin();
        openWholeContainer(container);
      },
      toggleReviewed: (node, reviewed) => {
        hover.unpin();
        marks.mark([node.id], reviewed);
      },
      openInEditor: (node, facts, editor) => {
        hover.unpin();
        openInEditor({ node, facts, repositoryPath: view.repositoryPath }, editor);
      },
      // A cluster's card is the title bar's, not the region's. @see ContainerNode
      showContainerCard: (container, element) =>
        hover.show({ kind: 'container', id: container.id, rect: element.getBoundingClientRect() }),
      hideContainerCard: () => hover.hide(),
      pinContainerCard: (container, element) =>
        hover.pinTo({ kind: 'container', id: container.id, rect: element.getBoundingClientRect() }),
    }),
    [editors, hover, openDiff, openWholeContainer, marks, openInEditor, view.repositoryPath],
  );

  // One worker for the life of the surface. Building an ELK instance is not cheap and collapsing a
  // container has to feel immediate.
  const runnerRef = useRef<{ run: LayoutRunner; dispose: () => void } | null>(null);
  if (runnerRef.current === null) {
    runnerRef.current =
      typeof Worker === 'undefined'
        ? { run: inProcessLayout, dispose: () => {} }
        : createWorkerLayout();
  }

  useEffect(() => () => runnerRef.current?.dispose(), []);

  // Highlight is derived rather than stored: the search box already holds the query, and a second
  // copy of "what matches" is a second thing to keep in step.
  const highlight = useMemo(() => {
    const hits = searchGraph(view, search);
    return {
      matchedNodeIds: new Set(hits.map((hit) => hit.nodeId)),
      matchedContainerIds: new Set(hits.filter((hit) => hit.field === 'containerTitle').map((hit) => hit.containerId)),
      focusedNodeId: focusedNodeId ?? null,
      currentNodeId: currentNodeId ?? null,
      reviewedNodeIds,
    };
  }, [view, search, focusedNodeId, currentNodeId, reviewedNodeIds]);

  const elkGraph = useMemo(() => buildElkGraph(view, collapsed), [view, collapsed]);

  useEffect(() => {
    let cancelled = false;
    const runner = runnerRef.current;
    if (!runner) return;

    runner
      .run(elkGraph)
      .then((laidOut) => {
        // A layout that finished after the reviewer collapsed something else would paint the
        // older arrangement over the newer one.
        if (cancelled) return;
        setGraph(toFlowGraph(view, laidOut, collapsed, highlight));
        setLayoutError(null);
        setLaidOutOnce(true);
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setLayoutError(error instanceof Error ? error.message : String(error));
      });

    return () => {
      cancelled = true;
    };
    // `highlight` is deliberately not a dependency: highlighting changes no coordinate, and
    // relaying out three hundred nodes on every keystroke in the search box is the one thing
    // guaranteed to make this feel slow. The effect below repaints for it instead.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [elkGraph, view, collapsed]);

  // Highlighting without relayout: same positions, new node data.
  useEffect(() => {
    if (!laidOutOnce) return;
    setGraph((current) => ({ ...current, nodes: applyHighlight(current.nodes, highlight) }));
  }, [highlight, laidOutOnce]);

  const focusNode = useCallback(
    (nodeId: string) => {
      const node = view.nodes.find((candidate) => candidate.id === nodeId);
      if (node) revealNode(nodeId, node.containerId);
    },
    [view.nodes, revealNode],
  );

  /**
   * The pointer arrived on something. React Flow hands over the element it decorated, and its
   * client rectangle is what the card is drawn beside — measured rather than derived from the
   * viewport transform, so it is right at every zoom level without this code knowing the zoom.
   */
  const enter = useCallback(
    (kind: HoverTarget['kind'], id: string, event: { currentTarget: Element }) => {
      hover.show({ kind, id, rect: event.currentTarget.getBoundingClientRect() });
    },
    [hover],
  );

  /** The same thing, kept. Clicking is how a reviewer says "I want to read this, not glance at it". */
  const keep = useCallback(
    (kind: HoverTarget['kind'], id: string, event: { currentTarget: Element }) => {
      hover.pinTo({ kind, id, rect: event.currentTarget.getBoundingClientRect() });
    },
    [hover],
  );

  // A card anchored to a box that has since moved, folded away or been laid out again is a card
  // pointing at nothing. A pinned one survives — `hide` declines while pinned — because a reviewer
  // reading a pinned card while they collapse a neighbouring cluster has not asked to lose it.
  useEffect(() => {
    hover.hide();
    // Only when the arrangement itself changed. `hover` is stable enough to depend on, but adding
    // it would fire this on every pointer move.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [elkGraph, collapsed]);

  // The card is portalled to the document body, so hiding the diagram does not hide it. A pinned card
  // left floating over a full-screen diff is the same complaint the collision boundary answers for
  // the side-by-side case, and the boundary cannot answer this one: a hidden canvas has no rectangle.
  useEffect(() => {
    if (diffFullScreen) hover.unpin();
    // `hover` is a fresh object on every pointer move; depending on it would unpin continuously for
    // as long as the panel stayed maximised.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [diffFullScreen]);

  // Brightening the hovered line, the same way `applyHighlight` re-labels nodes: no coordinate is
  // touched, and edges whose state did not change keep their object identity.
  useEffect(() => {
    const hoveredId = hover.target?.kind === 'edge' ? hover.target.id : null;
    setGraph((current) => ({ ...current, edges: applyEdgeHover(current.edges, hoveredId) }));
  }, [hover.target]);

  // Centring happens after the node exists in the laid-out graph, which — when the container had
  // to be expanded first — is a relayout later than the click.
  //
  // `diffPanelWidth` is a dependency, and that is requirement 10 rather than a nicety: dragging the
  // divider narrows the canvas without moving the viewport, so the node the reviewer is reading
  // slides out of the strip they can still see. Re-centring on every drag keeps the promise that
  // their position is visible in the diagram at all times, including at the panel's widest.
  useEffect(() => {
    if (!focusedNodeId) return;

    const placed = graph.nodes.find((node) => node.id === focusedNodeId);
    if (!placed) return;

    const parent = placed.parentId
      ? graph.nodes.find((node) => node.id === placed.parentId)
      : undefined;

    setCenter(
      (parent?.position.x ?? 0) + placed.position.x + NODE_WIDTH / 2,
      (parent?.position.y ?? 0) + placed.position.y + NODE_HEIGHT / 2,
      // No animation while the divider is moving: sixty queued four-hundred-millisecond pans is a
      // canvas that keeps drifting after the pointer has stopped.
      { zoom: 1, duration: diffPanelWidth === lastWidth.current ? 400 : 0 },
    );

    lastWidth.current = diffPanelWidth;
  }, [focusedNodeId, graph.nodes, setCenter, diffPanelWidth]);

  if (layoutError !== null) {
    return (
      <div className="flex h-full items-center justify-center p-8 text-sm text-destructive">
        {t('analysis.graph.layoutFailed')}
      </div>
    );
  }

  return (
    <GraphActionsProvider value={actions}>
      {/*
        The active grouping is on the wrapper as well as in the toolbar's own state, because "which
        picture is this?" is a question about the whole diagram and one an end-to-end test has to be
        able to answer without reading prose.
      */}
      <div
        className="relative flex h-full min-h-0 flex-col"
        data-grouping-mode={view.grouping}
      >
        <GraphToolbar
          view={view}
          colours={graph.colours}
          onSelectSearchHit={focusNode}
          onFitView={() => void fitView({ duration: 300 })}
          onChangeGrouping={onChangeGrouping}
          groupingBusy={groupingBusy}
        />

        <div ref={canvas} className="relative min-h-0 flex-1">
          <EdgeMarkers />

          {!laidOutOnce && (
            <p className="absolute inset-0 z-10 flex items-center justify-center text-sm text-muted-foreground">
              {t('analysis.graph.layingOut')}
            </p>
          )}

          <ReactFlow
            nodes={graph.nodes}
            edges={graph.edges}
            nodeTypes={NODE_TYPES}
            edgeTypes={EDGE_TYPES}
            fitView
            minZoom={0.05}
            maxZoom={2}
            // Requirement 9: zoom, pan, fit and a minimap — and nothing that moves a node. There is
            // no manual layout in this product and so nothing to persist (§0.6).
            nodesDraggable={false}
            nodesConnectable={false}
            edgesFocusable={false}
            elementsSelectable
            panOnScroll
            proOptions={{ hideAttribution: false }}
            onlyRenderVisibleElements={onlyRenderVisible}
            aria-label={t('analysis.graph.canvasLabel')}
            // Iteration 9's whole interaction. Hovering costs nothing but a lookup in the view the
            // screen already holds — no call of any kind leaves the renderer because a pointer moved.
            //
            // An expanded container is the one node type that answers to none of these: its card
            // belongs to its title bar, which asks for it through `GraphActions`. The region itself is
            // canvas the reviewer works over, and it explained itself every time they crossed it.
            onNodeMouseEnter={(event, node) => {
              if (node.type === 'container') return;
              enter(node.type === 'file' ? 'node' : 'container', node.id, event);
            }}
            onNodeMouseLeave={(_, node) => {
              if (node.type === 'container') return;
              hover.hide();
            }}
            onEdgeMouseEnter={(event, edge) => enter('edge', edge.id, event)}
            onEdgeMouseLeave={() => hover.hide()}
            // And clicking keeps the card. Hovering is for glancing; anyone who wants to read the
            // explanation, scroll it or select out of it should not have to hold a hand still.
            onNodeClick={(event, node) => {
              if (node.type === 'container') return;
              keep(node.type === 'file' ? 'node' : 'container', node.id, event);
            }}
            onEdgeClick={(event, edge) => keep('edge', edge.id, event)}
            // Iteration 10's second way into the diff. The first is the row of buttons on the box
            // itself; this is for anyone who would rather aim at the whole box than at a button on it.
            // The single click stays what Iteration 9 spent it on.
            //
            // On a cluster it means the cluster: every file in it, as a queue. A double-click on a
            // region and a press of its "open every file" button are the same intention.
            onNodeDoubleClick={(_, node) => {
              hover.unpin();

              if (node.type === 'file') {
                const target = view.nodes.find((candidate) => candidate.id === node.id);
                if (target) openDiff(target.id, target.containerId);
                return;
              }

              const container = view.containers.find((candidate) => candidate.id === node.id);
              if (container) openWholeContainer(container);
            }}
            // Double-clicking no longer zooms, and it has to not: React Flow's zoom is d3-zoom, whose
            // `dblclick.zoom` listener sits on the pane as a native handler and stops the event dead
            // before React — which delivers every synthetic event at the document root — ever sees it.
            // With it on, the handler above is simply never called. Nothing is lost: scroll, the
            // controls and the Fit button are all still there, and now a double-click on the diagram
            // means exactly one thing.
            zoomOnDoubleClick={false}
            // Clicking the background is how you put the card away again.
            onPaneClick={() => hover.unpin()}
            // Panning or zooming moves the diagram out from under an anchored card.
            onMoveStart={() => hover.hide()}
          >
            <Background gap={24} className="!bg-background" />
            <Controls showInteractive={false} />
            <MiniMap
              pannable
              zoomable
              nodeStrokeWidth={2}
              nodeColor={(node) => minimapColour(node)}
              className="!bg-card"
            />
          </ReactFlow>

          <GraphHoverCard controller={hover} boundary={canvas}>
            <HoverContent view={view} target={hover.target} edges={graph.edges} />
          </GraphHoverCard>
        </div>
      </div>
    </GraphActionsProvider>
  );
}

/**
 * Which card the pointer earned.
 *
 * Every branch reads the analysis the screen already has. There is no loading state here because
 * there is nothing to load — §0.2.8 produced the whole result before any of it was shown, and this
 * is the iteration that finally spends it.
 */
function HoverContent({
  view,
  target,
  edges,
}: {
  view: AnalysisView;
  target: HoverTarget | null;
  edges: readonly Edge[];
}) {
  if (!target) return null;

  if (target.kind === 'container') {
    const container = view.containers.find((candidate) => candidate.id === target.id);
    return container ? <ContainerHoverCard container={container} view={view} /> : null;
  }

  if (target.kind === 'edge') {
    const data = edges.find((edge) => edge.id === target.id)?.data as ReadingEdgeData | undefined;
    if (!data || data.edges.length === 0) return null;

    return <EdgeHoverCard edges={data.edges} view={view} count={data.count} />;
  }

  const node = view.nodes.find((candidate) => candidate.id === target.id);
  if (!node) return null;

  return (
    <NodeHoverCard
      node={node}
      container={view.containers.find((candidate) => candidate.id === node.containerId)}
      facts={view.changedFiles.find((file) => file.path === node.filePath)}
    />
  );
}

/** Re-labels the hovered edge without touching a single coordinate. @see applyHighlight */
function applyEdgeHover(edges: readonly Edge[], hoveredId: string | null): Edge[] {
  return edges.map((edge) => {
    const isHovered = edge.id === hoveredId;
    if ((edge.data?.isHovered ?? false) === isHovered) return edge;

    return { ...edge, data: { ...edge.data, isHovered } };
  });
}

/**
 * Re-labels the existing nodes without touching a single coordinate.
 *
 * Iteration 10's two new channels come through here rather than through a relayout for exactly the
 * reason the search highlight does: marking a node reviewed changes no position, and rearranging
 * three hundred boxes because a checkbox moved would make every mark feel expensive.
 */
function applyHighlight(nodes: readonly Node[], highlight: GraphHighlight): Node[] {
  return nodes.map((node) => {
    const isMatch =
      node.type === 'file'
        ? highlight.matchedNodeIds.has(node.id)
        : highlight.matchedContainerIds.has(node.id);
    const isFocused = highlight.focusedNodeId === node.id;
    const isCurrent = highlight.currentNodeId === node.id;
    const isReviewed = highlight.reviewedNodeIds.has(node.id);

    if (
      node.data.isMatch === isMatch &&
      node.data.isFocused === isFocused &&
      node.data.isCurrent === isCurrent &&
      node.data.isReviewed === isReviewed
    ) {
      return node;
    }

    return { ...node, data: { ...node.data, isMatch, isFocused, isCurrent, isReviewed } };
  });
}

/** The minimap carries the project colours, so the overview is the same map as the diagram. */
function minimapColour(node: Node): string {
  if (node.type === 'file') {
    return projectColourStyle((node.data as { colourSlot: number | null }).colourSlot).rail;
  }

  return 'var(--muted)';
}
