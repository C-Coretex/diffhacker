/**
 * Every layout constant, in one file.
 *
 * Requirement 12 asks for a snapshot test that fails when a layout constant changes. That is only
 * a meaningful test if there is somewhere to change one — so the numbers live here rather than
 * scattered through `elkGraph.ts`, and `layout.snapshot.test.ts` pins what they produce.
 */

/** A node box. Fixed, because ELK needs a size before it can place anything, and because three
 *  hundred boxes of the same size are scannable in a way three hundred different ones are not. */
export const NODE_WIDTH = 260;
export const NODE_HEIGHT = 96;

/** A collapsed container: wider than a node so its title has room, the same height so a row of
 *  mixed collapsed and expanded containers still lines up. */
export const COLLAPSED_WIDTH = 300;
export const COLLAPSED_HEIGHT = 96;

/** Room for the container's own title bar, above where its first node can be placed. */
export const CONTAINER_HEADER = 48;

export const ELK_ROOT_OPTIONS: Record<string, string> = {
  // Containers are independent sub-graphs — no edge in the ELK input ever crosses one — so the
  // root's only job is packing them. `rectpacking` fills the viewport in two dimensions; the
  // layered algorithm would string thirty clusters out in one very long row.
  'elk.algorithm': 'rectpacking',
  'elk.spacing.nodeNode': '48',
  'elk.padding': '[top=24,left=24,bottom=24,right=24]',

  // Containers are emitted in displayOrder and rectpacking respects the order it is given, so the
  // cluster the model wants read first is the one at the top left.
  'elk.rectpacking.widthApproximation.strategy': 'GREEDY',
};

export const ELK_CONTAINER_OPTIONS: Record<string, string> = {
  'elk.algorithm': 'layered',
  'elk.direction': 'DOWN',

  // Each container is laid out on its own. Nothing crosses a boundary in the input, and this says
  // so to ELK rather than leaving it to discover it.
  'elk.hierarchyHandling': 'SEPARATE_CHILDREN',

  'elk.layered.layering.strategy': 'NETWORK_SIMPLEX',
  'elk.layered.nodePlacement.strategy': 'BRANDES_KOEPF',
  'elk.edgeRouting': 'ORTHOGONAL',

  // The model's rank is the order children are emitted in, and these two are what make ELK honour
  // that order inside a layer instead of reordering purely to minimise crossings. Requirement 3
  // is "follow the LLM's ranking"; without these, the ranking reaches ELK and is discarded.
  'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
  'elk.layered.crossingMinimization.forceNodeModelOrder': 'true',

  'elk.spacing.nodeNode': '32',
  'elk.layered.spacing.nodeNodeBetweenLayers': '48',
  'elk.padding': `[top=${CONTAINER_HEADER},left=24,bottom=24,right=24]`,
};

/**
 * Pins the entry node to the first layer.
 *
 * Verification step 2 — "the entry node of every container is visually at the top" — then holds by
 * construction rather than by hope. Without it a container whose entry node has an incoming edge
 * from one of its own consequences would place that consequence above the starting point, which
 * is exactly backwards from how the answer is meant to be read.
 */
export const ENTRY_NODE_OPTIONS: Record<string, string> = {
  'elk.layered.layering.layerConstraint': 'FIRST',
};
