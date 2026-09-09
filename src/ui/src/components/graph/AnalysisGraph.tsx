import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Node,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import type { AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { buildElkGraph } from '@/graph/elkGraph';
import { toFlowGraph, type FlowGraph } from '@/graph/flowGraph';
import { NODE_HEIGHT, NODE_WIDTH } from '@/graph/elkOptions';
import { projectColourStyle } from '@/graph/palette';
import { createWorkerLayout, inProcessLayout, type LayoutRunner } from '@/graph/runLayout';
import { searchGraph } from '@/graph/search';
import { useAppStore } from '@/store/appStore';
import { CollapsedContainerNode } from './CollapsedContainerNode';
import { ContainerNode } from './ContainerNode';
import { BundleEdge, CrossContainerEdge, EdgeMarkers, ReadingEdge } from './edges';
import { FileNode } from './FileNode';
import { GraphToolbar } from './GraphToolbar';

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

export function AnalysisGraph({ view }: { view: AnalysisView }) {
  return (
    <ReactFlowProvider>
      <GraphSurface view={view} />
    </ReactFlowProvider>
  );
}

function GraphSurface({ view }: { view: AnalysisView }) {
  const t = useT();
  const collapsed = useAppStore((state) => state.graphCollapsed);
  const search = useAppStore((state) => state.graphSearch);
  const focusedNodeId = useAppStore((state) => state.graphFocusedNodeId);
  const focusGraphNode = useAppStore((state) => state.focusGraphNode);
  const toggleCollapsed = useAppStore((state) => state.toggleContainerCollapsed);
  const onlyRenderVisible = useAppStore((state) => state.graphOnlyRenderVisible);

  const { setCenter, fitView } = useReactFlow();

  const [graph, setGraph] = useState<FlowGraph>(EMPTY_GRAPH);
  const [layoutError, setLayoutError] = useState<string | null>(null);
  const [laidOutOnce, setLaidOutOnce] = useState(false);

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
    };
  }, [view, search, focusedNodeId]);

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
      if (!node) return;

      // Expand first: centring on a node inside a folded cluster would pan to an empty patch of
      // canvas and look broken.
      if (collapsed.has(node.containerId)) toggleCollapsed(node.containerId);
      focusGraphNode(nodeId);
    },
    [view.nodes, collapsed, toggleCollapsed, focusGraphNode],
  );

  // Centring happens after the node exists in the laid-out graph, which — when the container had
  // to be expanded first — is a relayout later than the click.
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
      { zoom: 1, duration: 400 },
    );
  }, [focusedNodeId, graph.nodes, setCenter]);

  if (layoutError !== null) {
    return (
      <div className="flex h-full items-center justify-center p-8 text-sm text-destructive">
        {t('analysis.graph.layoutFailed')}
      </div>
    );
  }

  return (
    <div className="relative flex h-full min-h-0 flex-col">
      <GraphToolbar
        view={view}
        colours={graph.colours}
        onSelectSearchHit={focusNode}
        onFitView={() => void fitView({ duration: 300 })}
      />

      <div className="relative min-h-0 flex-1">
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
      </div>
    </div>
  );
}

/** Re-labels the existing nodes without touching a single coordinate. */
function applyHighlight(
  nodes: readonly Node[],
  highlight: { matchedNodeIds: ReadonlySet<string>; matchedContainerIds: ReadonlySet<string>; focusedNodeId: string | null },
): Node[] {
  return nodes.map((node) => {
    const isMatch =
      node.type === 'file'
        ? highlight.matchedNodeIds.has(node.id)
        : highlight.matchedContainerIds.has(node.id);
    const isFocused = highlight.focusedNodeId === node.id;

    if (node.data.isMatch === isMatch && node.data.isFocused === isFocused) return node;

    return { ...node, data: { ...node.data, isMatch, isFocused } };
  });
}

/** The minimap carries the project colours, so the overview is the same map as the diagram. */
function minimapColour(node: Node): string {
  if (node.type === 'file') {
    return projectColourStyle((node.data as { colourSlot: number | null }).colourSlot).rail;
  }

  return 'var(--muted)';
}
