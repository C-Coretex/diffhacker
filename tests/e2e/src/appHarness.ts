import { chromium, expect, type Browser, type Page, type TestInfo } from '@playwright/test';
import { spawn, type ChildProcess } from 'node:child_process';
import { createServer } from 'node:net';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  openSync,
  readFileSync,
  readdirSync,
  rmSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { en } from './strings.ts';

const repositoryRoot = resolve(import.meta.dirname, '..', '..', '..');
const screenshotRoot = resolve(import.meta.dirname, '..', 'artifacts', 'screenshots');

export interface LaunchOptions {
  /**
   * Relaunch onto the state an earlier instance left behind — the value {@link DiffHackerApp.stop}
   * returned. This is how persistence across a restart is tested rather than assumed.
   */
  root?: string;

  /**
   * Launch with no reachable `git` on `PATH`, to exercise the condition that makes the whole
   * application non-functional (Iteration 2, requirement 6).
   */
  withoutGit?: boolean;
}

/**
 * The running application, driven for real.
 *
 * The renderer lives in a WebView2 window, and WebView2 is Chromium, so Playwright can attach to
 * it over the Chrome DevTools Protocol. Two things make that work:
 *
 * - WebView2 reads `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` from the environment and
 *   `PhotinoAppShell` never overrides it, so the harness can ask for a debugging port without
 *   any production code knowing about it.
 * - `--data-dir` puts the application's own state somewhere disposable. That switch is not
 *   cosmetic: .NET resolves the per-user directory through the Win32 known-folder API, which
 *   ignores `LOCALAPPDATA`, so without it these tests would write their throwaway providers and
 *   API keys into the developer's real secret store.
 *
 * `LOCALAPPDATA` is still redirected, for a different reason: it is where WebView2 keeps its own
 * browser profile, and a fresh one per app keeps one test's page state out of the next.
 *
 * Windows only, because CDP is. On macOS and Linux the shell is WKWebView and WebKitGTK, which
 * expose no equivalent, and the suite skips rather than pretending to have run.
 */
export class DiffHackerApp {
  private constructor(
    readonly page: Page,
    /** Pass this back to {@link LaunchOptions.dataDirectory} to restart onto the same state. */
    readonly root: string,
    /** Where the application itself keeps settings, secrets and the log. */
    readonly dataDirectory: string,
    private readonly browser: Browser,
    private readonly host: ChildProcess,
    private readonly ownsRoot: boolean,
    private readonly testInfo: TestInfo,
    private readonly shotDirectory: string,
    /**
     * Shared across every app one test launches, so a journey that restarts the application
     * still produces one numbered sequence rather than two that both begin at 01.
     */
    private readonly shotCounter: { next: number },
  ) {}

