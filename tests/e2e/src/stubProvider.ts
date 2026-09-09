import { createServer, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

/**
 * A scripted OpenAI-compatible endpoint, so a profile run can be driven end to end without
 * reaching a real provider.
 *
 * No test in this repository spends money or depends on a network, and a model's answer is not
 * what this suite is checking anyway — what it checks is that a run travels the whole path: the
 * screen, the RPC method, the orchestrator, the toolbox, the notification channel, SQLite, and
 * back into the window. A stub is the only way to make that path deterministic.
 *
 * It speaks just enough of the API for `ChatClientFactory`'s OpenAI-compatible path: `/models`
 * for the connection test, and `/chat/completions` for the run. The conversation is scripted as a
 * queue of turns; the last turn repeats if the model is asked again, so a test that produces one
 * more request than expected fails on its assertions rather than on a hang.
 *
 * **It answers `stream: true` with server-sent events**, because `LlmSession` streams every
 * request — deliberately, so a long answer keeps bytes moving and an idle-timeout does not kill it.
 * A stub that only knew how to send one JSON body looked like a provider returning an empty
 * message: the run failed schema validation with "the response was empty" and no layer said why.
 */
export class StubProvider {
  private readonly server: Server;
  private readonly turns: Turn[] = [];

  /** Every chat request the application made, in order. */
  readonly requests: ChatRequest[] = [];

  private constructor(server: Server) {
    this.server = server;
  }

  static async start(): Promise<StubProvider> {
    let provider!: StubProvider;

    const server = createServer((request, response) => {
      const chunks: Buffer[] = [];

      request.on('data', (chunk: Buffer) => chunks.push(chunk));
      request.on('end', () => {
        const body = Buffer.concat(chunks).toString('utf8');
        provider.handle(request.url ?? '', body, response);
      });
    });

    provider = new StubProvider(server);

    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    return provider;
  }

  /** The base URL to type into the provider form. */
  get baseUrl(): string {
    const { port } = this.server.address() as AddressInfo;
    return `http://127.0.0.1:${port}/v1`;
  }

  /**
   * Clears the script. Each run scripts its own turns: the last turn deliberately repeats so that
   * an unexpected extra request fails on an assertion rather than on a hang, which means a
   * finished run leaves one turn behind for the next one to trip over.
   */
  reset(): this {
    this.turns.length = 0;
    return this;
  }

  /** Queues a turn in which the model calls tools. */
  callsTools(...calls: { name: string; arguments?: Record<string, unknown> }[]): this {
    this.turns.push({ kind: 'tools', calls });
    return this;
  }

  /** Queues the final turn: the structured profile the model answers with. */
  answers(document: unknown): this {
    this.turns.push({ kind: 'answer', content: JSON.stringify(document) });
    return this;
  }

  /** Queues a turn that never responds, so a test can cancel a run that is genuinely in flight. */
  hangs(): this {
    this.turns.push({ kind: 'hang' });
    return this;
  }

  async stop(): Promise<void> {
    await new Promise<void>((resolve) => this.server.close(() => resolve()));
  }

  private handle(url: string, body: string, response: ServerResponse): void {
    if (url.includes('/models')) {
      return send(response, 200, {
        object: 'list',
        data: [{ id: 'stub-model', object: 'model' }],
      });
    }

    if (!url.includes('/chat/completions')) {
      return send(response, 404, { error: { message: `no route for ${url}` } });
    }

    const request = JSON.parse(body) as ChatRequest;
    this.requests.push(request);

    const turn = this.turns.length > 1 ? this.turns.shift()! : this.turns[0];

    if (turn === undefined || turn.kind === 'hang') {
      // Deliberately no response: the socket stays open until the test cancels or the app times
      // out, which is what makes a mid-flight cancellation test honest.
      return;
    }

    const choice =
      turn.kind === 'tools'
        ? {
            finish_reason: 'tool_calls',
            message: {
              role: 'assistant',
              content: null,
              tool_calls: turn.calls.map((call, index) => ({
                id: `call_${index}`,
                type: 'function',
                function: { name: call.name, arguments: JSON.stringify(call.arguments ?? {}) },
              })),
            },
          }
        : {
            finish_reason: 'stop',
            message: { role: 'assistant', content: turn.content },
          };

    return request.stream === true
      ? sendStream(response, choice)
      : send(response, 200, completion(choice));
  }
}

type Turn =
  | { kind: 'tools'; calls: { name: string; arguments?: Record<string, unknown> }[] }
  | { kind: 'answer'; content: string }
  | { kind: 'hang' };

interface ChatRequest {
  model: string;
  messages: { role: string; content?: unknown }[];
  tools?: { function: { name: string } }[];
  response_format?: { type: string };
  stream?: boolean;
  stream_options?: { include_usage?: boolean };
}

function completion(choice: Record<string, unknown>): Record<string, unknown> {
  return {
    id: 'chatcmpl-stub',
    object: 'chat.completion',
    created: Math.floor(Date.now() / 1000),
    model: 'stub-model',
    choices: [{ index: 0, logprobs: null, ...choice }],
    usage: { prompt_tokens: 1200, completion_tokens: 240, total_tokens: 1440 },
  };
}

/**
 * The same answer as server-sent events.
 *
 * Deliberately more than one chunk: a single frame carrying the whole message would let a bug that
 * only reads the first delta pass. The shape follows the OpenAI streaming API — a role-only first
 * delta, content or tool-call deltas after it, a finish_reason with empty choices for usage when it
 * was asked for, and `[DONE]`.
 */
function sendStream(response: ServerResponse, choice: Record<string, unknown>): void {
  response.writeHead(200, {
    'content-type': 'text/event-stream',
    'cache-control': 'no-cache',
    connection: 'keep-alive',
  });

  const frame = (payload: Record<string, unknown>): void => {
    response.write(`data: ${JSON.stringify(payload)}\n\n`);
  };

  const chunk = (delta: Record<string, unknown>, finish: string | null = null) =>
    frame({
      id: 'chatcmpl-stub',
      object: 'chat.completion.chunk',
      created: Math.floor(Date.now() / 1000),
      model: 'stub-model',
      choices: [{ index: 0, delta, logprobs: null, finish_reason: finish }],
    });

  const message = choice.message as {
    content?: string | null;
    tool_calls?: Record<string, unknown>[];
  };

  chunk({ role: 'assistant' });

  if (message.tool_calls) {
    message.tool_calls.forEach((call, index) => {
      const fn = call.function as { name: string; arguments: string };
      chunk({
        tool_calls: [
          {
            index,
            id: call.id,
            type: 'function',
            function: { name: fn.name, arguments: fn.arguments },
          },
        ],
      });
    });
  } else if (typeof message.content === 'string') {
    // Split down the middle, so the aggregation on the other side has two pieces to join.
    const half = Math.ceil(message.content.length / 2);
    chunk({ content: message.content.slice(0, half) });
    chunk({ content: message.content.slice(half) });
  }

  chunk({}, choice.finish_reason as string);

  frame({
    id: 'chatcmpl-stub',
    object: 'chat.completion.chunk',
    created: Math.floor(Date.now() / 1000),
    model: 'stub-model',
    choices: [],
    usage: { prompt_tokens: 1200, completion_tokens: 240, total_tokens: 1440 },
  });

  response.write('data: [DONE]\n\n');
  response.end();
}

function send(response: ServerResponse, status: number, payload: unknown): void {
  const body = JSON.stringify(payload);

  response.writeHead(status, {
    'content-type': 'application/json',
    'content-length': Buffer.byteLength(body),
  });

  response.end(body);
}

/** A profile document that satisfies the schema, with recognisable values to assert on. */
export const stubProfileDocument = {
  purpose: 'A fixture repository used by the DiffHacker end-to-end suite.',
  architecture: 'One source directory and a readme. Nothing else to speak of.',
  modules: [
    {
      name: 'src',
      path: 'src',
      summary: 'Everything the fixture contains.',
      relatedModules: [],
    },
  ],
  layering: 'There is one layer.',
  patterns: '- Files are plain text.',
  entryPoints: [{ path: 'src/main.ts', purpose: 'The only file that runs.' }],
  testLayout: 'The fixture has no tests.',
  documentationSources: ['readme.md'],
};

/**
 * An analysis document that satisfies `analysis-result.schema.json` and every rule the validator
 * enforces, built for whichever files the fixture actually changed.
 *
 * Generated rather than written out, because the suite runs it over a three-file change and over a
 * five-hundred-file one, and the completeness invariant (§0.2.5) means the answer has to name every
 * path either way. One cluster with the first file as its entry node is the smallest shape that
 * satisfies "exactly one entry node, ranks 1..n, every file covered".
 */
export function stubAnalysisResult(paths: readonly string[]) {
  const nodes = paths.map((path, index) => ({
    id: path,
    filePath: path,
    symbol: '',
    startLine: 0,
    endLine: 0,
    title: `What ${path} does in this change`,
    whatChanged: `A line was added to ${path}.`,
    whyItChanged: 'The fixture needed something to review.',
    howItAffectsOthers: index === 0 ? 'Everything downstream reads from here.' : '',
    implementationNotes: '',
    risks: index === 0 ? ['The entry point changed shape.'] : [],
    importance: index === 0 ? 5 : 2,
    rank: index + 1,
    states: index === 0 ? ['changed', 'entry_point'] : ['changed'],
  }));

  return {
    summary: 'Every file in the fixture gained a line, which is the whole of the change.',
    overallRisks: ['The fixture has no tests, so nothing proves the change works.'],
    readingOrder: paths.map((path) => path),
    containers: [
      {
        id: 'the-whole-change',
        title: 'The whole change',
        summary: 'One cluster, because every file changed for the same reason.',
        explanation: 'The fixture was edited in one pass, so there is nothing to separate.',
        risks: [],
        displayOrder: 1,
        entryNodeId: paths[0],
        nodeIds: paths.map((path) => path),
      },
    ],
    nodes,
    edges: paths.slice(1).map((path) => ({
      sourceNodeId: paths[0],
      targetNodeId: path,
      kind: 'conceptual',
      explanation: 'Read the first file before the ones that follow it.',
      risks: [],
    })),
  };
}

/**
 * The same document with one file left out, so a run has something specific to be sent back for.
 * Requirement 4 is that the failure fed to the model is specific, and this is what makes it so.
 */
export function stubAnalysisMissing(paths: readonly string[], omit: string) {
  const kept = paths.filter((path) => path !== omit);
  return stubAnalysisResult(kept);
}

/**
 * The same files split across two clusters, with one edge crossing between them.
 *
 * Iteration 8's diagram only has anything to say once there is more than one cluster: collapsing,
 * cross-container edges and bundling are all properties of a graph with a boundary in it, and the
 * single-cluster answer above has none.
 *
 * The split is by index rather than by anything meaningful, because the fixture's files are not
 * meaningfully different — what matters is that the shape reaches the renderer, not that a stub
 * clusters well.
 */
export function stubTwoClusterResult(paths: readonly string[]) {
  const half = Math.max(1, Math.ceil(paths.length / 2));
  const first = paths.slice(0, half);
  const second = paths.slice(half);

  const base = stubAnalysisResult(paths);

  const cluster = (id: string, title: string, order: number, members: readonly string[]) => ({
    id,
    title,
    summary: `${members.length} file(s) that changed together.`,
    explanation: `Everything in ${title} was edited for one reason.`,
    risks: [],
    displayOrder: order,
    entryNodeId: members[0],
    nodeIds: [...members],
  });

  return {
    ...base,
    containers:
      second.length === 0
        ? [cluster('first-half', 'The first half', 1, first)]
        : [
            cluster('first-half', 'The first half', 1, first),
            cluster('second-half', 'The second half', 2, second),
          ],
    // Ranks restart inside each cluster, which is what the schema requires and what the boxes
    // print.
    nodes: base.nodes.map((node) => ({
      ...node,
      rank: (first.includes(node.filePath) ? first : second).indexOf(node.filePath) + 1,
      states: node.filePath === first[0] || node.filePath === second[0]
        ? ['changed', 'entry_point']
        : ['changed'],
    })),
    edges: [
      // One inside the first cluster, one crossing between them: the two kinds of line the
      // diagram has to draw differently.
      ...(first.length > 1
        ? [
            {
              sourceNodeId: first[0],
              targetNodeId: first[1],
              kind: 'direct',
              explanation: 'The second file reads from the first.',
              risks: [],
            },
          ]
        : []),
      ...(second.length > 0
        ? [
            {
              sourceNodeId: first[0],
              targetNodeId: second[0],
              kind: 'conceptual',
              explanation: 'The second cluster only makes sense after the first.',
              risks: [],
            },
          ]
        : []),
    ],
  };
}
