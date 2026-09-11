import { describe, expect, it } from 'vitest';
import { applyProjectFilter, hiddenFileCount } from './projectFilter';
import { testView, twoContainerView } from './testGraph';

describe('applyProjectFilter', () => {
  it('returns the same view when nothing is hidden', () => {
    const view = testView();
    expect(applyProjectFilter(view, new Set())).toBe(view);
  });

  it('returns the same view when the hidden set matches no project in the change', () => {
    const view = testView();
    expect(applyProjectFilter(view, new Set(['NoSuchProject']))).toBe(view);
  });

  it('drops nodes whose project is hidden, and the edges that touch them', () => {
    const filtered = applyProjectFilter(testView(), new Set(['docs']));

    expect(filtered.nodes.map((node) => node.id)).toEqual(['src/Contract.cs', 'src/Caller.cs']);
    // The direct edge between the two survivors is untouched.
    expect(filtered.edges).toHaveLength(1);
  });

  it('keeps a file with no project name, even while a filter is active', () => {
    const view = testView({
      changedFiles: testView().changedFiles.map((file) =>
        file.path === 'src/Notes.md' ? { ...file, project: '' } : file,
      ),
    });

    const filtered = applyProjectFilter(view, new Set(['DiffHacker.Core']));

    expect(filtered.nodes.map((node) => node.id)).toEqual(['src/Notes.md']);
  });

  it('reassigns the entry node when the hidden projects take it out', () => {
    // Hiding DiffHacker.Core removes both Contract.cs (the entry) and Caller.cs, leaving only
    // Notes.md — which must become the container's entry rather than pointing at a node that is
    // no longer a member.
    const filtered = applyProjectFilter(testView(), new Set(['DiffHacker.Core']));
    const container = filtered.containers.find((candidate) => candidate.id === 'core');

    expect(container?.nodeIds).toEqual(['src/Notes.md']);
    expect(container?.entryNodeId).toBe('src/Notes.md');
  });

  it('drops a container entirely once every one of its members is hidden', () => {
    const filtered = applyProjectFilter(twoContainerView(), new Set(['docs']));

    expect(filtered.containers.map((container) => container.id)).toEqual(['core']);
  });

  it('drops an edge with either end hidden, cross-container edges included', () => {
    const filtered = applyProjectFilter(twoContainerView(), new Set(['docs']));

    // twoContainerView has one direct edge inside `core` and two conceptual edges into `docs`;
    // only the one wholly inside the surviving container should remain.
    expect(filtered.edges).toHaveLength(1);
    expect(filtered.edges[0]?.targetNodeId).toBe('src/Caller.cs');
  });
});

describe('hiddenFileCount', () => {
  it('is zero when nothing is hidden', () => {
    expect(hiddenFileCount(testView(), new Set())).toBe(0);
  });

  it('counts only the nodes a named, hidden project actually owns', () => {
    expect(hiddenFileCount(testView(), new Set(['docs']))).toBe(1);
    expect(hiddenFileCount(testView(), new Set(['DiffHacker.Core']))).toBe(2);
  });

  it('never counts a file with no project name', () => {
    const view = testView({
      changedFiles: testView().changedFiles.map((file) =>
        file.path === 'src/Notes.md' ? { ...file, project: '' } : file,
      ),
    });

    expect(hiddenFileCount(view, new Set(['docs']))).toBe(0);
  });
});
