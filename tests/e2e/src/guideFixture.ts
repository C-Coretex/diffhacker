import { GitRepo, type RepoSet } from './gitFixture.ts';

/**
 * The change the user guide's screenshots show: a small TypeScript shop getting per-tenant rate
 * limiting, plus one unrelated fix in a file it touches.
 *
 * Every other fixture in this suite is built to make one assertion possible, and looks it. This one
 * is built to be *read* — by someone deciding whether DiffHacker is worth installing — so the files
 * are real code, the answer below is the kind of answer a good model gives, and the change has every
 * shape the guide talks about: three projects to colour, added, changed and deleted files, an
 * interface with two implementations, one file split into two unrelated nodes, direct and conceptual
 * links, links that cross clusters, and risks at every level.
 *
 * The prose stays inside the brief verbosity's budgets (`AnalysisFieldBudgets`), because a field the
 * validator flags as overlong is drawn shortened, and a guide should not show that.
 */

/** Where the fixture lives: a folder called `acme-shop`, so the application names it that. */
export const GUIDE_REPOSITORY_NAME = 'acme-shop';

/** Every file the change touches, as git spells them. */
export const GUIDE_CHANGED = [
  'CHANGELOG.md',
  'docs/rate-limits.md',
  'packages/api/src/errors.ts',
  'packages/api/src/legacy/throttle.ts',
  'packages/api/src/middleware/rateLimit.ts',
  'packages/api/src/routes/orders.ts',
  'packages/api/src/server.ts',
  'packages/api/test/rateLimit.test.ts',
  'packages/core/src/config.ts',
  'packages/core/src/rateLimit/FixedWindowLimiter.ts',
  'packages/core/src/rateLimit/RateLimiter.ts',
  'packages/core/src/rateLimit/TokenBucketLimiter.ts',
  'packages/web/src/Toast.tsx',
  'packages/web/src/apiClient.ts',
] as const;

/** The file the guide edits after the run, so the stale banner has something to report. */
export const GUIDE_EDITED_LATER = 'packages/web/src/Toast.tsx';

export function guideRepository(repos: RepoSet): GitRepo {
  const repo = repos.track(GitRepo.createNamed('guide', GUIDE_REPOSITORY_NAME));

  for (const [path, contents] of Object.entries(BASELINE)) repo.write(path, contents);
  repo.commitAll('Orders can be filtered by status');

  for (const [path, contents] of Object.entries(CHANGED)) repo.write(path, contents);
  repo.remove('packages/api/src/legacy/throttle.ts');

  return repo;
}

/** The later edit: same file, one more message, so the banner reports one file edited since. */
export function editAfterTheRun(repo: GitRepo): void {
  repo.write(
    GUIDE_EDITED_LATER,
    CHANGED[GUIDE_EDITED_LATER]!.replace(
      "  failed: 'Something went wrong. Try again.',\n",
      "  failed: 'Something went wrong. Try again.',\n  offline: 'You are offline.',\n",
    ),
  );
}

// ---------------------------------------------------------------------------------------------
// The repository

const lines = (...rows: string[]) => `${rows.join('\n')}\n`;

