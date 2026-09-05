# Future improvements

Deliberately excluded from the 14-iteration plan. Recorded here so they are neither
forgotten nor accidentally built early.

**Nothing on this list is in scope.** If an iteration seems to need one of these, that is a
signal to stop and ask, not to build it.

---

### Branch and commit comparison

Branch vs branch, commit ranges, merge-base diffs. The current scope is **uncommitted local
changes only** — working tree vs `HEAD` — and invariant §0.2.11 makes that explicit. This is
the single most-likely-to-be-requested addition; it stays out until the core product works.

### PR platform integration

GitHub, GitLab, Bitbucket. Analyse a pull request directly and post the summary back. Blocked
on branch comparison above.

### Multi-pass analysis for very large changesets

Discover containers first, then detail each separately. §0.6 fixes the current design at
**single pass regardless of changeset size**. If a changeset genuinely cannot fit, Iteration 7
surfaces the context-overflow condition as an actionable error rather than degrading quietly.

### Semantic zoom

A high-level architectural view that expands into the file-level graph.

### Static-analysis tools for the LLM

Roslyn, tree-sitter and similar, offered as *additional, non-authoritative* tools to improve
edge precision. Iteration 5 explicitly leaves the seam and adds nothing. This would be the
first deliberate crack in the language-agnostic invariant (§0.2.3), so it needs a real
decision, not a drive-by.

### User annotations

Notes on nodes, containers and edges.

### Diagram export

PNG, SVG, PDF, HTML, JSON. Cheap once the analysis is self-contained — which it is from
Iteration 7 onward. Iteration 13 already ships result export/import as a file; this is the
visual counterpart.

### Generated review checklist

An ordered, actionable list derived from the reading order and the collected risks.

### Architectural impact analysis

Compare dependency structure before and after, and report structural changes.

### Web version

The persisted analysis is self-contained and the UI has no host-specific code, so this is a
build target rather than a rewrite. Preserving that property is a reason to keep the
JSON-RPC boundary clean and the WebView a pure renderer (§0.2.13).

### Incremental re-analysis

Update an existing graph when the working tree changes, instead of regenerating. §0.6 fixes
the current behaviour: **re-analysis is always the whole changeset**; partial re-analysis does
not exist.

### Localisation

The resource layer exists from Iteration 1 and no UI string is hardcoded, so only
translations are missing.

### First-class keyboard navigation

Iteration 10 adds shortcuts where they are cheap. Full keyboard-driven review is a separate
piece of work.
