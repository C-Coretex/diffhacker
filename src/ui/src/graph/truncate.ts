/**
 * How much directory context a node box can hold at `text-xs` in 260 pixels. A character budget
 * rather than a measurement: measuring means a DOM, and a DOM means this could not be tested or
 * run inside the layout worker. The budget is deliberately a little conservative — a line that
 * ends early looks considered, a line clipped mid-word looks broken.
 */
export const PATH_BUDGET = 38;

/** The basename of a repository-relative path, which is never truncated. */
export function basename(path: string): string {
  const cut = path.lastIndexOf('/');
  return cut < 0 ? path : path.slice(cut + 1);
}

/**
 * The directory part of a path, elided to fit a node box.
 *
 * Elides **by segment, never by character**. `src/DiffHacker.Core/Ana…` tells the reader nothing
 * about where the file is; `src/…/Analyses/` tells them the repository area and the immediate
 * folder, which is what they actually navigate by. The first segment survives because it names
 * the top-level area, and the last two because they name the neighbourhood.
 *
 * Returns an empty string for a file at the repository root — the box then shows nothing rather
 * than a lone slash.
 */
export function directoryContext(path: string, budget = PATH_BUDGET): string {
  const cut = path.lastIndexOf('/');
  if (cut < 0) return '';

  const directory = path.slice(0, cut);
  const full = `${directory}/`;
  if (full.length <= budget) return full;

  const segments = directory.split('/');
  if (segments.length <= 3) {
    // Too few segments to elide without losing the whole middle. Keep the first and the last,
    // which is the most this shape can honestly say.
    return segments.length <= 1 ? `${segments[0]}/` : `${segments[0]}/…/${segments.at(-1)}/`;
  }

  const elided = `${segments[0]}/…/${segments.slice(-2).join('/')}/`;

  // Still over after eliding: the surviving segment names are simply long. One segment rather
  // than two is the last honest reduction; past that the answer is that it does not fit.
  return elided.length <= budget ? elided : `${segments[0]}/…/${segments.at(-1)}/`;
}

/**
 * Shortens text to a budget, at a word boundary when there is one close enough.
 *
 * The ellipsis is part of the budget rather than added past it, so a truncated string is never
 * longer than an untruncated one — which is what keeps a fixed-size box fixed.
 */
export function clamp(text: string, budget: number): string {
  if (text.length <= budget) return text;

  const head = text.slice(0, budget - 1);
  const lastSpace = head.lastIndexOf(' ');

  // Only break at a word if doing so keeps most of the budget; otherwise a long word at the start
  // would collapse the whole line to two characters and an ellipsis.
  return lastSpace > budget * 0.6 ? `${head.slice(0, lastSpace)}…` : `${head}…`;
}
