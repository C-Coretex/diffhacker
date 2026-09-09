import { useMemo, useState } from 'react';
import { Search, X } from 'lucide-react';
import type { AnalysisView } from '@/contracts';
import { useT } from '@/i18n/useT';
import { Input } from '@/components/ui/input';
import { searchGraph, type SearchField, type SearchHit } from '@/graph/search';
import { useAppStore } from '@/store/appStore';

/**
 * Find a file on the diagram (requirements 10 and 13).
 *
 * Selecting a hit does three things, and all three are needed for "find" to mean anything on a
 * three-hundred-node canvas: it expands the hit's container if it was collapsed, it centres the
 * viewport on the node, and it rings the node so the eye lands on it after the pan.
 *
 * Results are grouped by what matched, so a hit on a cluster title is never mistaken for a file
 * that happens to be called that.
 */
export function NodeSearch({
  view,
  onSelect,
}: {
  view: AnalysisView;
  onSelect: (nodeId: string) => void;
}) {
  const t = useT();
  const query = useAppStore((state) => state.graphSearch);
  const setQuery = useAppStore((state) => state.setGraphSearch);
  const [open, setOpen] = useState(false);

  const hits = useMemo(() => searchGraph(view, query), [view, query]);
  const groups = useMemo(() => groupHits(hits), [hits]);

  const choose = (hit: SearchHit): void => {
    onSelect(hit.nodeId);
    setOpen(false);
  };

  return (
    <div className="relative w-72">
      <Search
        className="pointer-events-none absolute left-2 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
        aria-hidden
      />

      <Input
        type="search"
        value={query}
        placeholder={t('analysis.graph.searchPlaceholder')}
        aria-label={t('analysis.graph.searchLabel')}
        className="pl-8"
        onChange={(event) => {
          setQuery(event.target.value);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setOpen(false);
            return;
          }

          // Enter takes the first hit. The common case is typing enough of a filename to make it
          // unique, and reaching for the mouse to confirm what is already the only answer is work.
          if (event.key === 'Enter' && hits[0] !== undefined) {
            event.preventDefault();
            choose(hits[0]);
          }
        }}
      />

      {query.length > 0 && (
        <button
          type="button"
          onClick={() => setQuery('')}
          aria-label={t('analysis.graph.searchClear')}
          className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
        >
          <X className="size-4" aria-hidden />
        </button>
      )}

      {open && query.trim().length > 0 && (
        <div className="absolute z-20 mt-1 max-h-80 w-full overflow-auto rounded-md border border-border bg-popover p-1 text-popover-foreground shadow-md">
          {hits.length === 0 ? (
            <p className="px-2 py-3 text-xs text-muted-foreground">
              {t('analysis.graph.searchNoResults', { query })}
            </p>
          ) : (
            groups.map(([field, group]) => (
              <section key={field}>
                <h4 className="px-2 pb-0.5 pt-2 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                  {t(fieldLabels[field])}
                </h4>
                <ul>
                  {group.map((hit) => (
                    <li key={`${hit.field}:${hit.nodeId}`}>
                      <button
                        type="button"
                        onClick={() => choose(hit)}
                        className="w-full rounded px-2 py-1 text-left hover:bg-accent"
                      >
                        <span className="block truncate text-xs font-medium">{hit.label}</span>
                        <span className="block truncate text-[10px] text-muted-foreground">
                          {hit.detail}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </section>
            ))
          )}
        </div>
      )}
    </div>
  );
}

const fieldLabels = {
  path: 'analysis.graph.searchByPath',
  nodeTitle: 'analysis.graph.searchByNodeTitle',
  containerTitle: 'analysis.graph.searchByContainerTitle',
} as const;

/** Groups in a fixed order, so the list does not reshuffle as the query is typed. */
function groupHits(hits: readonly SearchHit[]): [SearchField, SearchHit[]][] {
  const order: SearchField[] = ['path', 'nodeTitle', 'containerTitle'];

  return order
    .map((field): [SearchField, SearchHit[]] => [field, hits.filter((hit) => hit.field === field)])
    .filter(([, group]) => group.length > 0);
}