const BASELINE: Record<string, string> = {
  'package.json': lines('{', '  "name": "acme-shop",', '  "private": true,', '  "workspaces": ["packages/*"]', '}'),
  'packages/core/package.json': lines('{ "name": "@acme/core", "version": "2.3.0" }'),
  'packages/api/package.json': lines('{ "name": "@acme/api", "version": "2.3.0" }'),
  'packages/web/package.json': lines('{ "name": "@acme/web", "version": "2.3.0" }'),

  'CHANGELOG.md': lines('# Changelog', '', '## 2.3.0', '', '- Orders can be filtered by status.'),

  'packages/core/src/config.ts': lines(
    'export interface ShopConfig {',
    '  port: number;',
    '  healthCheckTimeoutMs: number;',
    '}',
    '',
    'export const config: ShopConfig = {',
    '  port: Number(process.env.PORT ?? 8080),',
    '  healthCheckTimeoutMs: 2000,',
    '};',
  ),

  'packages/api/src/server.ts': lines(
    "import express from 'express';",
    "import { config } from '@acme/core/config';",
    "import { throttle } from './legacy/throttle';",
    "import { ordersRouter } from './routes/orders';",
    "import { pingDatabase } from './db';",
    '',
    'export function createServer() {',
    '  const app = express();',
    '',
    '  app.use(express.json());',
    '  app.use(throttle({ requestsPerMinute: 600 }));',
    "  app.use('/orders', ordersRouter);",
    '',
    "  app.get('/health', async (_request, response) => {",
    '    const healthy = await checkDatabase(2000);',
    '    response.status(healthy ? 200 : 503).end();',
    '  });',
    '',
    '  app.listen(config.port);',
    '  return app;',
    '}',
    '',
    'async function checkDatabase(timeoutMs: number): Promise<boolean> {',
    '  const timeout = new Promise<boolean>((resolve) => setTimeout(() => resolve(false), timeoutMs));',
    '  return Promise.race([pingDatabase(), timeout]);',
    '}',
  ),

  'packages/api/src/routes/orders.ts': lines(
    "import { Router } from 'express';",
    "import { createOrder, listOrders } from '../orders';",
    '',
    'export const ordersRouter = Router();',
    '',
    "ordersRouter.get('/', async (request, response) => {",
    '  response.json(await listOrders(request.query));',
    '});',
    '',
    "ordersRouter.post('/', async (request, response) => {",
    '  response.status(201).json(await createOrder(request.body));',
    '});',
  ),

  'packages/api/src/errors.ts': lines(
    "import type { Response } from 'express';",
    '',
    'export class HttpError extends Error {',
    '  constructor(readonly status: number, message: string) {',
    '    super(message);',
    '  }',
    '}',
    '',
    'export class NotFoundError extends HttpError {',
    '  constructor(what: string) {',
    '    super(404, `${what} was not found`);',
    '  }',
    '}',
    '',
    'export function sendError(error: unknown, response: Response): void {',
    '  const status = error instanceof HttpError ? error.status : 500;',
    "  response.status(status).json({ error: status === 500 ? 'Internal error' : (error as Error).message });",
    '}',
  ),

  'packages/api/src/legacy/throttle.ts': lines(
    "import type { NextFunction, Request, Response } from 'express';",
    '',
    '/** One counter for the whole server: every tenant shares the same allowance. */',
    'let windowStart = Date.now();',
    'let count = 0;',
    '',
    'export function throttle(options: { requestsPerMinute: number }) {',
    '  return (_request: Request, response: Response, next: NextFunction) => {',
    '    if (Date.now() - windowStart > 60_000) {',
    '      windowStart = Date.now();',
    '      count = 0;',
    '    }',
    '',
    '    if (++count > options.requestsPerMinute) {',
    '      response.status(503).end();',
    '      return;',
    '    }',
    '',
    '    next();',
    '  };',
    '}',
  ),

  'packages/web/src/apiClient.ts': lines(
    'export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {',
    '  const response = await fetch(`/api${path}`, init);',
    '',
    '  if (!response.ok) {',
    '    throw new Error(`Request to ${path} failed with ${response.status}`);',
    '  }',
    '',
    '  return (await response.json()) as T;',
    '}',
  ),

  'packages/web/src/Toast.tsx': lines(
    'const messages = {',
    "  saved: 'Saved.',",
    "  failed: 'Something went wrong. Try again.',",
    '};',
    '',
    'export type ToastMessage = keyof typeof messages;',
    '',
    'export function showToast(message: ToastMessage): void {',
    "  window.dispatchEvent(new CustomEvent('toast', { detail: messages[message] }));",
    '}',
  ),
};

