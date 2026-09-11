import { describe, expect, it } from 'vitest';
import { en } from '@/i18n/en';
import { HELP_SECTIONS } from '@/store/appStore';
import { GUIDE_STEPS } from './guideSteps';
import { renderUserGuideMarkdown, slug } from './userGuideMarkdown';

/**
 * `docs/user-guide.md` is generated from the catalogue, and this is what keeps it so.
 *
 * A file snapshot rather than a hand-written comparison, because Vitest already knows how to do the
 * two things wanted: fail when the committed file differs, and rewrite it when asked to. `npm run
 * docs:guide` in `src/ui` is this file with `-u`.
 */
describe('docs/user-guide.md', () => {
  it('is exactly what the catalogue produces', async () => {
    await expect(renderUserGuideMarkdown()).toMatchFileSnapshot('../../../../docs/user-guide.md');
  });

  it('shows every step with its screenshot, in the guide’s order', () => {
    const markdown = renderUserGuideMarkdown();

    let from = 0;
    GUIDE_STEPS.forEach((step, index) => {
      const heading = `### ${index + 1}. ${en.help.guide.steps[step.id].title}`;
      const at = markdown.indexOf(heading, from);

      expect(at, heading).toBeGreaterThan(from);
      expect(markdown).toContain(`(../src/ui/src/help/screenshots/${step.screenshot})`);
      from = at;
    });
  });

  it('links its contents to headings that exist', () => {
    const markdown = renderUserGuideMarkdown();

    for (const section of HELP_SECTIONS) {
      const title = en.help.sections[section];
      expect(markdown).toContain(`- [${title}](#${slug(title)})`);
      expect(markdown).toContain(`\n## ${title}\n`);
    }
  });
});

describe('slug', () => {
  it('spells anchors the way GitHub does', () => {
    expect(slug('Step-by-step guide')).toBe('step-by-step-guide');
    expect(slug('What DiffHacker does')).toBe('what-diffhacker-does');
    expect(slug('FAQ')).toBe('faq');
  });
});
