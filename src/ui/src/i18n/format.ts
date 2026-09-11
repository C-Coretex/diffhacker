/**
 * Number formatting for the interface.
 *
 * Deliberately not `toLocaleString()`. That reads its grouping from whatever ICU data the runtime
 * happens to carry, so the same number renders as `1,200` in one environment and `1200` in
 * another — which showed up as a test asserting one thing and the WebView showing the other.
 * DiffHacker ships English only (§0.6) and the host runs with invariant globalisation, so the
 * honest choice is one explicit rule rather than an inherited one.
 */
export function formatCount(value: number): string {
  const rounded = Math.trunc(value);
  const negative = rounded < 0;
  const digits = Math.abs(rounded).toString();

  let grouped = '';

  for (let i = 0; i < digits.length; i++) {
    if (i > 0 && (digits.length - i) % 3 === 0) {
      grouped += ',';
    }

    grouped += digits[i];
  }

  return negative ? `-${grouped}` : grouped;
}

/**
 * A UTC instant as the reviewer's own wall-clock time, `HH:mm:ss`.
 *
 * Deliberately not `toLocaleTimeString()`, for the same reason as {@link formatCount}: its output
 * depends on ICU data the runtime happens to carry, which is exactly the inconsistency an
 * invariant-globalisation host was chosen to avoid. `Date`'s local-time getters need none of it.
 */
export function formatTime(atUtc: string): string {
  const date = new Date(atUtc);

  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const pad = (value: number) => value.toString().padStart(2, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

/**
 * A size in bytes, as a reader wants it: exact below a kilobyte, one decimal above.
 *
 * Binary units — 1 KB is 1,024 bytes — because the numbers being described are the host's
 * `ToolText` caps, which are set in KiB, and a 48 KB cap should read as 48 KB rather than 49.2.
 */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${formatCount(bytes)} B`;
  }

  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unit = 0;

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }

  return `${value.toFixed(1)} ${units[unit]}`;
}

/**
 * Elapsed wall-clock time as `m:ss`, or `h:mm:ss` past an hour. Whole seconds, truncated, so a
 * ticking clock never shows a second that has not finished yet.
 */
export function formatElapsed(milliseconds: number): string {
  const total = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  const pad = (value: number) => value.toString().padStart(2, '0');

  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${minutes}:${pad(seconds)}`;
}
