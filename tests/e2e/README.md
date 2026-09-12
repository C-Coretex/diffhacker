# End-to-end tests

These drive **the real application**: the real Photino window, the real .NET host, the real
`git` command line, and real repositories on disk. Nothing is stubbed. The unit suites already
prove each layer in isolation; what they cannot prove is that the layers are wired together, and
that is the whole job of this directory.

This is the whole-application gate. It replaced Iteration 1's `--self-test` mode, which verified
the bridge from inside the page and shipped that test code in the production bundle — the two
checks it made that nothing else did are now in
[04-shell-guarantees.spec.ts](specs/04-shell-guarantees.spec.ts).

## Running them

```bash
dotnet build src/DiffHacker.slnx     # the suite launches the built host, it does not build it
cd tests/e2e
npm install                          # once
npm test
```

Then `npm run report` for the HTML report, or look at `artifacts/screenshots/<test>/` — every
journey is captured as a numbered sequence of PNGs you can flip through.

Useful switches:

| | |
|---|---|
| One spec | `npx playwright test specs/01-review-journey.spec.ts` |
| A Release build | `DIFFHACKER_CONFIGURATION=Release npm test` |
| Some other binary | `DIFFHACKER_HOST_EXE=/path/to/DiffHacker.Host.exe npm test` |
| Refresh the user guide's screenshots | `npm run docs:screenshots`, then rebuild the solution |

`docs:screenshots` is spec 15's guide journey with `--update-snapshots`. It overwrites the PNGs in
`src/ui/src/help/screenshots/`, which the Help screen bundles and `docs/user-guide.md` links to. A
normal run writes a guide screenshot into the source tree only when the file is missing, and leaves
every other one in `artifacts/guide-screenshots/`.

## How it attaches to the window

The renderer runs in WebView2, which is Chromium, so Playwright attaches over the Chrome
DevTools Protocol. Two things make that work:

- WebView2 reads `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` from the environment and
  `PhotinoAppShell` never overrides it, so the harness asks for a debugging port without any
  production code knowing this suite exists.
- The host is launched with `--data-dir`, pointing at a throwaway directory.

That second one is not a convenience. .NET resolves the per-user application data directory
through the Win32 known-folder API, which **ignores `LOCALAPPDATA`** — so on Windows there is no
environment variable a harness could redirect, and without the switch these tests would write
their throwaway providers and API keys into your real secret store.

The same trap applies to WebView2's own browser profile. PhotinoX resolves its folder through the
known-folder API too, so redirecting `LOCALAPPDATA` alone left every launch sharing your real
`%LOCALAPPDATA%\PhotinoX\EBWebView`, and its `localStorage` with it. A theme, merge toggle or
dismissed guide offer set by one test was still set in the next one, and in your own window
afterwards. The harness therefore also sets `WEBVIEW2_USER_DATA_FOLDER`, which WebView2 honours over
whatever folder the host asks for. Each app gets a fresh profile, and a restart given the same root
finds the same one.

**Windows only.** CDP is a WebView2 thing; on macOS and Linux the shell is WKWebView and
WebKitGTK, which expose no equivalent. The suite skips there rather than reporting a pass it did
not earn.

## Conventions

- **No fixed sleeps.** Every wait is a condition — a Playwright locator assertion, or a poll of
  the debugging endpoint. The window is up in a few hundred milliseconds and the whole suite runs
  in about twenty seconds; a sleep long enough to be safe on a slow machine would be dead time on
  every launch.
- **One test is one journey.** Specs walk several screens in sequence because each step depends
  on the state the last one left behind, and relaunching a desktop application between assertions
  would cost far more than it proved.
- **Assertions come from the application's own catalogue.** `src/strings.ts` imports
  `src/ui/src/i18n/en.ts` rather than pasting English copy, so these tests check that the right
  *resource* was rendered and a deleted key is a compile error here too.
- **Roles and accessible names, never test ids.** Two traps: `getByText` matches substrings by
  default and the catalogue genuinely overlaps — "Uncommitted changes" also appears inside the
  welcome card's description — so use `exact: true` for anything short. And `CardTitle` renders a
  `<div>`, so `getByRole('heading')` only works for panels using a real `<h2>`.
- **`workers: 1`**, because each test takes over a desktop window and the foreground.

## Layout

```
specs/
  01-review-journey.spec.ts              welcome → changeset → diffs → settings → recents
  02-awkward-repositories.spec.ts        clean, no commits, not a repo, bare, large, no git
  03-settings-secrets-and-restart.spec.ts  providers, the promises about API keys, restart
  04-shell-guarantees.spec.ts            CSP enforcement, in-process serving, the handshake
  05-repository-profile.spec.ts          profiling a repository, editing it, writing its docs
  06-analysis-pipeline.spec.ts           analysing, repairing, failing, cancelling, five hundred files
  07-graph-rendering.spec.ts             the diagram: searched, collapsed, dragged, explained
  08-explanations.spec.ts                explanation cards opened by click, the risk register
  09-diff-review.spec.ts                 Monaco, every awkward file kind, navigation, reviewed marks,
                                         the controls on a box, a cluster opened whole, full screen
  10-grouping-modes.spec.ts              two groupings of one analysis, switching for free, marks
                                         that survive it, and the opt-out proved at the wire
  11-implementation-groups.spec.ts       abstractions drawn with their implementations as one box
  12-run-library.spec.ts                 the live run strip, the tool-call inspector, earlier runs
                                         reopened for free, and a stale analysis said to be one
  13-budget-limits.spec.ts               a run pausing at a configured limit and asking to continue
  14-analysis-parts.spec.ts              parts switched off in Settings leave the request, the
                                         screen leaves them out, and a run override is forgotten
  15-user-guide.spec.ts                  the Help guide followed for real, each step photographed,
                                         and Help reachable from every screen with every image loaded
  16-delete-all-local-data.spec.ts       the database and a stored key erased, the window closing
                                         itself, and a fresh launch starting clean
src/
  appHarness.ts    launches the host, attaches over CDP, screenshots, tears down
  gitFixture.ts    builds real repositories in temp directories
  guideFixture.ts  the change the user guide shows, and the answer a good model gives about it
  guideCamera.ts   the guide's screenshots: fixed size, paths redacted, written where they belong
  screens.ts       locators, one class per screen
  fixtures.ts      the `test` object with `diffhacker` and `repos`
  strings.ts       the application's catalogue, imported
  stubProvider.ts  a scripted OpenAI-compatible endpoint, and the documents it answers with
  globalSetup.ts   sweeps temp directories earlier runs could not delete
```