const CHANGED: Record<string, string> = {
  'CHANGELOG.md': lines(
    '# Changelog',
    '',
    '## Unreleased',
    '',
    '- Requests are rate-limited per tenant. See docs/rate-limits.md.',
    '- The health check waits up to five seconds for the database.',
    '',
    '## 2.3.0',
    '',
    '- Orders can be filtered by status.',
  ),

  'docs/rate-limits.md': lines(
    '# Rate limits',
    '',
    'Every tenant has its own allowance of requests per minute.',
    '',
    '| Tenant | Strategy | Per minute | Burst |',
    '|---|---|---|---|',
    '| default | token bucket | 600 | 50 |',
    '| tenant-enterprise | token bucket | 6000 | 500 |',
    '| tenant-batch | fixed window | 120 | — |',
    '',
    'Placing an order is limited separately, on top of the tenant’s own limit.',
    '',
    'When a limit is reached the API answers `429 Too Many Requests` with a',
    '`Retry-After` header giving the seconds to wait.',
  ),

  'packages/core/src/rateLimit/RateLimiter.ts': lines(
    '/** How many requests a key may make, and how they are counted. */',
    'export type LimitPolicy =',
    "  | { strategy: 'token-bucket'; perMinute: number; burst: number }",
    "  | { strategy: 'fixed-window'; perMinute: number };",
    '',
    'export interface RateLimiter {',
    '  /**',
    "   * Takes one request's worth of allowance for `key`.",
    '   * Returns 0 when the request may go ahead, or the seconds to wait before it could.',
    '   */',
    '  tryAcquire(key: string): number;',
    '}',
  ),

  'packages/core/src/rateLimit/TokenBucketLimiter.ts': lines(
    "import type { RateLimiter } from './RateLimiter';",
    '',
    'interface Bucket {',
    '  tokens: number;',
    '  updatedAt: number;',
    '}',
    '',
    'export class TokenBucketLimiter implements RateLimiter {',
    '  private readonly buckets = new Map<string, Bucket>();',
    '',
    '  constructor(',
    '    private readonly perMinute: number,',
    '    private readonly burst: number,',
    '    private readonly now: () => number = Date.now,',
    '  ) {}',
    '',
    '  tryAcquire(key: string): number {',
    '    const perSecond = this.perMinute / 60;',
    '    const bucket = this.buckets.get(key) ?? { tokens: this.burst, updatedAt: this.now() };',
    '    const elapsed = (this.now() - bucket.updatedAt) / 1000;',
    '',
    '    bucket.tokens = Math.min(this.burst, bucket.tokens + elapsed * perSecond);',
    '    bucket.updatedAt = this.now();',
    '    this.buckets.set(key, bucket);',
    '',
    '    if (bucket.tokens >= 1) {',
    '      bucket.tokens -= 1;',
    '      return 0;',
    '    }',
    '',
    '    return Math.ceil((1 - bucket.tokens) / perSecond);',
    '  }',
    '}',
  ),

  'packages/core/src/rateLimit/FixedWindowLimiter.ts': lines(
    "import type { RateLimiter } from './RateLimiter';",
    '',
    'export class FixedWindowLimiter implements RateLimiter {',
    '  private readonly windows = new Map<string, { start: number; count: number }>();',
    '',
    '  constructor(',
    '    private readonly perMinute: number,',
    '    private readonly now: () => number = Date.now,',
    '  ) {}',
    '',
    '  tryAcquire(key: string): number {',
    '    const start = Math.floor(this.now() / 60_000) * 60_000;',
    '    const window = this.windows.get(key);',
    '',
    '    if (!window || window.start !== start) {',
    '      this.windows.set(key, { start, count: 1 });',
    '      return 0;',
    '    }',
    '',
    '    if (window.count < this.perMinute) {',
    '      window.count += 1;',
    '      return 0;',
    '    }',
    '',
    '    return Math.ceil((start + 60_000 - this.now()) / 1000);',
    '  }',
    '}',
  ),

  'packages/core/src/config.ts': lines(
    "import type { LimitPolicy } from './rateLimit/RateLimiter';",
    '',
    'export interface ShopConfig {',
    '  port: number;',
    '  healthCheckTimeoutMs: number;',
    '  /** Requests each tenant may make, by tenant id. `default` covers everyone else. */',
    '  rateLimits: Record<string, LimitPolicy>;',
    '}',
    '',
    'export const config: ShopConfig = {',
    '  port: Number(process.env.PORT ?? 8080),',
    '  healthCheckTimeoutMs: Number(process.env.HEALTH_TIMEOUT_MS ?? 5000),',
    '  rateLimits: {',
    "    default: { strategy: 'token-bucket', perMinute: 600, burst: 50 },",
    "    'tenant-enterprise': { strategy: 'token-bucket', perMinute: 6000, burst: 500 },",
    "    'tenant-batch': { strategy: 'fixed-window', perMinute: 120 },",
    '  },',
    '};',
  ),

  'packages/api/src/middleware/rateLimit.ts': lines(
    "import type { NextFunction, Request, Response } from 'express';",
    "import type { LimitPolicy, RateLimiter } from '@acme/core/rateLimit/RateLimiter';",
    "import { FixedWindowLimiter } from '@acme/core/rateLimit/FixedWindowLimiter';",
    "import { TokenBucketLimiter } from '@acme/core/rateLimit/TokenBucketLimiter';",
    "import { TooManyRequestsError } from '../errors';",
    '',
    'export interface RateLimitOptions {',
    '  policies: Record<string, LimitPolicy>;',
    "  /** Counted apart from the tenant's own limit, for routes that need a tighter one. */",
    '  bucket?: string;',
    '}',
    '',
    'export function rateLimit(options: RateLimitOptions) {',
    '  const limiters = new Map<string, RateLimiter>();',
    '',
    '  return (request: Request, _response: Response, next: NextFunction) => {',
    "    const tenant = request.header('x-tenant-id') ?? 'anonymous';",
    '    const key = options.bucket ? `${tenant}:${options.bucket}` : tenant;',
    '    const policy = options.policies[tenant] ?? options.policies.default;',
    '',
    '    let limiter = limiters.get(key);',
    '    if (!limiter) {',
    '      limiter = create(policy);',
    '      limiters.set(key, limiter);',
    '    }',
    '',
    '    const wait = limiter.tryAcquire(key);',
    '    next(wait === 0 ? undefined : new TooManyRequestsError(wait));',
    '  };',
    '}',
    '',
    'function create(policy: LimitPolicy): RateLimiter {',
    "  return policy.strategy === 'fixed-window'",
    '    ? new FixedWindowLimiter(policy.perMinute)',
    '    : new TokenBucketLimiter(policy.perMinute, policy.burst);',
    '}',
  ),

  // Lines 3 to 11 are the rate-limit wiring and 14 to 17 the health check — the two regions the
  // answer below splits this file into.
  'packages/api/src/server.ts': lines(
    "import express from 'express';",
    "import { config } from '@acme/core/config';",
    "import { rateLimit } from './middleware/rateLimit';",
    "import { ordersRouter } from './routes/orders';",
    "import { pingDatabase } from './db';",
    '',
    'export function createServer() {',
    '  const app = express();',
    '',
    '  app.use(express.json());',
    '  app.use(rateLimit({ policies: config.rateLimits }));',
    "  app.use('/orders', ordersRouter);",
    '',
    "  app.get('/health', async (_request, response) => {",
    '    const healthy = await checkDatabase(config.healthCheckTimeoutMs);',
    '    response.status(healthy ? 200 : 503).end();',
    '  });',
    '',
    '  app.listen(config.port);',
    '  return app;',
    '}',
    '',
    'async function checkDatabase(timeoutMs: number): Promise<boolean> {',
    '  const timeout = new Promise<boolean>((resolve) => setTimeout(() => resolve(false), timeoutMs));',
    '  return Promise.race([pingDatabase(), timeout]);',
    '}',
  ),

  'packages/api/src/routes/orders.ts': lines(
    "import { Router } from 'express';",
    "import { config } from '@acme/core/config';",
    "import { rateLimit } from '../middleware/rateLimit';",
    "import { createOrder, listOrders } from '../orders';",
    '',
    'export const ordersRouter = Router();',
    '',
    "ordersRouter.get('/', async (request, response) => {",
    '  response.json(await listOrders(request.query));',
    '});',
    '',
    'ordersRouter.post(',
    "  '/',",
    "  rateLimit({ policies: config.rateLimits, bucket: 'orders:write' }),",
    '  async (request, response) => {',
    '    response.status(201).json(await createOrder(request.body));',
    '  },',
    ');',
  ),

  'packages/api/src/errors.ts': lines(
    "import type { Response } from 'express';",
    '',
    'export class HttpError extends Error {',
    '  constructor(readonly status: number, message: string) {',
    '    super(message);',
    '  }',
    '}',
    '',
    'export class NotFoundError extends HttpError {',
    '  constructor(what: string) {',
    '    super(404, `${what} was not found`);',
    '  }',
    '}',
    '',
    'export class TooManyRequestsError extends HttpError {',
    '  constructor(readonly retryAfterSeconds: number) {',
    "    super(429, 'Too many requests');",
    '  }',
    '}',
    '',
    'export function sendError(error: unknown, response: Response): void {',
    '  if (error instanceof TooManyRequestsError) {',
    "    response.setHeader('Retry-After', String(error.retryAfterSeconds));",
    '  }',
    '',
    '  const status = error instanceof HttpError ? error.status : 500;',
    "  response.status(status).json({ error: status === 500 ? 'Internal error' : (error as Error).message });",
    '}',
  ),

  'packages/api/test/rateLimit.test.ts': lines(
    "import { describe, expect, it } from 'vitest';",
    "import { FixedWindowLimiter } from '@acme/core/rateLimit/FixedWindowLimiter';",
    "import { TokenBucketLimiter } from '@acme/core/rateLimit/TokenBucketLimiter';",
    '',
    "describe('TokenBucketLimiter', () => {",
    "  it('allows a burst, then asks the caller to wait', () => {",
    '    let now = 0;',
    '    const limiter = new TokenBucketLimiter(60, 2, () => now);',
    '',
    "    expect(limiter.tryAcquire('t')).toBe(0);",
    "    expect(limiter.tryAcquire('t')).toBe(0);",
    "    expect(limiter.tryAcquire('t')).toBe(1);",
    '',
    '    now += 1000;',
    "    expect(limiter.tryAcquire('t')).toBe(0);",
    '  });',
    '});',
    '',
    "describe('FixedWindowLimiter', () => {",
    "  it('starts again at the top of each minute', () => {",
    '    let now = 0;',
    '    const limiter = new FixedWindowLimiter(1, () => now);',
    '',
    "    expect(limiter.tryAcquire('t')).toBe(0);",
    "    expect(limiter.tryAcquire('t')).toBe(60);",
    '',
    '    now = 60_000;',
    "    expect(limiter.tryAcquire('t')).toBe(0);",
    '  });',
    '});',
  ),

  'packages/web/src/apiClient.ts': lines(
    "import { showToast } from './Toast';",
    '',
    'const MAX_RETRIES = 3;',
    '',
    'export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {',
    '  for (let attempt = 0; ; attempt++) {',
    '    const response = await fetch(`/api${path}`, init);',
    '',
    '    if (response.status === 429 && attempt < MAX_RETRIES) {',
    "      const seconds = Number(response.headers.get('Retry-After') ?? 1);",
    '      await new Promise((resolve) => setTimeout(resolve, seconds * 1000));',
    '      continue;',
    '    }',
    '',
    '    if (response.status === 429) {',
    "      showToast('slowDown');",
    '    }',
    '',
    '    if (!response.ok) {',
    '      throw new Error(`Request to ${path} failed with ${response.status}`);',
    '    }',
    '',
    '    return (await response.json()) as T;',
    '  }',
    '}',
  ),

  'packages/web/src/Toast.tsx': lines(
    'const messages = {',
    "  saved: 'Saved.',",
    "  failed: 'Something went wrong. Try again.',",
    "  slowDown: 'You are going a little fast. Wait a moment and try again.',",
    '};',
    '',
    'export type ToastMessage = keyof typeof messages;',
    '',
    'export function showToast(message: ToastMessage): void {',
    "  window.dispatchEvent(new CustomEvent('toast', { detail: messages[message] }));",
    '}',
  ),
};

