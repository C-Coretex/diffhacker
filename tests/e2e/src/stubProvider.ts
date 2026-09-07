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

    this.requests.push(JSON.parse(body) as ChatRequest);

    const turn = this.turns.length > 1 ? this.turns.shift()! : this.turns[0];

    if (turn === undefined || turn.kind === 'hang') {
      // Deliberately no response: the socket stays open until the test cancels or the app times
      // out, which is what makes a mid-flight cancellation test honest.
      return;
    }

    if (turn.kind === 'tools') {
      return send(response, 200, completion({
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
      }));
    }

    return send(response, 200, completion({
      finish_reason: 'stop',
      message: { role: 'assistant', content: turn.content },
    }));
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
