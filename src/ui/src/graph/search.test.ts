import { describe, expect, it } from 'vitest';
import { MAX_HITS, searchGraph } from './search';
import { container, node, testView, twoContainerView } from './testGraph';

describe('searchGraph', () => {
  it('finds nothing for an empty query rather than everything', () => {
    expect(searchGraph(testView(), '')).toEqual([]);
    expect(searchGraph(testView(), '   ')).toEqual([]);
  });

  it('finds a file by part of its name, whatever the case', () => {
    // Requirements 10 and 13, which are both about this.
    const hits = searchGraph(testView(), 'contract');

    expect(hits[0]?.nodeId).toBe('src/Contract.cs');
    expect(hits[0]?.field).toBe('path');
    expect(hits[0]?.label).toBe('Contract.cs');
  });

  it('leads with file matches, so a search for a filename behaves like one', () => {
    const view = testView({
      nodes: [
        node('src/Other.cs', 'core', 1, ['changed', 'entry_point']),
        node('src/Caller.cs', 'core', 2, ['changed']),
      ],
      containers: [container('core', 1, 'src/Other.cs', ['src/Other.cs', 'src/Caller.cs'])],
    });

    // "Other.cs" is a path; every node's title is "Title for …" and matches nothing here.
    expect(searchGraph(view, 'other')[0]?.field).toBe('path');
  });

  it('also matches node titles, because the box on screen shows one', () => {
    const hits = searchGraph(testView(), 'title for src/notes');

    expect(hits).toHaveLength(1);
    expect(hits[0]?.field).toBe('nodeTitle');
    expect(hits[0]?.nodeId).toBe('src/Notes.md');
  });

  it('matches a cluster title and points at where to start reading in it', () => {
    // Not at the container: "go here" is more useful than "go somewhere in here", and the entry
    // node is the model's own answer to where.
    const hits = searchGraph(twoContainerView(), 'cluster docs');

    expect(hits).toHaveLength(1);
    expect(hits[0]?.field).toBe('containerTitle');
    expect(hits[0]?.nodeId).toBe('src/Notes.md');
    expect(hits[0]?.containerId).toBe('docs');
  });

  it('does not list the same node twice for the same reason', () => {
    const hits = searchGraph(testView(), 'src/');
    const keys = hits.map((hit) => `${hit.field}:${hit.nodeId}`);

    expect(new Set(keys).size).toBe(keys.length);
  });

  it('stops before the list becomes a wall', () => {
    const paths = Array.from({ length: MAX_HITS + 10 }, (_, index) => `src/file${index}.cs`);
    const view = testView({
      containers: [container('core', 1, paths[0]!, [...paths])],
      nodes: paths.map((path, index) => node(path, 'core', index + 1, ['changed'])),
      edges: [],
      changedFiles: [],
    });

    expect(searchGraph(view, 'src/file')).toHaveLength(MAX_HITS);
  });
});