// ---------------------------------------------------------------------------------------------
// What the model answers

const LIMITER = 'packages/core/src/rateLimit/RateLimiter.ts';
const BUCKET = 'packages/core/src/rateLimit/TokenBucketLimiter.ts';
const WINDOW = 'packages/core/src/rateLimit/FixedWindowLimiter.ts';
const CONFIG = 'packages/core/src/config.ts';
const MIDDLEWARE = 'packages/api/src/middleware/rateLimit.ts';
const WIRING = 'packages/api/src/server.ts#rate-limit';
const HEALTH = 'packages/api/src/server.ts#health-timeout';
const ORDERS = 'packages/api/src/routes/orders.ts';
const ERRORS = 'packages/api/src/errors.ts';
const THROTTLE = 'packages/api/src/legacy/throttle.ts';
const TESTS = 'packages/api/test/rateLimit.test.ts';
const CLIENT = 'packages/web/src/apiClient.ts';
const TOAST = 'packages/web/src/Toast.tsx';
const DOCS = 'docs/rate-limits.md';
const CHANGELOG = 'CHANGELOG.md';

/** Node ids the spec points at. */
export const GUIDE_NODES = { LIMITER, MIDDLEWARE, ERRORS, CLIENT, TOAST, HEALTH } as const;

type State = 'changed' | 'added' | 'deleted' | 'risky';

