import type { ElkNode } from './elkGraph';

/**
 * Runs a layout. One seam, two implementations.
 *
 * The application uses {@link workerLayout}, because ELK on three hundred nodes takes long enough
 * to drop frames and the iteration's fixed decisions put it in a Web Worker. The snapshot test
 * uses {@link inProcessLayout}, because a worker in jsdom is a fight with the test environment
 * rather than a test of the layout — and requirement 12 is about the coordinates, which are the
 * same either way.
 */
export type LayoutRunner = (graph: ElkNode) => Promise<ElkNode>;

/**
 * Layout on the main thread. Used by tests, and as the fallback when a worker cannot be created —
 * a locked-down WebView is a worse place to discover that the diagram simply does not appear.
 */
export async function inProcessLayout(graph: ElkNode): Promise<ElkNode> {
  const { default: ELK } = await import('elkjs/lib/elk.bundled.js');
  const elk = new ELK();
  return (await elk.layout(graph as never)) as unknown as ElkNode;
}

/**
 * Layout in a Web Worker.
 *
 * One worker per runner, kept alive across relayouts: ELK's constructor is not cheap, and
 * collapsing a container is meant to feel immediate. Requests are tagged so that a layout started
 * before a fast second collapse cannot resolve after it and paint the older arrangement.
 */
export function createWorkerLayout(): { run: LayoutRunner; dispose: () => void } {
  let worker: Worker | null = null;
  let nextId = 0;
  let failed = false;
  const pending = new Map<
    number,
    { graph: ElkNode; resolve: (graph: ElkNode) => void; reject: (error: Error) => void }
  >();

  const ensure = (): Worker => {
    if (worker) return worker;

    // A classic worker, matching `worker.format: 'iife'` in vite.config.ts. WebView2 will not
    // start a module worker served from the custom scheme.
    worker = new Worker(new URL('./layout.worker.ts', import.meta.url));

    worker.onmessage = (event: MessageEvent<WorkerResponse>) => {
      const waiting = pending.get(event.data.id);
      if (!waiting) return;
      pending.delete(event.data.id);

      if (event.data.error !== undefined) {
        waiting.reject(new Error(event.data.error));
      } else {
        waiting.resolve(event.data.graph!);
      }
    };

    worker.onerror = (event) => {
      // The worker died — an old WebView, a policy that forbids one, a scheme it will not load a
      // script from. Whatever the reason, a diagram that arrives a moment late is worth far more
      // than a diagram that does not arrive, so everything waiting is retried on this thread and
      // the worker is not tried again.
      //
      // Reported to the console rather than swallowed: this is a real degradation, and the next
      // person to wonder why a large graph feels slow should be able to find out here.
      console.warn('The layout worker stopped; laying the diagram out on the main thread.', event);

      const waiting = [...pending.values()];
      pending.clear();
      worker?.terminate();
      worker = null;
      failed = true;

      for (const { graph, resolve, reject } of waiting) {
        inProcessLayout(graph).then(resolve, reject);
      }
    };

    return worker;
  };

  const run: LayoutRunner = (graph) =>
    new Promise((resolve, reject) => {
      // Once a worker has failed there is no sense building another one to fail the same way.
      if (failed) {
        inProcessLayout(graph).then(resolve, reject);
        return;
      }

      let instance: Worker;
      try {
        instance = ensure();
      } catch {
        // No worker available at all — an old WebView, or a policy that forbids one. Lay out on
        // the main thread instead: a brief stall beats a blank diagram.
        failed = true;
        inProcessLayout(graph).then(resolve, reject);
        return;
      }

      const id = ++nextId;
      pending.set(id, { graph, resolve, reject });
      instance.postMessage({ id, graph } satisfies WorkerRequest);
    });

  return {
    run,
    dispose: () => {
      worker?.terminate();
      worker = null;
      pending.clear();
    },
  };
}

export interface WorkerRequest {
  readonly id: number;
  readonly graph: ElkNode;
}

export interface WorkerResponse {
  readonly id: number;
  readonly graph?: ElkNode;
  readonly error?: string;
}
