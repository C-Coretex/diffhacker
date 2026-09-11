import { expect, test, type Locator, type Page } from '@playwright/test';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { userInfo } from 'node:os';
import { join, resolve } from 'node:path';
import { screenshotFor, type GuideStepId } from '../../../src/ui/src/help/guideSteps.ts';

/** Where the application bundles the guide's screenshots from. */
export const GUIDE_SCREENSHOT_DIRECTORY = resolve(
  import.meta.dirname, '..', '..', '..', 'src', 'ui', 'src', 'help', 'screenshots',
);

/** Where every run leaves its captures, whether or not the source tree is updated. */
const ARTIFACT_DIRECTORY = resolve(import.meta.dirname, '..', 'artifacts', 'guide-screenshots');

/**
 * The size the guide's screenshots are taken at: a common laptop's worth of window, so the pictures
 * show what most readers will actually see and stay legible when the Help screen scales them down.
 */
export const GUIDE_VIEWPORT = { width: 1440, height: 900 } as const;

/**
 * Takes the user guide's screenshots.
 *
 * Every capture lands in `artifacts/guide-screenshots/`. It is also written into the renderer's
 * source tree — `src/ui/src/help/screenshots/`, which the application bundles — when Playwright was
 * asked to update snapshots (`npm run docs:screenshots`), or when the file is not there at all, which
 * is how the very first set gets made. A normal run never overwrites a committed screenshot: the
 * pictures change every run (dates, timings), and a test suite that rewrote tracked files on every
 * pass would be noise in every diff.
 *
 * **Nothing personal reaches an image.** The fixture repository lives under the temp directory,
 * whose path carries the operating system's user name, and the stub provider listens on a random
 * port. Before each capture the page's text and input values are rewritten to neutral ones, and a
 * capture that would still show the user name in a path fails the test instead of being written.
 * Only this suite does that rewriting; no production code knows about it.
 */
export class GuideCamera {
  private readonly replacements: [string, string][];

  constructor(
    private readonly page: Page,
    replacements: Record<string, string>,
  ) {
    // Both separators, because the host reports paths as git spells them and the OS as it does.
    this.replacements = Object.entries(replacements).flatMap(([from, to]) =>
      from.includes('\\') || from.includes('/')
        ? [
            [from.replaceAll('/', '\\'), to.replaceAll('/', '\\')],
            [from.replaceAll('\\', '/'), to.replaceAll('\\', '/')],
          ]
        : [[from, to]],
    ) as [string, string][];
  }

  /** The whole window. */
  async window(id: GuideStepId): Promise<void> {
    await this.prepare();
    await this.save(id, await this.page.screenshot({ scale: 'css', animations: 'disabled' }));
  }

  /** One element, cropped to its own box. */
  async element(id: GuideStepId, element: Locator): Promise<void> {
    await element.scrollIntoViewIfNeeded();
    await this.prepare();
    await this.save(id, await element.screenshot({ scale: 'css', animations: 'disabled' }));
  }

  /**
   * A band of the window: as wide as `frame`, from just above `top` to just below `bottom`. For a
   * long form where the part a step is about is its first half, and the element as a whole would be
   * mostly fields the step does not mention.
   */
  async region(id: GuideStepId, frame: Locator, top: Locator, bottom: Locator): Promise<void> {
    await top.scrollIntoViewIfNeeded();
    await this.prepare();

    const [outer, first, last] = await Promise.all([frame.boundingBox(), top.boundingBox(), bottom.boundingBox()]);
    expect(outer && first && last, `The ${id} screenshot's region is not on screen`).toBeTruthy();

    const padding = 20;
    const y = Math.max(0, first!.y - padding);

    await this.save(
      id,
      await this.page.screenshot({
        scale: 'css',
        animations: 'disabled',
        clip: {
          x: outer!.x,
          y,
          width: outer!.width,
          height: last!.y + last!.height + padding - y,
        },
      }),
    );
  }

  private async prepare(): Promise<void> {
    await this.page.evaluate((pairs) => {
      const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const patterns = pairs.map(([from, to]) => [new RegExp(escape(from), 'gi'), to] as const);
      const rewrite = (text: string) => patterns.reduce((out, [from, to]) => out.replace(from, to), text);

      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
      for (let current = walker.nextNode(); current; current = walker.nextNode()) {
        const next = rewrite(current.nodeValue ?? '');
        if (next !== current.nodeValue) current.nodeValue = next;
      }

      for (const field of Array.from(document.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input, textarea'))) {
        const next = rewrite(field.value);
        if (next !== field.value) field.value = next;
      }
    }, this.replacements);

    // The guard. A user name inside a path is the one personal detail a screenshot of this app can
    // carry, and it is checked for rather than trusted to the rewriting above.
    const name = userInfo().username;
    const visible = await this.page.evaluate(() =>
      [
        document.body.innerText,
        ...Array.from(document.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input, textarea')).map((f) => f.value),
      ].join('\n'),
    );

    for (const separator of ['\\', '/']) {
      expect(
        visible.toLowerCase(),
        'A guide screenshot would show the user name in a path; add a replacement for it',
      ).not.toContain(`${separator}${name.toLowerCase()}${separator}`);
    }
  }

  private async save(id: GuideStepId, image: Buffer): Promise<void> {
    const file = screenshotFor(id);

    mkdirSync(ARTIFACT_DIRECTORY, { recursive: true });
    writeFileSync(join(ARTIFACT_DIRECTORY, file), image);
    await test.info().attach(`guide: ${id}`, { body: image, contentType: 'image/png' });

    const target = join(GUIDE_SCREENSHOT_DIRECTORY, file);
    const mode = test.info().config.updateSnapshots;

    if (mode === 'all' || mode === 'changed' || (mode === 'missing' && !existsSync(target))) {
      mkdirSync(GUIDE_SCREENSHOT_DIRECTORY, { recursive: true });
      writeFileSync(target, image);
    }
  }
}
