import type { ReactNode } from 'react';
import { ChevronRightIcon } from 'lucide-react';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';
import { cn } from '@/lib/utils';
import { HELP_SECTIONS, useAppStore, type HelpSection } from '@/store/appStore';
import { Markdown } from '@/components/analysis/Markdown';
import { GuideStepper } from './GuideStepper';
import {
  DIAGRAM_TOPIC_IDS,
  FAQ_IDS,
  SHORTCUT_IDS,
  TROUBLESHOOTING_IDS,
  diagramTopicKey,
  faqKey,
  shortcutKey,
  troubleshootingKey,
} from './helpContent';

/**
 * Help: the step-by-step guide, what the product is, how to read the diagram, the shortcuts, and
 * the questions people ask first.
 *
 * A screen of its own rather than a panel over the current one, reachable from the header on every
 * screen and returning to wherever it was opened from (`openHelp`/`closeHelp` in the store). Nothing
 * here asks the host for anything: it is the catalogue and a folder of screenshots, which is also why
 * it works before a provider, a repository or even git is set up — the moment someone most needs it.
 *
 * Every word comes from `en.help`, drawn as Markdown by the same component that draws the model's
 * prose. `docs/user-guide.md` is generated from the same strings, so the two cannot drift.
 */
export function HelpScreen() {
  const t = useT();
  const section = useAppStore((state) => state.helpSection);
  const setSection = useAppStore((state) => state.setHelpSection);

  return (
    <div className="flex flex-col gap-6" data-testid="help-screen" data-section={section}>
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold tracking-tight">{t('help.heading')}</h2>
        <p className="text-sm text-muted-foreground">{t('help.description')}</p>
      </div>

      <SectionPicker active={section} onChange={setSection} />

      <section aria-label={t(`help.sections.${section}`)}>{renderSection(section)}</section>
    </div>
  );
}

function renderSection(section: HelpSection): ReactNode {
  switch (section) {
    case 'guide':
      return <GuideStepper />;
    case 'about':
      return <AboutSection />;
    case 'diagram':
      return <DiagramSection />;
    case 'shortcuts':
      return <ShortcutsSection />;
    case 'faq':
      return (
        <Disclosures
          testId="help-faq"
          items={FAQ_IDS.map((id) => ({
            id,
            summary: faqKey(id, 'question'),
            body: faqKey(id, 'answer'),
          }))}
        />
      );
    case 'troubleshooting':
      return (
        <Disclosures
          testId="help-troubleshooting"
          items={TROUBLESHOOTING_IDS.map((id) => ({
            id,
            summary: troubleshootingKey(id, 'title'),
            body: troubleshootingKey(id, 'body'),
          }))}
        />
      );
  }
}

/** The sections, as the pressed-button group `ThemePicker` and `GroupingPicker` already use. */
function SectionPicker({
  active,
  onChange,
}: {
  active: HelpSection;
  onChange: (section: HelpSection) => void;
}) {
  const t = useT();

  return (
    <div
      role="group"
      aria-label={t('help.sectionsLabel')}
      className="flex w-fit flex-wrap items-center gap-0.5 rounded-md border border-border p-0.5"
    >
      {HELP_SECTIONS.map((section) => (
        <button
          key={section}
          type="button"
          onClick={() => onChange(section)}
          aria-pressed={active === section}
          data-testid={`help-section-${section}`}
          className={cn(
            'rounded px-3 py-1.5 text-sm text-muted-foreground hover:bg-accent hover:text-foreground',
            active === section && 'bg-accent text-foreground',
          )}
        >
          {t(`help.sections.${section}`)}
        </button>
      ))}
    </div>
  );
}

/** Prose reads badly stretched across a wide window, so every section but the guide keeps a measure. */
function Prose({ children }: { children: ReactNode }) {
  return <div className="flex max-w-3xl flex-col gap-6 text-sm leading-relaxed">{children}</div>;
}

function Topic({ heading, body }: { heading: ResourceKey; body: ResourceKey }) {
  const t = useT();

  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-base font-semibold">{t(heading)}</h3>
      <Markdown text={t(body)} className="gap-2" />
    </div>
  );
}

function AboutSection() {
  const t = useT();

  return (
    <Prose>
      <Markdown text={t('help.about.body')} className="gap-2.5" />
      <Topic heading="help.about.needsHeading" body="help.about.needs" />
      <Topic heading="help.about.neverHeading" body="help.about.never" />
    </Prose>
  );
}

function DiagramSection() {
  const t = useT();

  return (
    <Prose>
      <p className="text-muted-foreground">{t('help.diagram.intro')}</p>
      {DIAGRAM_TOPIC_IDS.map((id) => (
        <Topic key={id} heading={diagramTopicKey(id, 'title')} body={diagramTopicKey(id, 'body')} />
      ))}
    </Prose>
  );
}

function ShortcutsSection() {
  const t = useT();

  return (
    <Prose>
      <p>{t('help.shortcuts.intro')}</p>

      <table className="w-full border-collapse text-left" data-testid="help-shortcuts">
        <thead>
          <tr className="border-b border-border text-xs text-muted-foreground">
            <th scope="col" className="py-2 pr-4 font-medium">
              {t('help.shortcuts.keyColumn')}
            </th>
            <th scope="col" className="py-2 font-medium">
              {t('help.shortcuts.actionColumn')}
            </th>
          </tr>
        </thead>
        <tbody>
          {SHORTCUT_IDS.map((id) => (
            <tr key={id} className="border-b border-border last:border-b-0">
              <td className="w-28 py-2 pr-4 align-top">
                <kbd className="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-xs">
                  {t(shortcutKey(id, 'key'))}
                </kbd>
              </td>
              <td className="py-2">{t(shortcutKey(id, 'action'))}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <p className="text-muted-foreground">{t('help.shortcuts.helpNote')}</p>
    </Prose>
  );
}

/**
 * Questions that open to their answers. Native `<details>`: keyboard, screen readers and find-in-page
 * all work with no code of ours, and there is no state worth keeping about which ones are open.
 */
function Disclosures({
  items,
  testId,
}: {
  items: { id: string; summary: ResourceKey; body: ResourceKey }[];
  testId: string;
}) {
  const t = useT();

  return (
    <div className="flex max-w-3xl flex-col gap-2" data-testid={testId}>
      {items.map((item) => (
        <details
          key={item.id}
          data-testid={`${testId}-${item.id}`}
          className="group rounded-md border border-border bg-card px-4 py-3 text-sm"
        >
          <summary className="flex cursor-pointer list-none items-center gap-2 font-medium [&::-webkit-details-marker]:hidden">
            <ChevronRightIcon
              className="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-90"
              aria-hidden
            />
            {t(item.summary)}
          </summary>

          <Markdown text={t(item.body)} className="mt-3 gap-2 pl-6 leading-relaxed" />
        </details>
      ))}
    </div>
  );
}
