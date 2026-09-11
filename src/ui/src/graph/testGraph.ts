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
    reviewedNodeIds: [],
    grouping: 'dependency_flow',
    availableGroupings: ['dependency_flow', 'change_clusters'],
    produceChangeClusters: true,
    implementationGroups: [],
    implementationGroupsProduced: true,
    produceImplementationGroups: true,
    ...overrides,
  };
}

/**
 * An interface changed with two implementations, and a caller that reads the interface.
 *
 * `IStore.cs` is the abstraction and the entry node; `MemoryStore.cs` and `DiskStore.cs` implement
 * it; `Caller.cs` sits between them in the reading order, so the merged box has to take the place of
 * its earliest member rather than of a contiguous run. Both implementations have an edge from the
 * interface — internal to the box once merged — and each leads on to the caller they serve, so
 * merging folds two of the model's edges onto one line.
 */
export function implementationView(overrides: Partial<AnalysisView> = {}): AnalysisView {
  const ids = ['src/IStore.cs', 'src/Caller.cs', 'src/MemoryStore.cs', 'src/DiskStore.cs', 'src/Notes.md'];

  return testView({
    readingOrder: ids,
    containers: [
      container('store', 1, 'src/IStore.cs', ['src/IStore.cs', 'src/Caller.cs', 'src/MemoryStore.cs', 'src/DiskStore.cs']),
      container('docs', 2, 'src/Notes.md', ['src/Notes.md']),
    ],
    nodes: [
      node('src/IStore.cs', 'store', 1, ['changed', 'entry_point']),
      node('src/Caller.cs', 'store', 2, ['changed']),
      node('src/MemoryStore.cs', 'store', 3, ['changed']),
      node('src/DiskStore.cs', 'store', 4, ['added']),
      node('src/Notes.md', 'docs', 1, ['added', 'entry_point']),
    ],
    edges: [
      edge('src/IStore.cs', 'src/MemoryStore.cs', 'direct'),
      edge('src/IStore.cs', 'src/DiskStore.cs', 'direct'),
      edge('src/MemoryStore.cs', 'src/Caller.cs', 'conceptual'),
      edge('src/DiskStore.cs', 'src/Caller.cs', 'conceptual'),
      edge('src/DiskStore.cs', 'src/Notes.md', 'conceptual', true),
    ],
    changedFiles: ids.map((id) => file(id, 'modified', 3, 1, 'C#', 'DiffHacker.Core')),
    implementationGroups: [
      {
        abstractionNodeId: 'src/IStore.cs',
        implementationNodeIds: ['src/MemoryStore.cs', 'src/DiskStore.cs'],
        containerId: 'store',
      },
    ],
    ...overrides,
  });
}

/**
 * The same three files, grouped the other way.
 *
 * One dependency-flow cluster holding the whole path against three thematic ones — which is the
 * difference the two groupings exist to show, and the reason a switch is worth making: the same
 * `direct` edge sits inside a cluster in one and crosses two clusters in the other.
 *
 * A pair rather than one view with a flag, because the host projects a grouping and hands the
 * renderer the result: two views is what the renderer actually sees.
 */
export function groupedViews(): { dependencyFlow: AnalysisView; changeClusters: AnalysisView } {
  return {
    dependencyFlow: testView({
      readingOrder: ['src/Contract.cs', 'src/Caller.cs', 'src/Notes.md'],
    }),
    changeClusters: testView({
      grouping: 'change_clusters',
      readingOrder: ['src/Contract.cs', 'src/Caller.cs', 'src/Notes.md'],
      containers: [
        container('contracts', 1, 'src/Contract.cs', ['src/Contract.cs']),
        container('callers', 2, 'src/Caller.cs', ['src/Caller.cs']),
        container('documentation', 3, 'src/Notes.md', ['src/Notes.md']),
      ],
      nodes: [
        node('src/Contract.cs', 'contracts', 1, ['changed', 'entry_point']),
        node('src/Caller.cs', 'callers', 1, ['changed', 'entry_point']),
        node('src/Notes.md', 'documentation', 1, ['added', 'entry_point']),
      ],
      edges: [edge('src/Contract.cs', 'src/Caller.cs', 'direct', true)],
    }),
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

/**
 * A node with three predecessors and two successors, which is the shape Iteration 10's verification
 * step 5 names: "from a node with three predecessors and two successors, confirm the navigation offers
 * a labelled choice". One of the successors is in another cluster, so the label has something to say
 * beyond the file name.
 *
 * The reading order is complete and deliberately not the order the nodes are declared in, so a test
 * that walks it is testing the order rather than the array.
 */
export function fanInFanOutView(): AnalysisView {
  const ids = [
    'src/A.cs',
    'src/B.cs',
    'src/C.cs',
    'src/Hub.cs',
    'src/Down1.cs',
    'src/Down2.cs',
  ];

  return testView({
    readingOrder: ['src/A.cs', 'src/B.cs', 'src/C.cs', 'src/Hub.cs', 'src/Down1.cs', 'src/Down2.cs'],
    containers: [
      container('core', 1, 'src/A.cs', ['src/A.cs', 'src/B.cs', 'src/C.cs', 'src/Hub.cs', 'src/Down1.cs']),
      container('docs', 2, 'src/Down2.cs', ['src/Down2.cs']),
    ],
    nodes: [
      node('src/A.cs', 'core', 1, ['changed', 'entry_point']),
      node('src/B.cs', 'core', 2, ['changed']),
      node('src/C.cs', 'core', 3, ['changed']),
      node('src/Hub.cs', 'core', 4, ['changed']),
      node('src/Down1.cs', 'core', 5, ['changed']),
      node('src/Down2.cs', 'docs', 1, ['added', 'entry_point']),
    ],
    edges: [
      edge('src/A.cs', 'src/Hub.cs', 'direct'),
      edge('src/B.cs', 'src/Hub.cs', 'direct'),
      edge('src/C.cs', 'src/Hub.cs', 'conceptual'),
      edge('src/Hub.cs', 'src/Down1.cs', 'direct'),
      edge('src/Hub.cs', 'src/Down2.cs', 'conceptual', true),
    ],
    changedFiles: ids.map((id) => file(id, 'modified', 3, 1, 'C#', 'DiffHacker.Core')),
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
