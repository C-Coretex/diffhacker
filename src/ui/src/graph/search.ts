import type { AnalysisView } from '@/contracts';

/** What a hit matched on, so the results can be grouped and the reason shown. */
export type SearchField = 'path' | 'nodeTitle' | 'containerTitle';

export interface SearchHit {
  /** The node to focus. For a container hit, its entry node — the place to start reading. */
  readonly nodeId: string;
  readonly containerId: string;
  readonly label: string;
  readonly detail: string;
  readonly field: SearchField;
}

/** How many hits the box offers before it stops being a list and starts being a wall. */
export const MAX_HITS = 25;

/**
 * Finds nodes by file path, node title or container title.
 *
 * Requirements 10 and 13 both ask for file name. This matches titles too, deliberately: the box
 * looks like a search box, and a reviewer who types the name of a cluster they can see on the
 * screen and gets nothing has been told the box is broken. Each hit says which field it matched,
 * so a title hit is never mistaken for a filename hit.
 *
 * Case-insensitive substring rather than fuzzy matching. A reviewer searching a diagram is looking
 * for something they know the name of; fuzzy matching mostly adds hits they did not want.
 */
export function searchGraph(view: AnalysisView, query: string): SearchHit[] {
  const needle = query.trim().toLowerCase();
  if (needle.length === 0) return [];

  const hits: SearchHit[] = [];
  const seen = new Set<string>();

  const add = (hit: SearchHit): void => {
    const key = `${hit.field}:${hit.nodeId}`;
    if (seen.has(key)) return;
    seen.add(key);
    hits.push(hit);
  };

  // Path first: it is what the requirement asks for, so it is what the list leads with.
  for (const node of view.nodes) {
    if (node.filePath.toLowerCase().includes(needle)) {
      add({
        nodeId: node.id,
        containerId: node.containerId,
        label: basename(node.filePath),
        detail: node.filePath,
        field: 'path',
      });
    }
  }

  for (const node of view.nodes) {
    if (node.title.toLowerCase().includes(needle)) {
      add({
        nodeId: node.id,
        containerId: node.containerId,
        label: node.title,
        detail: node.filePath,
        field: 'nodeTitle',
      });
    }
  }

  for (const container of view.containers) {
    if (!container.title.toLowerCase().includes(needle)) continue;

    // A container hit focuses its entry node rather than the container: "go here" is more useful
    // than "go somewhere in here", and the entry node is the model's own answer to where.
    add({
      nodeId: container.entryNodeId,
      containerId: container.id,
      label: container.title,
      detail: container.summary,
      field: 'containerTitle',
    });
  }

  return hits.slice(0, MAX_HITS);
}

function basename(path: string): string {
  const cut = path.lastIndexOf('/');
  return cut < 0 ? path : path.slice(cut + 1);
}
