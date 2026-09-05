/**
 * The string catalogue.
 *
 * CLAUDE.md §0.6: DiffHacker ships English only, but no user-facing string is hardcoded at a
 * call site. .NET resources cannot reach the WebView, so this is the single resource layer for
 * the whole application — the host sends error codes and resource keys, never sentences.
 *
 * `as const` makes every key path checkable at compile time, so a typo is a build failure
 * rather than a blank label at run time.
 */
export const en = {
  app: {
    title: 'DiffHacker',
    tagline: 'Review large Git changes as a graph, not an alphabetical file list.',
    placeholder:
      'No repository is open yet. Choosing a repository and configuring a provider arrives in the next iteration.',
  },
  host: {
    heading: 'Host connection',
    connecting: 'Connecting to the host…',
    detached:
      'Running outside the DiffHacker window, so there is no host to talk to. Launch the app with `dotnet run --project src/DiffHacker.Host`.',
    appVersion: 'Version',
    platform: 'Platform',
    architecture: 'Architecture',
    os: 'Operating system',
    contract: 'Contract',
    started: 'Started',
    contractMismatch:
      'The host was built against contract {host} but this interface expects {ui}. Rebuild the solution.',
  },
  demo: {
    heading: 'Bridge demonstration',
    description:
      'Calls a .NET method over JSON-RPC, then receives a stream of notifications pushed back from the host.',
    run: 'Run the round trip',
    running: 'Running…',
    step: 'Processing step {step} of {total}',
    done: 'Stream complete: {count} notification(s) received.',
    idle: 'Not started.',
  },
  theme: {
    light: 'Light',
    dark: 'Dark',
  },
  error: {
    unknown_error: 'Something went wrong. See log.txt for details.',
    rpc_timeout: 'The host did not respond in time.',
    demo_steps_out_of_range: 'The host rejected a step count of {steps}.',
    self_test_not_requested: 'The host was not started in self-test mode.',
  },
} as const;

export type Catalogue = typeof en;
