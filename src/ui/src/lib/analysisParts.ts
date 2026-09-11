import type { AnalysisOptions, AnalysisVerbosityLevel } from '@/contracts';
import type { ResourceKey } from '@/i18n/translate';

/** The switchable parts of an analysis — every field of `AnalysisOptions` but the verbosity. */
export type AnalysisPart = Exclude<keyof AnalysisOptions, 'verbosity'>;

export interface AnalysisPartInfo {
  readonly part: AnalysisPart;
  readonly label: ResourceKey;
  readonly body: ResourceKey;

  /** Kebab-case, for test ids and element ids. */
  readonly slug: string;
}

/** Which grouping the diagram can be drawn in, and whether interfaces can merge with implementations. */
export const GROUPING_PARTS: readonly AnalysisPartInfo[] = [
  {
    part: 'changeClusters',
    label: 'analysis.parts.changeClusters',
    body: 'analysis.parts.changeClustersBody',
    slug: 'change-clusters',
  },
  {
    part: 'implementationGroups',
    label: 'analysis.parts.implementationGroups',
    body: 'analysis.parts.implementationGroupsBody',
    slug: 'implementation-groups',
  },
];

/** What the model writes about what it found. */
export const CONTENT_PARTS: readonly AnalysisPartInfo[] = [
  {
    part: 'risks',
    label: 'analysis.parts.risks',
    body: 'analysis.parts.risksBody',
    slug: 'risks',
  },
  {
    part: 'nodeExplanations',
    label: 'analysis.parts.nodeExplanations',
    body: 'analysis.parts.nodeExplanationsBody',
    slug: 'node-explanations',
  },
  {
    part: 'edgeExplanations',
    label: 'analysis.parts.edgeExplanations',
    body: 'analysis.parts.edgeExplanationsBody',
    slug: 'edge-explanations',
  },
  {
    part: 'containerExplanations',
    label: 'analysis.parts.containerExplanations',
    body: 'analysis.parts.containerExplanationsBody',
    slug: 'container-explanations',
  },
];

export const ALL_PARTS: readonly AnalysisPartInfo[] = [...GROUPING_PARTS, ...CONTENT_PARTS];

export const VERBOSITY_LEVELS: readonly {
  readonly value: AnalysisVerbosityLevel;
  readonly label: ResourceKey;
  readonly body: ResourceKey;
}[] = [
  { value: 'brief', label: 'analysis.parts.verbosityBrief', body: 'analysis.parts.verbosityBriefBody' },
  { value: 'medium', label: 'analysis.parts.verbosityMedium', body: 'analysis.parts.verbosityMediumBody' },
  { value: 'detailed', label: 'analysis.parts.verbosityDetailed', body: 'analysis.parts.verbosityDetailedBody' },
];

/**
 * What a run asks for when the host has not said yet — the host's own default, every part at brief.
 * Only used until `analysis.getDefaults` answers; never sent in place of what the reviewer chose.
 */
export const FALLBACK_OPTIONS: AnalysisOptions = {
  changeClusters: true,
  implementationGroups: true,
  risks: true,
  nodeExplanations: true,
  edgeExplanations: true,
  containerExplanations: true,
  verbosity: 'brief',
};

/** How many parts are switched off. */
export function partsOff(options: AnalysisOptions): number {
  return ALL_PARTS.filter(({ part }) => !options[part]).length;
}

/** Whether two sets of options ask for exactly the same run. */
export function sameOptions(a: AnalysisOptions, b: AnalysisOptions): boolean {
  return a.verbosity === b.verbosity && ALL_PARTS.every(({ part }) => a[part] === b[part]);
}

/** The resource key naming a verbosity. */
export function verbosityLabel(verbosity: AnalysisVerbosityLevel): ResourceKey {
  return VERBOSITY_LEVELS.find((level) => level.value === verbosity)?.label ?? 'analysis.parts.verbosityBrief';
}
