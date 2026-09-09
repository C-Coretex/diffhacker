import type {
  AnalysisContainerInfo,
  AnalysisEdgeInfo,
  AnalysisNodeInfo,
  AnalysisView,
  ChangedFileFactsInfo,
} from '@/contracts';

/**
 * A small analysis to lay out, shared by every graph test.
 *
 * Fixed rather than generated, because `layout.snapshot.test.ts` snapshots the coordinates it
 * produces: a fixture that drifts is a snapshot that fails for reasons nobody changed.
 */
export function testView(overrides: Partial<AnalysisView> = {}): AnalysisView {
  return {
    repositoryPath: 'C:/repo',
    hasAnalysis: true,
    analysisId: 'analysis1',
    summary: 'A contract grew a field and everything downstream followed.',
    overallRisks: [],
    readingOrder: [],
    containers: [container('core', 1, 'src/Contract.cs', ['src/Contract.cs', 'src/Caller.cs', 'src/Notes.md'])],
    nodes: [
      node('src/Contract.cs', 'core', 1, ['changed', 'entry_point']),
      node('src/Caller.cs', 'core', 2, ['changed']),
      node('src/Notes.md', 'core', 3, ['added']),
    ],
    edges: [edge('src/Contract.cs', 'src/Caller.cs', 'direct')],
    diagnostics: [],
    changedFiles: [
      file('src/Contract.cs', 'modified', 12, 3, 'C#', 'DiffHacker.Core'),
      file('src/Caller.cs', 'modified', 4, 1, 'C#', 'DiffHacker.Core'),
      file('src/Notes.md', 'added', 20, 0, 'Markdown', 'docs'),
    ],
    ...overrides,
  };
}

/** The same change split across two clusters, with one edge between them. */
export function twoContainerView(): AnalysisView {
  return testView({
    containers: [
      container('core', 1, 'src/Contract.cs', ['src/Contract.cs', 'src/Caller.cs']),
      container('docs', 2, 'src/Notes.md', ['src/Notes.md']),
    ],
    nodes: [
      node('src/Contract.cs', 'core', 1, ['changed', 'entry_point']),
      node('src/Caller.cs', 'core', 2, ['changed']),
      node('src/Notes.md', 'docs', 1, ['added', 'entry_point']),
    ],
    edges: [
      edge('src/Contract.cs', 'src/Caller.cs', 'direct'),
      edge('src/Contract.cs', 'src/Notes.md', 'conceptual', true),
      edge('src/Caller.cs', 'src/Notes.md', 'conceptual', true),
    ],
  });
}

export function container(
  id: string,
  displayOrder: number,
  entryNodeId: string,
  nodeIds: string[],
): AnalysisContainerInfo {
  return {
    id,
    title: `Cluster ${id}`,
    summary: `What ${id} is about.`,
    explanation: `The longer account of ${id}.`,
    risks: [],
    displayOrder,
    entryNodeId,
    nodeIds,
  };
}

export function node(
  id: string,
  containerId: string,
  rank: number,
  states: AnalysisNodeInfo['states'],
): AnalysisNodeInfo {
  return {
    id,
    containerId,
    filePath: id,
    symbol: '',
    startLine: 0,
    endLine: 0,
    title: `Title for ${id}`,
    whatChanged: 'Something changed.',
    whyItChanged: 'Because of a decision upstream.',
    howItAffectsOthers: '',
    implementationNotes: '',
    risks: [],
    importance: 3,
    rank,
    states,
  };
}

export function edge(
  sourceNodeId: string,
  targetNodeId: string,
  kind: AnalysisEdgeInfo['kind'],
  crossesContainers = false,
): AnalysisEdgeInfo {
  return {
    sourceNodeId,
    targetNodeId,
    kind,
    explanation: `Read ${targetNodeId} after ${sourceNodeId}.`,
    risks: [],
    crossesContainers,
  };
}

export function file(
  path: string,
  status: ChangedFileFactsInfo['status'],
  linesAdded: number | undefined,
  linesRemoved: number | undefined,
  language: string,
  project: string,
): ChangedFileFactsInfo {
  return { path, status, linesAdded, linesRemoved, isBinary: false, language, project };
}
