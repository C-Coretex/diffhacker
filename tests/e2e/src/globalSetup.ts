import { readdirSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/**
 * Sweeps temporary directories left behind by earlier runs.
 *
 * WebView2 keeps a browser profile inside each app's directory and does not always release every
 * handle by the time the process is gone, so a per-test cleanup sometimes cannot finish. Failing
 * a test over an undeletable temp directory would be absurd, and leaving them to accumulate
 * would be untidy — so each run tidies up after the last one, when nothing holds them any more.
 *
 * Only this suite's own prefixes, and only from before this run started.
 */
export default function globalSetup(): void {
  const prefixes = ['diffhacker-e2e-'];
  const startedAt = Date.now();
  let removed = 0;

  for (const entry of safeList(tmpdir())) {
    if (!prefixes.some((prefix) => entry.startsWith(prefix))) {
      continue;
    }

    const path = join(tmpdir(), entry);

    try {
      if (statSync(path).mtimeMs >= startedAt) {
        continue;
      }

      rmSync(path, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
      removed += 1;
    } catch {
      // Still held by something. The next run will get it.
    }
  }

  if (removed > 0) {
    console.log(`[e2e] removed ${removed} leftover temporary directory(ies) from earlier runs.`);
  }
}

function safeList(directory: string): string[] {
  try {
    return readdirSync(directory);
  } catch {
    return [];
  }
}
