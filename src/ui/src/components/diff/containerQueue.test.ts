import { describe, expect, it } from 'vitest';
import { fanInFanOutView, testView } from '@/graph/testGraph';
import { containerQueue } from './containerQueue';

describe('containerQueue', () => {
  it('puts a cluster’s files in the analysis’s reading order, not the order it declared them', () => {
    const view = fanInFanOutView();

    // The container lists these in its own order; the reading order is what a reviewer was told to
    // follow, and opening a cluster whole has to mean following it.
    const scrambled = {
      ...view,
      containers: view.containers.map((container) =>
        container.id === 'core'
          ? {
              ...container,
              nodeIds: ['src/Hub.cs', 'src/Down1.cs', 'src/A.cs', 'src/C.cs', 'src/B.cs'],
            }
          : container,
      ),
    };

    expect(containerQueue(scrambled, 'core').map((node) => node.id)).toEqual([
      'src/A.cs',
      'src/B.cs',
      'src/C.cs',
      'src/Hub.cs',
      'src/Down1.cs',
    ]);
  });

  it('still lists a file the reading order never mentioned, after the ones it did', () => {
    // §0.2.5: every changed file appears. A list of a cluster's files that quietly dropped one the
    // model forgot to rank would be the one place in this product where a file can go missing.
    const view = fanInFanOutView();
    const partial = { ...view, readingOrder: ['src/Hub.cs', 'src/A.cs'] };

    expect(containerQueue(partial, 'core').map((node) => node.id)).toEqual([
      'src/Hub.cs',
      'src/A.cs',
      'src/B.cs',
      'src/C.cs',
      'src/Down1.cs',
    ]);
  });

  it('is empty for a cluster that is not there, rather than throwing', () => {
    expect(containerQueue(testView(), 'no-such-cluster')).toEqual([]);
    expect(containerQueue(testView(), undefined)).toEqual([]);
  });
});