  static async launch(
    testInfo: TestInfo,
    options: LaunchOptions = {},
    shotCounter: { next: number } = { next: 1 },
  ): Promise<DiffHackerApp> {
    const executable = resolveHostExecutable();
    const port = await freePort();

    // One root per app instance: the application's state beside the browser profile, so a
    // restart can be given the same root and find both again.
    const ownsRoot = options.root === undefined;
    const root = options.root ?? mkdtempSync(join(tmpdir(), 'diffhacker-e2e-'));
    const dataDirectory = join(root, 'data');
    const browserProfile = join(root, 'webview2');
    mkdirSync(dataDirectory, { recursive: true });
    mkdirSync(browserProfile, { recursive: true });

    // The host's console output is the first thing worth reading when a journey fails, so it is
    // kept rather than discarded.
    const consoleLog = join(root, 'host-console.log');
    const consoleStream = openSync(consoleLog, 'a');

    const host = spawn(executable, ['--data-dir', dataDirectory, '--verbose'], {
      cwd: dirname(executable),
      windowsHide: false,
      stdio: ['ignore', consoleStream, consoleStream],
      env: {
        ...process.env,
        // Ask WebView2 for a debugging port. Photino leaves this variable alone.
        WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}`,
        // Only the browser profile. Redirecting APPDATA or USERPROFILE as well stops WebView2
        // starting at all, and neither is consulted by AppPaths on Windows anyway.
        LOCALAPPDATA: browserProfile,
        PATH: options.withoutGit ? pathWithoutGit() : process.env.PATH,
      },
    });

    host.on('error', (error) => {
      throw new Error(`The DiffHacker host could not be started: ${error.message}`);
    });

    let browser: Browser;
    try {
      await waitForDebugger(port, host);
      browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
    } catch (error) {
      terminate(host);
      if (ownsRoot) {
        safeRemove(root);
      }
      throw error;
    }

    const page = await firstPage(browser);

    const shotDirectory = join(screenshotRoot, slug(testInfo.titlePath.join(' ')));
    mkdirSync(shotDirectory, { recursive: true });

    const app = new DiffHackerApp(
      page,
      root,
      dataDirectory,
      browser,
      host,
      ownsRoot,
      testInfo,
      shotDirectory,
      shotCounter,
    );

    await app.waitUntilReady();
    return app;
  }

  /**
   * Blocks until the window has rendered, the host handshake has completed, and the environment
   * probe has answered.
   *
   * All three are conditions, not durations — the window is up in a few hundred milliseconds and
   * a sleep long enough to be safe on a slow machine would be dead time on every launch.
   *
   * The handshake matters more than it looks. On startup the renderer mounts before the RPC
   * client exists, so `connection` passes briefly through `detached` and the host panel renders
   * "there is no host to talk to". A gate that only waited for the *connecting* message to
   * disappear would sail straight through that window and start clicking at controls that are
   * still disabled. The honest condition is the host panel being gone altogether, which only
   * happens once the ping has come back.
   */
  private async waitUntilReady(): Promise<void> {
    await expect(this.page.getByRole('heading', { level: 1, name: en.app.title })).toBeVisible();
    await expect(this.page.getByText(en.host.heading)).toHaveCount(0);
    await expect(this.page.getByText(en.environment.checking)).toHaveCount(0);
  }

  /** Whatever the host printed to its console. The first thing to read when a journey fails. */
  hostConsole(): string {
    const path = join(this.root, 'host-console.log');
    return existsSync(path) ? readFileSync(path, 'utf8') : '';
  }

  /**
   * Records every JSON-RPC frame the renderer sends to the host from now on.
   *
   * This is how "no API key crosses the bridge" (§0.2.13) becomes an assertion rather than a
   * claim. The transport looks `sendMessage` up on `window.external` at call time, so wrapping
   * the property is enough — and the wrapper verifies it actually took, because a recorder that
   * silently failed to install would make the assertion pass for the wrong reason.
   */
  async recordBridgeTraffic(): Promise<void> {
    const installed = await this.page.evaluate(() => {
      const target = (globalThis as Record<string, unknown>).external as
        | { sendMessage?: ((message: string) => void) & { recording?: boolean } }
        | undefined;

      if (typeof target?.sendMessage !== 'function') {
        return false;
      }

      if (target.sendMessage.recording) {
        return true;
      }

      const frames: string[] = [];
      (globalThis as Record<string, unknown>).__diffhackerFrames = frames;

      const original = target.sendMessage.bind(target);
      const wrapper = (message: string) => {
        frames.push(message);
        original(message);
      };
      wrapper.recording = true;
      target.sendMessage = wrapper;

      return target.sendMessage.recording === true;
    });

    expect(installed, 'The bridge recorder could not be installed on window.external').toBe(true);
  }

  /** Every frame sent since {@link recordBridgeTraffic}. */
  bridgeFrames(): Promise<string[]> {
    return this.page.evaluate(
      () => ((globalThis as Record<string, unknown>).__diffhackerFrames as string[]) ?? [],
    );
  }

  /**
   * Captures the window and attaches it to the report.
   *
   * Screenshots also land in `artifacts/screenshots/<test>/` under a numbered name, so the whole
   * journey can be flipped through in order without opening the HTML report.
   */
  async shot(name: string): Promise<void> {
    const index = this.shotCounter.next++;
    const fileName = `${String(index).padStart(2, '0')}-${slug(name)}.png`;
    const path = join(this.shotDirectory, fileName);

    await this.page.screenshot({ path });
    await this.testInfo.attach(name, { path, contentType: 'image/png' });
  }

  /** Contents of the rolling log, or an empty string when nothing has been logged. */
  logText(): string {
    const logDirectory = join(this.dataDirectory, 'logs');
    if (!existsSync(logDirectory)) {
      return '';
    }

    return readdirSync(logDirectory)
      .map((file) => readFileSync(join(logDirectory, file), 'utf8'))
      .join('\n');
  }

  /** Raw bytes of a file in the data directory, for "the key is not in here" assertions. */
  dataFileBytes(name: string): Buffer | null {
    const path = join(this.dataDirectory, name);
    return existsSync(path) ? readFileSync(path) : null;
  }

  /**
   * Stops the app and returns the root to relaunch from, so a test can prove that state
   * survives a restart rather than assuming it.
   */
  async stop(): Promise<string> {
    await this.browser.close();
    await terminateAndWait(this.host);
    return this.root;
  }

  async dispose(): Promise<void> {
    try {
      await this.browser.close();
    } catch {
      // Already gone.
    }

    await terminateAndWait(this.host);

    if (this.ownsRoot) {
      safeRemove(this.root);
    }
  }
}

/**
 * WebView2 keeps its own profile inside the data directory and does not always release every
 * handle by the time the process is gone. A leftover temp directory is not worth failing a test
 * over, and certainly not worth masking the failure that led here.
 */
function safeRemove(directory: string): void {
  try {
    rmSync(directory, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
  } catch {
    // Windows will clean it up with the rest of TEMP.
  }
}

/** Launches apps for one test and disposes of every one afterwards. */
export class AppFactory {
  private readonly running: DiffHackerApp[] = [];
  private readonly shotCounter = { next: 1 };

  constructor(private readonly testInfo: TestInfo) {}

  async launch(options: LaunchOptions = {}): Promise<DiffHackerApp> {
    const app = await DiffHackerApp.launch(this.testInfo, options, this.shotCounter);
    this.running.push(app);
    return app;
  }

  async disposeAll(): Promise<void> {
    for (const app of this.running.splice(0).reverse()) {
      await app.dispose();
    }
  }
}

/** True when this platform can be driven over CDP: WebView2, so Windows. */
export function canDriveTheWindow(): boolean {
  return process.platform === 'win32';
}

function resolveHostExecutable(): string {
  const override = process.env.DIFFHACKER_HOST_EXE;
  if (override) {
    if (!existsSync(override)) {
      throw new Error(`DIFFHACKER_HOST_EXE points at a file that does not exist: ${override}`);
    }
    return override;
  }

  const configuration = process.env.DIFFHACKER_CONFIGURATION ?? 'Debug';
  const name = process.platform === 'win32' ? 'DiffHacker.Host.exe' : 'DiffHacker.Host';
  const candidate = join(
    repositoryRoot,
    'src',
    'DiffHacker.Host',
    'bin',
    configuration,
    'net10.0',
    name,
  );

  if (!existsSync(candidate)) {
    throw new Error(
      `The host has not been built. Expected ${candidate}.\n` +
        'Run: dotnet build src/DiffHacker.slnx',
    );
  }

  return candidate;
}

/**
 * `PATH` with every directory that contains a git executable removed.
 *
 * Probing for the executable rather than filtering on the word "git" in the path: a shim
 * somewhere unexpected would otherwise leave git reachable and the test would assert the
 * opposite of what it claims.
 */
function pathWithoutGit(): string {
  const separator = process.platform === 'win32' ? ';' : ':';
  const names = process.platform === 'win32' ? ['git.exe', 'git.cmd', 'git.bat'] : ['git'];

  return (process.env.PATH ?? '')
    .split(separator)
    .filter((directory) => {
      if (!directory) {
        return false;
      }

      return !names.some((name) => existsSync(join(directory, name)));
    })
    .join(separator);
}

async function freePort(): Promise<number> {
  return new Promise((resolveWith, reject) => {
    const server = createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      if (address === null || typeof address === 'string') {
        server.close();
        reject(new Error('Could not reserve a debugging port.'));
        return;
      }

      const { port } = address;
      server.close(() => resolveWith(port));
    });
  });
}

/**
 * Polls the debugging endpoint until WebView2 answers.
 *
 * Tight interval on purpose: the window is usually ready in a few hundred milliseconds, and the
 * deadline exists only so a failure to start reports itself instead of hanging.
 */
async function waitForDebugger(port: number, host: ChildProcess): Promise<void> {
  const deadline = Date.now() + 30_000;
  let lastError = 'no attempt made';

  while (Date.now() < deadline) {
    if (host.exitCode !== null) {
      throw new Error(`The DiffHacker host exited with code ${host.exitCode} before opening a window.`);
    }

    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/version`);
      if (response.ok) {
        return;
      }
      lastError = `HTTP ${response.status}`;
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
    }

    await delay(50);
  }