function node(
  id: string,
  details: {
    title: string;
    whatChanged: string;
    whyItChanged: string;
    howItAffectsOthers?: string;
    implementationNotes?: string;
    risks?: string[];
    importance: number;
    states: State[];
    symbol?: string;
    lines?: [number, number];
  },
) {
  return {
    id,
    filePath: id.split('#')[0],
    symbol: details.symbol ?? '',
    startLine: details.lines?.[0] ?? 0,
    endLine: details.lines?.[1] ?? 0,
    title: details.title,
    whatChanged: details.whatChanged,
    whyItChanged: details.whyItChanged,
    howItAffectsOthers: details.howItAffectsOthers ?? '',
    implementationNotes: details.implementationNotes ?? '',
    risks: details.risks ?? [],
    importance: details.importance,
    states: details.states,
  };
}

function edge(
  sourceNodeId: string,
  targetNodeId: string,
  kind: 'direct' | 'conceptual',
  explanation: string,
  risks: string[] = [],
) {
  return { sourceNodeId, targetNodeId, kind, explanation, risks };
}

function container(
  id: string,
  displayOrder: number,
  title: string,
  summary: string,
  explanation: string,
  nodeIds: string[],
  risks: string[] = [],
) {
  return { id, title, summary, explanation, risks, displayOrder, entryNodeId: nodeIds[0], nodeIds };
}

