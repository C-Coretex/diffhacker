/**
 * Putting text on the clipboard from inside the WebView.
 *
 * Two paths, and the second is not defensive noise. The renderer is served from
 * `diffhacker://app` — a custom scheme, not `https:` — and whether a WebView treats that as a
 * secure context is up to the host that registered it. Where it does not, `navigator.clipboard`
 * is simply `undefined` and there is nothing to catch. WebView2, WKWebView and WebKitGTK do not
 * have to agree about this, and only one of the three has been exercised (CI is deferred), so the
 * fallback is what makes "copy path" work rather than what makes it look safe.
 *
 * The fallback is `document.execCommand('copy')`: deprecated, still implemented everywhere, and
 * the only synchronous way to reach the clipboard without a secure context.
 */
export async function copyText(value: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value);
      return true;
    }
  } catch {
    // Denied, or no permission in this context. Fall through rather than give up: the older
    // path is not subject to the same permission.
  }

  return copyWithExecCommand(value);
}

function copyWithExecCommand(value: string): boolean {
  if (typeof document === 'undefined') return false;

  const field = document.createElement('textarea');

  field.value = value;
  // Off-screen rather than hidden: a `display: none` element cannot be selected, and a visible
  // one would flash a text box over the diagram.
  field.setAttribute('readonly', '');
  field.style.position = 'fixed';
  field.style.top = '-1000px';
  field.style.opacity = '0';

  document.body.appendChild(field);

  try {
    field.select();
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    field.remove();
  }
}