  terminate(host);
  throw new Error(`WebView2 never exposed a debugging port on ${port}. Last attempt: ${lastError}`);
}

/** The renderer's page. WebView2 exposes exactly one, but it may appear a moment after connecting. */
async function firstPage(browser: Browser): Promise<Page> {
  const deadline = Date.now() + 15_000;

  while (Date.now() < deadline) {
    const page = browser.contexts().flatMap((context) => context.pages())[0];
    if (page) {
      return page;
    }

    await delay(50);
  }

  throw new Error('Connected to WebView2 but it never reported a page.');
}

function terminate(host: ChildProcess): void {
  if (host.exitCode !== null || host.killed) {
    return;
  }

  if (process.platform === 'win32' && host.pid !== undefined) {
    // The window owns a native message loop; SIGTERM does not reliably unwind it.
    try {
      spawn('taskkill', ['/PID', String(host.pid), '/T', '/F'], { stdio: 'ignore' });
      return;
    } catch {
      // Fall through to the portable path.
    }
  }

  host.kill();
}

async function terminateAndWait(host: ChildProcess): Promise<void> {
  if (host.exitCode !== null) {
    return;
  }

  terminate(host);

  const deadline = Date.now() + 10_000;
  while (host.exitCode === null && Date.now() < deadline) {
    await delay(25);
  }
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolveWith) => setTimeout(resolveWith, milliseconds));
}

function slug(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80);
}
