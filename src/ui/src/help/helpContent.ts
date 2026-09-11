import { en } from '@/i18n/en';
import type { ResourceKey } from '@/i18n/translate';
import type { GuideStepId } from './guideSteps';

/**
 * Typed ways into the `help` block of the catalogue, shared by the Help screen and the generator
 * that writes the same content into `docs/user-guide.md`.
 *
 * The lists of FAQ entries, diagram topics and so on are read from the catalogue's own key order
 * rather than repeated here, so adding a question is one edit in `en.ts`. The guide's steps are the
 * exception — they have screenshots, so their order lives in `guideSteps.ts` — and the two checks at
 * the bottom of this file make a step without copy, or copy without a step, a compile error.
 */

type Help = typeof en.help;

export type FaqId = keyof Help['faq']['items'];
export type TroubleshootingId = keyof Help['troubleshooting']['items'];
export type DiagramTopicId = keyof Help['diagram']['items'];
export type ShortcutId = keyof Help['shortcuts']['keys'];

export const FAQ_IDS = Object.keys(en.help.faq.items) as FaqId[];
export const TROUBLESHOOTING_IDS = Object.keys(en.help.troubleshooting.items) as TroubleshootingId[];
export const DIAGRAM_TOPIC_IDS = Object.keys(en.help.diagram.items) as DiagramTopicId[];
export const SHORTCUT_IDS = Object.keys(en.help.shortcuts.keys) as ShortcutId[];

export function stepKey(id: GuideStepId, field: 'title' | 'body' | 'alt'): ResourceKey {
  return `help.guide.steps.${id}.${field}`;
}

export function faqKey(id: FaqId, field: 'question' | 'answer'): ResourceKey {
  return `help.faq.items.${id}.${field}`;
}

export function troubleshootingKey(id: TroubleshootingId, field: 'title' | 'body'): ResourceKey {
  return `help.troubleshooting.items.${id}.${field}`;
}

export function diagramTopicKey(id: DiagramTopicId, field: 'title' | 'body'): ResourceKey {
  return `help.diagram.items.${id}.${field}`;
}

export function shortcutKey(id: ShortcutId, field: 'key' | 'action'): ResourceKey {
  return `help.shortcuts.keys.${id}.${field}`;
}

// Every step in `GUIDE_STEPS` has its title, body and alt text in the catalogue…
const stepsHaveCopy: Record<GuideStepId, { title: string; body: string; alt: string }> =
  en.help.guide.steps;

// …and the catalogue holds no copy for a step the guide does not show.
type OrphanedCopy = Exclude<keyof Help['guide']['steps'], GuideStepId>;
const noOrphanedCopy: [OrphanedCopy] extends [never] ? true : OrphanedCopy = true;

void stepsHaveCopy;
void noOrphanedCopy;
