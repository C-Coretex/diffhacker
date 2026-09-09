import ELK from 'elkjs/lib/elk.bundled.js';
import type { WorkerRequest, WorkerResponse } from './runLayout';

/**
 * The layout worker. Transport only — every decision about *what* to lay out was made in
 * `elkGraph.ts`, on the main thread, where it can be tested without one.
 *
 * One ELK instance for the life of the worker. Building one is expensive and collapsing a
 * container has to feel instant.
 *
 * `worker-src 'self' blob:` is already in the page's CSP for exactly this.
 */
const elk = new ELK();

self.onmessage = async (event: MessageEvent<WorkerRequest>) => {
  const { id, graph } = event.data;

  try {
    const laidOut = (await elk.layout(graph as never)) as unknown as WorkerResponse['graph'];
    (self as unknown as Worker).postMessage({ id, graph: laidOut } satisfies WorkerResponse);
  } catch (error) {
    // A layout failure is a bad graph or a bad option, and the caller turns it into a message on
    // the screen. Crashing the worker would take every later relayout down with it.
    (self as unknown as Worker).postMessage({
      id,
      error: error instanceof Error ? error.message : String(error),
    } satisfies WorkerResponse);
  }
};
