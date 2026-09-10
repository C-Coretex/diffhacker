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
