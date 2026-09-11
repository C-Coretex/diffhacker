import { useState } from 'react';
import { useT } from '@/i18n/useT';
import { PROJECT_COLOURS, projectColourStyle, type ProjectColour } from '@/graph/palette';

/**
 * What the shapes and colours on the diagram mean.
 *
 * Requirements 4 and 6 both ask for a legend, and this is one panel rather than two because a
 * reviewer looking something up does not know in advance which of the two questions they have.
 *
 * The project list is the part that can grow without bound. Ten projects get a colour; the rest
 * share the neutral one and are collapsed behind a disclosure — a legend of forty swatches is a
 * legend nobody reads, and past ten hues the swatches stop being distinguishable anyway.
 */
export function GraphLegend({ colours }: { colours: readonly ProjectColour[] }) {
  const t = useT();
  const [showRest, setShowRest] = useState(false);

  const coloured = colours.filter((entry) => entry.slot !== null);
  const rest = colours.filter((entry) => entry.slot === null);

  return (
    <div className="w-72 space-y-4 text-xs">
      <section>
        <h3 className="mb-2 font-semibold">{t('analysis.graph.legendEdges')}</h3>
        <ul className="space-y-1.5 text-muted-foreground">
          <LegendLine sample={<Line />} label={t('analysis.graph.legendDirect')} />
          <LegendLine sample={<Line dashed />} label={t('analysis.graph.legendConceptual')} />
          <LegendLine sample={<Line faint />} label={t('analysis.graph.legendCrossContainer')} />
          <LegendLine sample={<Line bundle />} label={t('analysis.graph.legendBundle')} />
        </ul>
      </section>

      <section>
        <h3 className="mb-2 font-semibold">{t('analysis.graph.legendStates')}</h3>
        <ul className="space-y-1.5 text-muted-foreground">
          <LegendLine sample={<Box className="border-solid" />} label={t('analysis.state.changed')} />
          <LegendLine sample={<Box className="border-dashed" />} label={t('analysis.state.added')} />
          <LegendLine sample={<Box className="border-dotted" />} label={t('analysis.state.deleted')} />
          <LegendLine
            sample={<Box className="border-double border-4" />}
            label={t('analysis.state.unchanged_relevant')}
          />
          <LegendLine
            sample={<Box className="border-solid border-destructive" />}
            label={t('analysis.state.risky')}
          />
          <LegendLine sample={<span aria-hidden>▲</span>} label={t('analysis.state.entry_point')} />
          <LegendLine sample={<MergedBox />} label={t('analysis.graph.merged.legend')} />
        </ul>
        <p className="mt-2 text-[11px] text-muted-foreground">{t('analysis.graph.legendStatesNote')}</p>
      </section>

      {/*
        Iteration 9's sixth channel. A reviewer who notices that some boxes are fainter than others
        will otherwise guess at why, and the likeliest guess — that the faint ones are somehow less
        real, or filtered out — is exactly the one §0.2.5 says is wrong.
      */}
      <section>
        <h3 className="mb-2 font-semibold">{t('analysis.graph.legendImportance')}</h3>
        <ul className="space-y-1.5 text-muted-foreground">
          <LegendLine
            sample={<Box className="border-solid font-bold" />}
            label={t('analysis.graph.legendImportanceHigh')}
          />
          <LegendLine
            sample={<Box className="border-solid" />}
            label={t('analysis.graph.legendImportanceNormal')}
          />
          <LegendLine
            sample={<Box className="border-solid opacity-70" />}
            label={t('analysis.graph.legendImportanceLow')}
          />
        </ul>
        <p className="mt-2 text-[11px] text-muted-foreground">
          {t('analysis.graph.legendImportanceNote')}
        </p>
      </section>

      <section>
        <h3 className="mb-2 font-semibold">{t('analysis.graph.legendProjects')}</h3>

        {coloured.length === 0 ? (
          <p className="text-muted-foreground">{t('analysis.graph.legendNoProjects')}</p>
        ) : (
          <ul className="space-y-1 text-muted-foreground">
            {coloured.map((entry) => (
              <li key={entry.project} className="flex items-center gap-2">
                <Swatch slot={entry.slot} />
                <span className="min-w-0 flex-1 truncate" title={entry.project}>
                  {entry.project}
                </span>
                <span className="tabular-nums">{entry.nodeCount}</span>
              </li>
            ))}
          </ul>
        )}

        {rest.length > 0 && (
          <div className="mt-2">
            <button
              type="button"
              onClick={() => setShowRest((open) => !open)}
              className="flex w-full items-center gap-2 text-left text-muted-foreground hover:text-foreground"
              aria-expanded={showRest}
            >
              <Swatch slot={null} />
              <span className="flex-1">
                {t('analysis.graph.legendOtherProjects', { count: rest.length })}
              </span>
            </button>

            {showRest && (
              <ul className="mt-1 space-y-1 pl-6 text-muted-foreground">
                {rest.map((entry) => (
                  <li key={entry.project} className="flex items-center gap-2">
                    <span className="min-w-0 flex-1 truncate" title={entry.project}>
                      {entry.project}
                    </span>
                    <span className="tabular-nums">{entry.nodeCount}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}

        {coloured.length === PROJECT_COLOURS && rest.length > 0 && (
          <p className="mt-2 text-[11px] text-muted-foreground">
            {t('analysis.graph.legendPaletteFull', { count: PROJECT_COLOURS })}
          </p>
        )}
      </section>
    </div>
  );
}

function LegendLine({ sample, label }: { sample: React.ReactNode; label: string }) {
  return (
    <li className="flex items-center gap-2">
      <span className="flex w-10 shrink-0 justify-center">{sample}</span>
      <span>{label}</span>
    </li>
  );
}

function Line({ dashed, faint, bundle }: { dashed?: boolean; faint?: boolean; bundle?: boolean }) {
  return (
    <svg width="36" height="8" aria-hidden>
      <line
        x1="0"
        y1="4"
        x2="36"
        y2="4"
        stroke="var(--muted-foreground)"
        strokeWidth={bundle ? 2.5 : faint ? 1 : 1.5}
        strokeOpacity={faint || bundle ? 0.4 : 1}
        strokeDasharray={dashed ? '6 4' : faint ? '4 6' : undefined}
      />
    </svg>
  );
}

function Box({ className }: { className: string }) {
  return <span aria-hidden className={`block h-4 w-8 rounded-sm border-2 ${className}`} />;
}

/** A frame holding two rows, the way an abstraction and its implementation are drawn together. */
function MergedBox() {
  return (
    <span aria-hidden className="flex w-8 flex-col gap-px rounded-sm border-2 p-px">
      <span className="block h-1.5 rounded-[1px] border border-solid" />
      <span className="block h-1.5 rounded-[1px] border border-solid" />
    </span>
  );
}

function Swatch({ slot }: { slot: number | null }) {
  const colour = projectColourStyle(slot);
  return (
    <span
      aria-hidden
      className="size-3 shrink-0 rounded-sm border"
      style={{ background: colour.fill, borderColor: colour.rail }}
    />
  );
}
