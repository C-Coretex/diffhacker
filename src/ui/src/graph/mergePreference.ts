/**
 * Whether the diagram draws an abstraction and its implementations as one box.
 *
 * Remembered in `localStorage`, for the reasons `theme/useTheme.ts` gives for the colour scheme: it
 * is presentation and nothing else — it changes no analysis, costs nothing and reaches no host — and
 * a schema, a method, a handler and a table for one boolean would be all cost. Application-wide
 * rather than per analysis, because it is a habit about how someone reads, like the theme.
 *
 * What the *next run* asks the model for is a different setting entirely and lives with the host,
 * beside the change-clusters one: that one spends money.
 */
const STORAGE_KEY = 'diffhacker.graph.mergeImplementations';

/** On unless the reviewer turned it off — the view the feature exists for. */
export function storedMergePreference(): boolean {
  try {
    return window.localStorage?.getItem(STORAGE_KEY) !== 'false';
  } catch {
    // Storage blocked or throwing on access. Merging is a perfectly good answer.
    return true;
  }
}

export function rememberMergePreference(merge: boolean): void {
  try {
    window.localStorage?.setItem(STORAGE_KEY, merge ? 'true' : 'false');
  } catch {
    // The choice still applies for this session; it just will not survive a restart.
  }
}
