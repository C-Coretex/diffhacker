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
their throwaway providers and API keys into your real secret store. `LOCALAPPDATA` *is* still
redirected, for a different reason: it is where WebView2 keeps its own browser profile, and a
fresh one per app keeps one test's page state out of the next.

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
src/
  appHarness.ts    launches the host, attaches over CDP, screenshots, tears down
  gitFixture.ts    builds real repositories in temp directories
  screens.ts       locators, one class per screen
  fixtures.ts      the `test` object with `diffhacker` and `repos`
  strings.ts       the application's catalogue, imported
  globalSetup.ts   sweeps temp directories earlier runs could not delete
```
