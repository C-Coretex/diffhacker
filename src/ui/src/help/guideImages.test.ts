import { describe, expect, it } from 'vitest';
import { GUIDE_STEPS } from './guideSteps';
import { screenshotUrl } from './guideImages';

/**
 * Every step of the guide has its screenshot in the bundle.
 *
 * The stepper survives a missing one — it draws a placeholder, so a fresh checkout still builds — but
 * shipping a guide with a hole in it should fail somewhere, and this is where. The fix is to run
 * `npm run docs:screenshots` in `tests/e2e`, which drives the real application through every step.
 */
describe('the guide’s screenshots', () => {
  it.each(GUIDE_STEPS.map((step) => [step.id, step.screenshot]))('%s has %s', (id) => {
    expect(screenshotUrl(id as (typeof GUIDE_STEPS)[number]['id'])).toBeTruthy();
  });

  it('names each screenshot once', () => {
    const files = GUIDE_STEPS.map((step) => step.screenshot);
    expect(new Set(files).size).toBe(files.length);
  });
});
