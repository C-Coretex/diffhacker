import type { AnalysisView } from '@/contracts';

/**
 * Which boxes on the diagram stand for more than one node: an abstraction and its implementations,
 * drawn as one box with a row per node.
 *
 * The model decided which nodes those are (§0.2.1 — the application reads no language and cannot
 * tell an interface from anything else), and the host already projected its answer onto the grouping
 * on screen, so every group here is inside one container. What is decided here is only whether to
 * draw them merged, which is the reviewer's toggle and nothing else's — the same kind of decision as
 * collapsing a container, and made in the same place for the same reason: it changes pixels, not the
 * analysis.
 */

/** The prefix on a merged box's id. A node id is a path, and no path starts like this. */
export const MERGED_BOX_PREFIX = 'merged:';

export interface MergedBox {
  readonly id: string;
  readonly containerId: string;
  /** The abstraction first, then its implementations in the order the container reads them. */
  readonly memberIds: readonly string[];
}

export interface MergePlan {
  /** Every merged box, by its own id. */
  readonly boxes: ReadonlyMap<string, MergedBox>;
  /** The box each merged node is drawn in. A node absent from this is drawn as its own box. */
  readonly boxOf: ReadonlyMap<string, string>;
}

export const NO_MERGE: MergePlan = { boxes: new Map(), boxOf: new Map() };

export function mergedBoxId(abstractionNodeId: string): string {
  return `${MERGED_BOX_PREFIX}${abstractionNodeId}`;
}

export function isMergedBox(id: string): boolean {
  return id.startsWith(MERGED_BOX_PREFIX);
}

/**
 * The boxes to merge, or none when the reviewer has merging off.
 *
 * Defensive about the model's answer even though the validator already checked it: a node that does
 * not exist, sits in another container, or was claimed by an earlier group is left as its own box.
 * Drawing it twice, or not at all, would break §0.2.5 on screen; leaving it unmerged costs nothing.
 */
export function mergePlan(view: AnalysisView, enabled: boolean): MergePlan {
  if (!enabled || view.implementationGroups.length === 0) return NO_MERGE;

  const containerOf = new Map(view.nodes.map((node) => [node.id, node.containerId]));
  const boxes = new Map<string, MergedBox>();
  const boxOf = new Map<string, string>();

  for (const group of view.implementationGroups) {
    const home = containerOf.get(group.abstractionNodeId);
    if (home === undefined || boxOf.has(group.abstractionNodeId)) continue;

    const implementations = group.implementationNodeIds.filter(
      (id) => id !== group.abstractionNodeId && containerOf.get(id) === home && !boxOf.has(id),
    );

    const unique = [...new Set(implementations)];
    if (unique.length === 0) continue;

    const box: MergedBox = {
      id: mergedBoxId(group.abstractionNodeId),
      containerId: home,
      memberIds: [group.abstractionNodeId, ...unique],
    };

    boxes.set(box.id, box);
    for (const id of box.memberIds) boxOf.set(id, box.id);
  }

  return { boxes, boxOf };
}

/** The box a node is drawn in: its merged box, or its own. */
export function unitOf(plan: MergePlan, nodeId: string): string {
  return plan.boxOf.get(nodeId) ?? nodeId;
}
