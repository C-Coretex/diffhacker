import type { AnalysisContainerInfo, AnalysisNodeInfo, AnalysisView } from '@/contracts';

/**
 * The reviewer's own decluttering tool: hide everything outside the projects they are looking at
 * right now.
 *
 * This is deliberately not what §0.2.5 forbids. The analysis itself is untouched — `applyProjectFilter`
 * only ever runs on a copy fed to the diagram surface, never on the stored result, and every other
 * screen (the diff panel, the reading order, the risk register, the overview band) still walks the
 * model's whole answer. `AnalysisScreen.tsx` shows a standing banner the whole time a filter is
 * active, naming what is hidden and offering the one click back to everything — so a reviewer who
 * forgot they narrowed the view is told, not left to wonder why a file they know changed is missing.
 *
 * A file with no project name is never hidden by this: there is no named project to uncheck it
 * from, and inventing a bucket for it would be the made-up category `palette.ts` already refuses.
 */
export function applyProjectFilter(view: AnalysisView, hiddenProjects: ReadonlySet<string>): AnalysisView {
  if (hiddenProjects.size === 0) return view;

  const projectByPath = new Map(view.changedFiles.map((file) => [file.path, file.project]));

  const isHidden = (node: AnalysisNodeInfo): boolean => {
    const project = projectByPath.get(node.filePath);
    return project !== undefined && project.length > 0 && hiddenProjects.has(project);
  };

  const nodes = view.nodes.filter((node) => !isHidden(node));
  if (nodes.length === view.nodes.length) return view;

  const visibleIds = new Set(nodes.map((node) => node.id));

  const containers = view.containers.reduce<AnalysisContainerInfo[]>((kept, container) => {
    const nodeIds = container.nodeIds.filter((id) => visibleIds.has(id));
    if (nodeIds.length === 0) return kept;

    const entryNodeId = nodeIds.includes(container.entryNodeId) ? container.entryNodeId : nodeIds[0]!;
    kept.push({ ...container, nodeIds, entryNodeId });
    return kept;
  }, []);

  const edges = view.edges.filter(
    (edge) => visibleIds.has(edge.sourceNodeId) && visibleIds.has(edge.targetNodeId),
  );

  return { ...view, nodes, containers, edges };
}

/** How many files a filter is currently hiding, for the banner's count. */
export function hiddenFileCount(view: AnalysisView, hiddenProjects: ReadonlySet<string>): number {
  if (hiddenProjects.size === 0) return 0;

  const projectByPath = new Map(view.changedFiles.map((file) => [file.path, file.project]));

  return view.nodes.filter((node) => {
    const project = projectByPath.get(node.filePath);
    return project !== undefined && project.length > 0 && hiddenProjects.has(project);
  }).length;
}
