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
    states: index === 0 ? ['changed'] : ['changed'],
  }));

  return {
    summary: 'Every file in the fixture gained a line, which is the whole of the change.',
    overallRisks: ['The fixture has no tests, so nothing proves the change works.'],
    dependencyReadingOrder: paths.map((path) => path),
    dependencyContainers: [
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
    ...byDirectory(paths),
    nodes,
    edges: paths.slice(1).map((path) => ({
      sourceNodeId: paths[0],
      targetNodeId: path,
      kind: 'conceptual',
      explanation: 'Read the first file before the ones that follow it.',
      risks: [],
    })),
    // Asked for by default, and a fixture edit has no abstraction in it: the answer most runs give.
    // A spec that turns the request off sets this to undefined, as the change-clusters one does.
    implementationGroups: [] as { abstractionNodeId: string; implementationNodeIds: string[] }[],
  };
}

/**
 * An abstraction changed beside two implementations, and a caller between them in the reading order
 * — the fixture the "merge implementations" toggle exists to draw.
 *
 * Takes the four paths in that order: the abstraction, the caller, then the two implementations. Each
 * implementation leads on to the caller it serves, so merging folds two of the model's edges onto one
 * line and leaves the two from the abstraction inside the box.
 */
export function stubImplementationResult(paths: readonly string[]) {
  if (paths.length !== 4) {
    throw new Error(`stubImplementationResult needs exactly four paths, got ${paths.length}.`);
  }

  const [abstraction, caller, first, second] = paths as unknown as [string, string, string, string];
  const base = stubAnalysisResult(paths);

  return {
    ...base,
    edges: [
      { sourceNodeId: abstraction, targetNodeId: first, kind: 'direct', explanation: `${first} implements ${abstraction}.`, risks: [] },
      { sourceNodeId: abstraction, targetNodeId: second, kind: 'direct', explanation: `${second} implements ${abstraction}.`, risks: [] },
      { sourceNodeId: first, targetNodeId: caller, kind: 'conceptual', explanation: `${caller} is served by ${first} in memory.`, risks: [] },
      { sourceNodeId: second, targetNodeId: caller, kind: 'conceptual', explanation: `${caller} is served by ${second} on disk.`, risks: [] },
    ],
    implementationGroups: [{ abstractionNodeId: abstraction, implementationNodeIds: [first, second] }],
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
 * The change-clusters grouping, for any set of paths: one cluster per top-level directory.
 *
 * Every builder here needs a second grouping now, and grouping by directory is the one rule that
 * works for three files and for five hundred without a fixture having to describe itself. It is also
 * honestly thematic — `db/`, `auth/`, `api/` really are separate concerns in the layered fixture —
 * which is the property the specs assert against.
 *
 * Sorted by name so two runs over one changeset produce the same clusters in the same order, and so
 * a spec can name one.
 */
function byDirectory(paths: readonly string[]) {
  const groups = new Map<string, string[]>();

  for (const path of paths) {
    const head = path.split('/')[0] ?? path;
    const theme = head === path ? 'root' : head;
    const members = groups.get(theme);

    if (members === undefined) {
      groups.set(theme, [path]);
    } else {
      members.push(path);
    }
  }

  const themes = [...groups.entries()].sort(([left], [right]) => left.localeCompare(right));

  return {
    clusterReadingOrder: themes.flatMap(([, members]) => members),
    clusterContainers: themes.map(([theme, members], index) => ({
      id: theme.replace(/[^a-z0-9]+/gi, '-').toLowerCase(),
      title: `Everything under ${theme}`,
      summary: `${members.length} file(s) that belong to one concern.`,
      explanation: `Grouped by area rather than by the path a reader would walk through them.`,
      risks: [],
      displayOrder: index + 1,
      entryNodeId: members[0],
      nodeIds: [...members],
    })),
  };
}

/**
 * One node set grouped two genuinely different ways — the fixture Iteration 11 exists to draw.
 *
 * Dependency flow puts the **whole** database-to-auth-to-API chain in one cluster, because that is
 * one change and cutting it at a concern boundary would hide the thing a reviewer needs to see.
 * Change clusters splits the same nodes by area, which breaks that chain across three clusters and
 * turns its edges into crossings. Verification steps 2 and 3 are those two sentences.
 */
export function stubGroupedResult(paths: readonly string[]) {
  const base = stubAnalysisResult(paths);

  return {
    ...base,
    summary: 'A tenant column was added, the token started carrying it, and the API started returning it.',
    dependencyContainers: [
      {
        id: 'tenant-end-to-end',
        title: 'Tenancy, end to end',
        summary: 'One change from the migration through the token to the endpoint.',
        explanation:
          'Kept whole on purpose: the path crosses database, auth and API, and following it is the only way to see what the change does.',
        risks: ['The migration and the API have to ship together.'],
        displayOrder: 1,
        entryNodeId: paths[0],
        nodeIds: paths.map((path) => path),
      },
    ],
    // Consecutive links, so the chain is a path rather than a fan — which is what makes the
    // difference between the two groupings visible as broken lines rather than as fewer of them.
    edges: paths.slice(1).map((path, index) => ({
      sourceNodeId: paths[index],
      targetNodeId: path,
      kind: index === 0 ? 'direct' : 'conceptual',
      explanation: `${path} only makes sense once ${paths[index]} is understood.`,
      risks: [],
    })),
  };
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

  // A risk at every level the schema allows: the change as a whole (in `stubAnalysisResult`), a
  // cluster, a file (likewise) and a link. Iteration 9's register has to find all four, and a
  // fixture that only ever wrote two of them would let it pass having found two.
  const cluster = (id: string, title: string, order: number, members: readonly string[]) => ({
    id,
    title,
    summary: `${members.length} file(s) that changed together.`,
    explanation: `Everything in ${title} was edited for one reason.`,
    risks: [`Everything in ${title} has to ship together.`],
    displayOrder: order,
    entryNodeId: members[0],
    nodeIds: [...members],
  });

  return {
    ...base,
    dependencyContainers:
      second.length === 0
        ? [cluster('first-half', 'The first half', 1, first)]
        : [
            cluster('first-half', 'The first half', 1, first),
            cluster('second-half', 'The second half', 2, second),
          ],
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
              risks: ['The second file still assumes the old shape.'],
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

/**
 * A result shaped for Iteration 10's navigation: one node with three predecessors and two
 * successors, and one node that names a place inside a file rather than the whole of it.
 *
 * Verification step 5 asks for exactly that fan — "from a node with three predecessors and two
 * successors, confirm the navigation offers a labelled choice" — and no other fixture here produces
 * one. The last path goes in a cluster of its own so one of the choices has a cluster to name, which
 * is the part of a label that says something a file name does not.
 *
 * Takes six paths. `hubIndex` is the third, so the first three point at it and the last two follow.
 */
export function stubReviewResult(
  paths: readonly string[],
  region?: { path: string; startLine: number; endLine: number },
) {
  if (paths.length !== 6) {
    throw new Error(`stubReviewResult needs exactly six paths, got ${paths.length}.`);
  }

  const [first, second, third, hub, fourth, aside] = paths as unknown as [
    string, string, string, string, string, string,
  ];

  const main = [first, second, third, hub, fourth];
  const base = stubAnalysisResult(paths);

  return {
    ...base,
    dependencyReadingOrder: [first, second, third, hub, fourth, aside],
    dependencyContainers: [
      {
        id: 'the-change',
        title: 'The change itself',
        summary: 'Everything that had to move together.',
        explanation: 'One cluster, held together by the reason the change was made.',
        risks: ['The whole cluster has to ship at once.'],
        displayOrder: 1,
        entryNodeId: first,
        nodeIds: main,
      },
      {
        id: 'the-aside',
        title: 'The aside',
        summary: 'One file that follows from the rest without being part of it.',
        explanation: 'Kept apart so a reader knows it is a consequence, not a cause.',
        risks: [],
        displayOrder: 2,
        entryNodeId: aside,
        nodeIds: [aside],
      },
    ],
    nodes: base.nodes.map((node) => ({
      ...node,
      startLine: region?.path === node.filePath ? region.startLine : 0,
      endLine: region?.path === node.filePath ? region.endLine : 0,
      symbol: region?.path === node.filePath ? 'tenantAware' : '',
    })),
    edges: [
      { sourceNodeId: first, targetNodeId: hub, kind: 'direct', explanation: `${hub} reads the contract ${first} defines.`, risks: [] },
      { sourceNodeId: second, targetNodeId: hub, kind: 'direct', explanation: `${hub} was renamed out from under ${second}.`, risks: [] },
      { sourceNodeId: third, targetNodeId: hub, kind: 'conceptual', explanation: `${third} only makes sense once ${hub} is understood.`, risks: [] },
      { sourceNodeId: hub, targetNodeId: fourth, kind: 'direct', explanation: `${fourth} is deleted because ${hub} absorbed it.`, risks: ['Nothing else was checked for references to it.'] },
      { sourceNodeId: hub, targetNodeId: aside, kind: 'conceptual', explanation: `${aside} is the consequence of ${hub} changing shape.`, risks: [] },
    ],
  };
}
