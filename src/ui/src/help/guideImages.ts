import { screenshotFor, type GuideStepId } from './guideSteps';

/**
 * The guide's screenshots, as URLs the bundle can serve.
 *
 * A glob rather than one import per file, so a step whose screenshot has not been generated yet
 * renders a placeholder instead of breaking the build — which matters exactly once, the first time
 * the end-to-end spec that produces them has to be run against a build that could not include them.
 * After that, `guideImages.test.ts` is what fails when one goes missing.
 *
 * Vite copies each file into `dist/assets` under a hashed name and hands back its relative URL, which
 * the host serves from `diffhacker://app/` like every other asset — the CSP's `img-src 'self'` is all
 * it needs. Small files come back as `data:` URIs instead, which the same policy also allows.
 */
const images = import.meta.glob<string>('./screenshots/*.png', { eager: true, import: 'default' });

export function screenshotUrl(id: GuideStepId): string | undefined {
  return images[`./screenshots/${screenshotFor(id)}`];
}
