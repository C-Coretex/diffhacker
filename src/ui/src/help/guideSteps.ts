/**
 * The step-by-step guide: which steps, in which order, and the screenshot each one shows.
 *
 * Three things read this list, and it is the one place they agree through:
 *
 * - `GuideStepper.tsx` draws one step at a time, its copy from `en.help.guide.steps.<id>`.
 * - `userGuideMarkdown.ts` writes the same steps into `docs/user-guide.md`.
 * - `tests/e2e/specs/15-user-guide.spec.ts` drives the real application through every step and
 *   captures the screenshot named here, into `src/ui/src/help/screenshots/`.
 *
 * **No imports, on purpose.** The end-to-end suite imports this file directly, the way it imports
 * `en.ts`, and Playwright's loader does not know the renderer's `@/` alias.
 */
export const GUIDE_STEPS = [
  { id: 'provider', screenshot: '01-add-provider.png' },
  { id: 'testConnection', screenshot: '02-test-connection.png' },
  { id: 'defaults', screenshot: '03-analysis-defaults.png' },
  { id: 'openRepository', screenshot: '04-open-repository.png' },
  { id: 'changes', screenshot: '05-changed-files.png' },
  { id: 'profile', screenshot: '06-repository-profile.png' },
  { id: 'runOptions', screenshot: '07-run-options.png' },
  { id: 'run', screenshot: '08-run-in-progress.png' },
  { id: 'overview', screenshot: '09-summary-and-risks.png' },
  { id: 'diagram', screenshot: '10-diagram.png' },
  { id: 'explanation', screenshot: '11-explanation-card.png' },
  { id: 'diff', screenshot: '12-diff-panel.png' },
  { id: 'reviewed', screenshot: '13-mark-reviewed.png' },
  { id: 'grouping', screenshot: '14-change-clusters.png' },
  { id: 'current', screenshot: '15-stale-banner.png' },
] as const;

export type GuideStep = (typeof GUIDE_STEPS)[number];
export type GuideStepId = GuideStep['id'];

/** The screenshot file for one step. */
export function screenshotFor(id: GuideStepId): string {
  const step = GUIDE_STEPS.find((candidate) => candidate.id === id);
  if (!step) throw new Error(`No guide step is called '${id}'.`);
  return step.screenshot;
}
