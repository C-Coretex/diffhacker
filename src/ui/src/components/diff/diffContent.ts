import type { FileContentInfo } from '@/contracts';

/**
 * The threshold at which the viewer stops trying to be clever.
 *
 * Requirement 9 asks that a very large file does not freeze the interface, and the honest answer is a
 * number rather than a promise. Above one megabyte on either side, Monaco is given the file as
 * plaintext with the minimap and word wrap off: tokenising a megabyte of source through a Monarch
 * grammar is the part that blocks the main thread, and the diff computation itself already happens in
 * a worker.
 *
 * The host declines anything over `ContentLimits.MaxBytes` — five megabytes — before it reaches the
 * bridge at all, so this only governs the band between the two.
 */
export const LARGE_FILE_BYTES = 1024 * 1024;

/**
 * What the host will not send at all: `ContentLimits.MaxBytes` in `DiffHacker.Core.Changes`.
 *
 * Duplicated here rather than put on the wire, because the renderer only ever *reports* it — the
 * decision is made and enforced host-side, and a file over it arrives as `too_large` with its true
 * size whatever this constant says. If the two ever disagree, the sentence is wrong and nothing else
 * is.
 */
export const HOST_MAX_BYTES = 5 * 1024 * 1024;

/** What the panel can be showing. Exactly one of these is true at a time. */
export type DiffBodyKind =
  /** Both sides are text, or one is text and the other legitimately absent. Monaco renders it. */
  | 'text'
  /** git will not diff the content. A statement and the facts, never a wall of bytes. */
  | 'binary'
  /** Bigger than the host will send. The size is still reported, truthfully. */
  | 'tooLarge'
  /** Neither side exists. Should not happen for a changed file, and says so if it does. */
  | 'absent';

export interface DiffContent {
  readonly kind: DiffBodyKind;

  /** The committed side, or null when the file was added. */
  readonly original: string | null;

  /** The working-tree side, or null when the file was deleted. */
  readonly modified: string | null;

  /** The larger of the two sides, which is what the threshold is judged on. */
  readonly sizeBytes: number;

  /** True when the content is past {@link LARGE_FILE_BYTES} and highlighting was turned off. */
  readonly degraded: boolean;

  /**
   * The encoding a side had to be decoded with when it was not UTF-8. Worth surfacing: a file read
   * as Latin-1 is a best effort, and the panel should admit that rather than show replacement
   * characters as though they were the file's own content.
   */
  readonly fallbackEncoding: string | null;
}

/**
 * Turns the two sides the host returned into one thing the panel can render.
 *
 * The precedence is deliberate. "Too large" beats "binary" because a five-megabyte binary and a
 * five-megabyte text file are the same problem from here, and the size is the more useful sentence.
 * "Absent on one side" is not a problem at all — it is what an added or a deleted file looks like, and
 * Monaco draws a one-sided diff perfectly well.
 */
export function describeContent(head: FileContentInfo, working: FileContentInfo): DiffContent {
  const sizeBytes = Math.max(head.sizeBytes, working.sizeBytes);
  const sides = [head, working];

  const kind: DiffBodyKind = sides.some((side) => side.kind === 'too_large')
    ? 'tooLarge'
    : sides.some((side) => side.kind === 'binary')
      ? 'binary'
      : sides.every((side) => side.kind === 'absent')
        ? 'absent'
        : 'text';

  return {
    kind,
    original: head.kind === 'text' ? (head.text ?? '') : null,
    modified: working.kind === 'text' ? (working.text ?? '') : null,
    sizeBytes,
    degraded: kind === 'text' && sizeBytes >= LARGE_FILE_BYTES,
    fallbackEncoding:
      sides.find((side) => side.usedFallbackEncoding)?.encoding ?? null,
  };
}

/**
 * Bytes as something a person reads. Lives here, dependency-free, rather than on `DiffPanel` —
 * `DiffUnavailable` needs it too, and importing it from a component that also pulls in Monaco's
 * setup module would drag that whole chunk along for a message that has no editor in it.
 */
export function formatBytes(bytes: number): string {
  const megabytes = bytes / (1024 * 1024);
  return megabytes >= 1 ? `${megabytes.toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`;
}
