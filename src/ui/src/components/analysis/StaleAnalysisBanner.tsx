import { useState } from 'react';
import { NetworkIcon, TriangleAlertIcon } from 'lucide-react';
import type { AnalysisFreshness, AnalysisView } from '@/contracts';
import { formatCount } from '@/i18n/format';
import type { ResourceKey } from '@/i18n/translate';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';

/**
 * Requirement 9's prompt: the working tree has moved since this analysis ran.
 *
 * Above the analysis rather than instead of it. What is on screen is still a faithful account of
 * the change as it was, and a reviewer halfway through it may well want to finish — but they are
 * told, with what moved, and one click from bringing it up to date. Silently showing it would be the
 * failure the requirement names.
 *
 * Dismissable for the session, and only for what it said: the dismissal remembers the analysis and
 * the difference it described, so a later check that finds something else has moved shows it again.
 */
export function StaleAnalysisBanner({
  view,
  onReanalyse,
}: {
  view: AnalysisView;
  onReanalyse(): void;
}) {
  const t = useT();
  const freshness = useAppStore((state) => state.analysisFreshness);
  const dismissed = useAppStore((state) => state.analysisStaleDismissed);
  const dismiss = useAppStore((state) => state.dismissStaleAnalysis);
  const [showFiles, setShowFiles] = useState(false);

  if (!freshness || !freshness.isStale || freshness.analysisId !== view.analysisId) return null;

  const signature = signatureOf(freshness);
  if (dismissed === signature) return null;

  return (
    <div className="shrink-0 px-6 pt-3">
      <Alert variant="warning" role="status" data-testid="stale-analysis-banner">
        <TriangleAlertIcon aria-hidden />
        <AlertTitle>{t('analysis.stale.heading')}</AlertTitle>
        <AlertDescription className="w-full">
          <p>{t('analysis.stale.body')}</p>

          <ul className="flex flex-col gap-0.5 text-xs" data-testid="stale-analysis-summary">
            {freshness.headMoved && (
              <li>
                {freshness.recordedHeadCommit && freshness.currentHeadCommit
                  ? t('analysis.stale.headMoved', {
                      from: freshness.recordedHeadCommit.slice(0, 8),
                      to: freshness.currentHeadCommit.slice(0, 8),
                    })
                  : t('analysis.stale.headMovedUnknown')}
              </li>
            )}
            {freshness.modifiedCount > 0 && (
              <li>{t('analysis.stale.modified', { count: formatCount(freshness.modifiedCount) })}</li>
            )}
            {freshness.addedCount > 0 && (
              <li>{t('analysis.stale.added', { count: formatCount(freshness.addedCount) })}</li>
            )}
            {freshness.removedCount > 0 && (
              <li>{t('analysis.stale.removed', { count: formatCount(freshness.removedCount) })}</li>
            )}
          </ul>

          {freshness.basis !== 'content' && (
            <p className="text-xs opacity-80">
              {freshness.basis === 'head_only'
                ? t('analysis.stale.weakerHeadOnly')
                : t('analysis.stale.weakerLineCounts')}
            </p>
          )}

          {showFiles && <FileLists view={view} freshness={freshness} />}

          <div className="mt-1 flex flex-wrap items-center gap-2">
            <Button size="sm" onClick={onReanalyse} data-testid="stale-analysis-reanalyse">
              <NetworkIcon aria-hidden />
              {t('analysis.stale.reanalyse')}
            </Button>

            {freshness.modifiedCount + freshness.addedCount + freshness.removedCount > 0 && (
              <Button size="sm" variant="outline" onClick={() => setShowFiles(!showFiles)} aria-expanded={showFiles}>
                {showFiles ? t('analysis.stale.hideFiles') : t('analysis.stale.showFiles')}
              </Button>
            )}

            <Button size="sm" variant="ghost" onClick={() => dismiss(signature)} data-testid="stale-analysis-dismiss">
              {t('analysis.stale.dismiss')}
            </Button>
          </div>
        </AlertDescription>
      </Alert>
    </div>
  );
}

/**
 * The paths themselves. A modified or removed path is a node on the diagram, so it opens there —
 * the reviewer can see what the explanation said and what the file now is. An added one is not in
 * the analysis at all, and says so by being plain text.
 */
function FileLists({ view, freshness }: { view: AnalysisView; freshness: AnalysisFreshness }) {
  const t = useT();
  const openDiffFor = useAppStore((state) => state.openDiffFor);

  const nodeFor = (path: string) => view.nodes.find((node) => node.filePath === path);

  const groups: { key: ResourceKey; paths: string[]; count: number }[] = [
    { key: 'analysis.stale.modified', paths: freshness.modifiedPaths, count: freshness.modifiedCount },
    { key: 'analysis.stale.added', paths: freshness.addedPaths, count: freshness.addedCount },
    { key: 'analysis.stale.removed', paths: freshness.removedPaths, count: freshness.removedCount },
  ];

  return (
    <div className="flex w-full flex-col gap-2 text-xs" data-testid="stale-analysis-files">
      {groups
        .filter((group) => group.count > 0)
        .map((group) => (
          <section key={group.key}>
            <h3 className="font-medium">{t(group.key, { count: formatCount(group.count) })}</h3>
            <ul className="max-h-32 overflow-y-auto pl-3 font-mono">
              {group.paths.map((path) => {
                const node = nodeFor(path);

                return (
                  <li key={path} className="break-all">
                    {node ? (
                      <button
                        type="button"
                        onClick={() => openDiffFor(node.id, node.containerId)}
                        className="text-left underline decoration-dotted underline-offset-2 hover:text-foreground"
                      >
                        {path}
                      </button>
                    ) : (
                      path
                    )}
                  </li>
                );
              })}
              {group.count > group.paths.length && (
                <li className="font-sans opacity-80">
                  {t('analysis.stale.more', { count: formatCount(group.count - group.paths.length) })}
                </li>
              )}
            </ul>
          </section>
        ))}
    </div>
  );
}

/** What a dismissal is of: this analysis, and this particular difference. */
export function signatureOf(freshness: AnalysisFreshness): string {
  return [
    freshness.analysisId,
    freshness.headMoved ? freshness.currentHeadCommit ?? 'moved' : '',
    freshness.modifiedPaths.join('|'),
    freshness.addedPaths.join('|'),
    freshness.removedPaths.join('|'),
    freshness.modifiedCount,
    freshness.addedCount,
    freshness.removedCount,
  ].join('\n');
}
