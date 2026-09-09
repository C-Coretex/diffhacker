import {
  BaseEdge,
  EdgeLabelRenderer,
  getBezierPath,
  getSmoothStepPath,
  type EdgeProps,
} from '@xyflow/react';
import type { ReadingEdgeData } from '@/graph/flowGraph';

/**
 * The three kinds of line on the diagram, in one file because their only real difference is stroke
 * and that is a comparison worth being able to make by reading down a page.
 *
 * | | drawn as | means |
 * |---|---|---|
 * | `ReadingEdge` | solid, or dashed | one of the model's edges inside a container: solid for direct, dashed for conceptual |
 * | `CrossContainerEdge` | faint and dashed, curved | the same, but crossing a container boundary — and kept out of the layout entirely |
 * | `BundleEdge` | thicker, labelled ×n | several edges that collapsed onto one pair |
 *
 * Requirement 4 wants direct and conceptual separable at a glance and at low zoom, so the
 * difference is stroke pattern and not colour: a dash survives being three pixels tall, and a hue
 * difference does not, particularly for a colour-blind reader.
 *
 * **Every line is wider than it looks.** Iteration 9 puts an explanation on hovering an edge, and a
 * one-and-a-half pixel line is not a hover target — least of all the faint cross-container ones,
 * which are deliberately the hardest to see. `interactionWidth` is React Flow's own answer: a
 * transparent stroke over the drawn one, rendered by the library, no extra element of ours. The
 * faint lines get the wider one because they are the ones that need it.
 */

const ARROW = 'url(#reading-arrow)';
const FAINT_ARROW = 'url(#faint-arrow)';

/** How wide the invisible hit area is, in pixels of graph space. */
const HIT_WIDTH = 20;
const FAINT_HIT_WIDTH = 28;

/**
 * An edge the pointer is on reads at full strength.
 *
 * Not decoration: the card that follows describes *one* relationship, and on a dense diagram the
 * reviewer has to be able to see which of several nearby lines they actually caught before they
 * start reading about it.
 */
function hoverStyle(hovered: boolean | undefined, base: number, opacity?: number) {
  return hovered
    ? { stroke: 'var(--primary)', strokeWidth: base + 1, strokeOpacity: 1 }
    : { stroke: 'var(--muted-foreground)', strokeWidth: base, strokeOpacity: opacity };
}

/** An edge ELK routed, inside one container. */
export function ReadingEdge({ data, ...props }: EdgeProps) {
  const [path] = getSmoothStepPath(props);
  const edge = data as ReadingEdgeData | undefined;
  const conceptual = edge?.kind === 'conceptual';

  return (
    <BaseEdge
      id={props.id}
      path={path}
      markerEnd={ARROW}
      interactionWidth={HIT_WIDTH}
      style={{
        ...hoverStyle(edge?.isHovered, 1.5),
        strokeDasharray: conceptual ? '6 4' : undefined,
      }}
    />
  );
}

/**
 * An edge between containers.
 *
 * Faint, per §0.6 and requirement 7, and **curved** where the intra-container edges are
 * orthogonal — so the two are distinguishable by shape as well as by weight, which is what still
 * works when the diagram is zoomed out far enough that stroke width is a guess.
 */
export function CrossContainerEdge({ data, ...props }: EdgeProps) {
  const [path] = getBezierPath(props);
  const edge = data as ReadingEdgeData | undefined;
  const conceptual = edge?.kind !== 'direct';

  return (
    <BaseEdge
      id={props.id}
      path={path}
      markerEnd={FAINT_ARROW}
      interactionWidth={FAINT_HIT_WIDTH}
      style={{
        ...hoverStyle(edge?.isHovered, 1, 0.35),
        strokeDasharray: conceptual ? '4 6' : undefined,
      }}
    />
  );
}

/**
 * Several edges that became one when a container was collapsed.
 *
 * Deliberately not drawn as direct or as conceptual. A bundle of four edges, two of each, is
 * neither — and picking one would be the diagram claiming something the model did not. So it gets
 * its own weight and a count, and expanding the container puts the real edges back.
 */
export function BundleEdge({ data, ...props }: EdgeProps) {
  const [path, labelX, labelY] = getBezierPath(props);
  const edge = data as ReadingEdgeData | undefined;
  const count = edge?.count ?? 2;

  return (
    <>
      <BaseEdge
        id={props.id}
        path={path}
        markerEnd={FAINT_ARROW}
        interactionWidth={FAINT_HIT_WIDTH}
        style={hoverStyle(edge?.isHovered, 2.5, 0.45)}
      />
      <EdgeLabelRenderer>
        <div
          className="pointer-events-none absolute rounded bg-background/90 px-1 text-[10px] tabular-nums text-muted-foreground"
          style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
        >
          ×{count}
        </div>
      </EdgeLabelRenderer>
    </>
  );
}

/**
 * The arrowheads, defined once for the whole surface.
 *
 * React Flow's built-in marker is defined per edge, which at three hundred edges is three hundred
 * identical `<marker>` elements in the document.
 */
export function EdgeMarkers() {
  return (
    <svg className="pointer-events-none absolute size-0" aria-hidden>
      <defs>
        <marker
          id="reading-arrow"
          viewBox="0 0 10 10"
          refX="8"
          refY="5"
          markerWidth="6"
          markerHeight="6"
          orient="auto-start-reverse"
        >
          <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--muted-foreground)" />
        </marker>
        <marker
          id="faint-arrow"
          viewBox="0 0 10 10"
          refX="8"
          refY="5"
          markerWidth="5"
          markerHeight="5"
          orient="auto-start-reverse"
        >
          <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--muted-foreground)" fillOpacity="0.4" />
        </marker>
      </defs>
    </svg>
  );
}