const limitsPath = [LIMITER, BUCKET, WINDOW, CONFIG, MIDDLEWARE, WIRING, ORDERS, ERRORS, THROTTLE, TESTS];

/** The analysis a good model gives of this change, in both groupings. */
export function stubGuideResult() {
  const dependencyContainers = [
    container(
      'limits-end-to-end',
      1,
      'Per-tenant limits, from policy to 429',
      'The whole path of a limit: the contract, its two strategies, the policies, the middleware and the 429.',
      'One cluster because every file is a step on one path: the middleware makes no sense without the `RateLimiter` contract, nor the 429 without the middleware. Start at the contract and follow a request.',
      limitsPath,
      ['Every file here has to ship together: the middleware imports all three core files.'],
    ),
    container(
      'client-backoff',
      2,
      'The web client backs off',
      'The client reads the 429, waits as long as the server asks, and tells the user when it gives up.',
      'Follows from the API change but lives in another package, so it gets its own cluster.',
      [CLIENT, TOAST],
    ),
    container(
      'documentation',
      3,
      'Documenting the limits',
      'A page describing the default policies, and a changelog entry pointing at it.',
      'Prose only. Worth checking that the numbers match `config.ts`.',
      [DOCS, CHANGELOG],
    ),
    container(
      'health-timeout',
      4,
      'Health check timeout',
      'An unrelated fix that happens to sit in `server.ts`: the timeout now comes from config.',
      'Split from the rest of `server.ts` because it has nothing to do with rate limiting.',
      [HEALTH],
    ),
  ];

  const clusterContainers = [
    container(
      'limiter-library',
      1,
      'The limiter library',
      'The contract, the two strategies, and the policies that choose between them.',
      'Everything in `@acme/core` that the change added or touched.',
      [LIMITER, BUCKET, WINDOW, CONFIG],
    ),
    container(
      'api-enforcement',
      2,
      'Enforcement in the API',
      'The middleware, where it is installed, the stricter order limit, the 429, and what was removed.',
      'Everything in `@acme/api` that serves rate limiting.',
      [MIDDLEWARE, WIRING, ORDERS, ERRORS, THROTTLE, TESTS],
    ),
    container(
      'client-experience',
      3,
      'What users see',
      'Retrying after a 429, and the message shown when retries run out.',
      'The `@acme/web` side of the change.',
      [CLIENT, TOAST],
    ),
    container(
      'documentation',
      4,
      'Documentation',
      'The new page on limits and the changelog.',
      'Prose only.',
      [DOCS, CHANGELOG],
    ),
    container(
      'operations',
      5,
      'Operations',
      'The health check timeout.',
      'Unrelated to rate limiting.',
      [HEALTH],
    ),
  ];

  return {
    summary:
      'Replaces the global request throttle with **per-tenant rate limiting**. A `RateLimiter` contract in core gets token-bucket and fixed-window implementations; the API enforces each tenant’s policy and answers `429` with `Retry-After`, and the web client now backs off instead of failing. Separately, the health check’s timeout moves into config.',
    overallRisks: [
      'Limits are counted in each API process, so with several instances a tenant gets its limit once per instance.',
      'Requests without an `x-tenant-id` header all share one `anonymous` allowance.',
    ],
    dependencyReadingOrder: dependencyContainers.flatMap((c) => c.nodeIds),
    dependencyContainers,
    clusterReadingOrder: clusterContainers.flatMap((c) => c.nodeIds),
    clusterContainers,
    nodes: [
      node(LIMITER, {
        title: 'The limiter contract',
        whatChanged: 'New `RateLimiter` interface with `tryAcquire(key)`, and the `LimitPolicy` type describing a limit.',
        whyItChanged: 'Lets the API pick a strategy per tenant without knowing how either one counts.',
        howItAffectsOthers: 'Both limiters implement it; config and the middleware use its types.',
        implementationNotes: '`tryAcquire` returns seconds to wait rather than a boolean, which is what fills `Retry-After`.',
        importance: 5,
        states: ['added'],
      }),
      node(BUCKET, {
        title: 'Token-bucket limiter',
        whatChanged: 'Refills tokens continuously, up to `burst`, and spends one per request.',
        whyItChanged: 'The default strategy: smooth under steady traffic, tolerant of short bursts.',
        risks: ['Buckets are never evicted, so memory grows with every tenant ever seen.'],
        importance: 4,
        states: ['added', 'risky'],
      }),
      node(WINDOW, {
        title: 'Fixed-window limiter',
        whatChanged: 'Counts requests per calendar minute and refuses past the limit.',
        whyItChanged: 'For batch tenants, where a hard per-minute ceiling is easier to reason about.',
        risks: ['A client can send twice its limit across the boundary between two minutes.'],
        importance: 3,
        states: ['added', 'risky'],
      }),
      node(CONFIG, {
        title: 'Policies in config',
        whatChanged: 'Adds `rateLimits`, one policy per tenant plus a `default`, and makes the health timeout configurable.',
        whyItChanged: 'Limits are business decisions, so they live in config rather than in code.',
        howItAffectsOthers: 'The middleware reads these policies on every request.',
        importance: 4,
        states: ['changed'],
      }),
      node(MIDDLEWARE, {
        title: 'Rate-limit middleware',
        whatChanged: 'Finds the tenant’s policy and limiter, and refuses a request with `TooManyRequestsError` when told to wait.',
        whyItChanged: 'So one busy tenant can no longer use up everyone’s allowance.',
        howItAffectsOthers: 'Every route passes through it once `createServer` installs it.',
        risks: ['The tenant comes from a header read before authentication, so it can be spoofed.'],
        importance: 5,
        states: ['added', 'risky'],
      }),
      node(WIRING, {
        title: 'Server installs the middleware',
        whatChanged: 'Swaps the global `throttle` for `rateLimit` with the configured policies.',
        whyItChanged: 'The one place every request enters.',
        importance: 3,
        states: ['changed'],
        symbol: 'createServer',
        lines: [3, 11],
      }),
      node(HEALTH, {
        title: 'Health check timeout from config',
        whatChanged: 'The database check waits `config.healthCheckTimeoutMs` instead of a fixed 2000 ms.',
        whyItChanged: 'Slow databases in staging failed the check. Unrelated to rate limiting.',
        importance: 2,
        states: ['changed'],
        symbol: 'health route',
        lines: [14, 17],
      }),
      node(ORDERS, {
        title: 'Tighter limit on placing orders',
        whatChanged: 'Placing an order also spends from an `orders:write` bucket.',
        whyItChanged: 'Order creation is the expensive endpoint, and the one scripts hammer.',
        importance: 3,
        states: ['changed'],
      }),
      node(ERRORS, {
        title: 'The 429 error',
        whatChanged: 'Adds `TooManyRequestsError`, and `sendError` sets `Retry-After` from it.',
        whyItChanged: 'The old throttle answered 503, which clients treated as an outage.',
        howItAffectsOthers: 'The web client reads the status and the header.',
        importance: 3,
        states: ['changed'],
      }),
      node(THROTTLE, {
        title: 'Global throttle removed',
        whatChanged: 'Deleted: one counter shared by every tenant.',
        whyItChanged: 'Replaced by the per-tenant middleware.',
        importance: 2,
        states: ['deleted'],
      }),
      node(TESTS, {
        title: 'Limiter tests with a fake clock',
        whatChanged: 'Covers a burst and a refill for the bucket, and a minute boundary for the window.',
        whyItChanged: 'Time-based code needs a clock the test controls.',
        risks: ['Nothing tests the middleware itself, or the 429 response.'],
        importance: 2,
        states: ['added'],
      }),
      node(CLIENT, {
        title: 'Client retries after a 429',
        whatChanged: 'Waits the server’s `Retry-After` and retries up to three times, then shows a toast.',
        whyItChanged: 'A limited request used to surface as a failure the user could do nothing about.',
        importance: 4,
        states: ['changed'],
      }),
      node(TOAST, {
        title: '“Slow down” message',
        whatChanged: 'Adds the `slowDown` message.',
        whyItChanged: 'What the client shows when retries run out.',
        importance: 1,
        states: ['changed'],
      }),
      node(DOCS, {
        title: 'Rate-limit documentation',
        whatChanged: 'New page listing the default policies and the 429 behaviour.',
        whyItChanged: 'Integrators need to know the limits before they hit them.',
        importance: 1,
        states: ['added'],
      }),
      node(CHANGELOG, {
        title: 'Changelog entry',
        whatChanged: 'Notes rate limiting and the longer health check.',
        whyItChanged: 'Release notes.',
        importance: 1,
        states: ['changed'],
      }),
    ],
    edges: [
      edge(LIMITER, BUCKET, 'direct', '`TokenBucketLimiter` implements the contract.'),
      edge(LIMITER, WINDOW, 'direct', '`FixedWindowLimiter` implements the same contract.'),
      edge(LIMITER, CONFIG, 'direct', 'Config’s policies are `LimitPolicy` values.'),
      edge(CONFIG, MIDDLEWARE, 'direct', 'The middleware looks each tenant’s policy up here.'),
      edge(BUCKET, MIDDLEWARE, 'direct', 'Built for token-bucket policies.', [
        'Limiters live in process memory, so they are not shared between API instances.',
      ]),
      edge(WINDOW, MIDDLEWARE, 'direct', 'Built for fixed-window policies.'),
      edge(MIDDLEWARE, WIRING, 'direct', '`createServer` installs the middleware for every route.'),
      edge(MIDDLEWARE, ORDERS, 'direct', 'The orders route adds a second, stricter bucket.'),
      edge(MIDDLEWARE, ERRORS, 'direct', 'A refused request becomes a `TooManyRequestsError`.'),
      edge(MIDDLEWARE, THROTTLE, 'conceptual', 'The middleware takes over what the throttle did.'),
      edge(MIDDLEWARE, TESTS, 'conceptual', 'The tests cover the limiters the middleware builds.'),
      edge(ERRORS, CLIENT, 'conceptual', 'The client reads the 429 and its `Retry-After` header.', [
        'An HTTP-date `Retry-After` would be read as zero and retried at once.',
      ]),
      edge(CLIENT, TOAST, 'direct', 'Shown when retries run out.'),
      edge(CONFIG, DOCS, 'conceptual', 'The documented numbers are the defaults in config.'),
      edge(DOCS, CHANGELOG, 'conceptual', 'The changelog points readers at the new page.'),
      edge(CONFIG, HEALTH, 'direct', 'The health check reads its timeout from config.'),
    ],
    implementationGroups: [{ abstractionNodeId: LIMITER, implementationNodeIds: [BUCKET, WINDOW] }],
  };
}

/** The repository profile the guide's profile step shows. */
export const stubGuideProfile = {
  purpose: 'An online shop: a public ordering API, a web front end, and the shared code both use.',
  // Plain text: the profile screen shows these in text areas, where backticks stay backticks.
  architecture:
    'An npm workspace of three packages. @acme/api is an Express server, @acme/web is the React front end, and @acme/core holds configuration and code shared by both.',
  modules: [
    { name: 'core', path: 'packages/core', summary: 'Configuration and shared building blocks.', relatedModules: ['api', 'web'] },
    { name: 'api', path: 'packages/api', summary: 'The public HTTP API: routes, middleware and errors.', relatedModules: ['core'] },
    { name: 'web', path: 'packages/web', summary: 'The browser front end and its API client.', relatedModules: ['core'] },
  ],
  layering: 'web calls api over HTTP; both depend on core, which depends on nothing.',
  patterns: '- Express middleware for cross-cutting concerns.\n- Errors are HttpError subclasses, turned into responses in one place.',
  entryPoints: [
    { path: 'packages/api/src/server.ts', purpose: 'Creates and starts the API server.' },
    { path: 'packages/web/src/apiClient.ts', purpose: 'Every request the front end makes.' },
  ],
  testLayout: 'Vitest, with tests beside each package under `test/`.',
  documentationSources: ['CHANGELOG.md'],
};
