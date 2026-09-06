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
    nav: {
      settings: 'Settings',
      back: 'Back',
    },
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
  environment: {
    checking: 'Checking your environment…',
    gitMissingHeading: 'Git was not found',
    gitMissingBody:
      'DiffHacker reads your working tree through the git command line and cannot do anything without it. Install git, make sure it is on your PATH, then restart DiffHacker.',
    gitMissingHint: 'On most systems `git --version` in a terminal is the quickest way to check.',
    secretBackend: 'API keys are protected by',
    backend: {
      windows_dpapi: 'Windows DPAPI, tied to your user account',
      macos_keychain: 'the macOS Keychain',
      linux_libsecret: 'your system keyring, through libsecret',
      machine_derived: 'a key derived from this machine',
    },
    fallbackWarning:
      'No system keyring was available, so keys are encrypted with a key derived from this machine and your user account. That protects the file if it is copied elsewhere, but not from software already running as you.',
  },
  welcome: {
    heading: 'Open a repository',
    description:
      'Point DiffHacker at a local repository. It reviews your uncommitted changes — the working tree against HEAD.',
    browse: 'Choose a folder…',
    browsing: 'Waiting for the folder picker…',
    pickerTitle: 'Choose a repository',
    pathLabel: 'Or type a path',
    pathPlaceholder: 'C:\\path\\to\\repository',
    open: 'Open',
    opening: 'Opening…',
    recentHeading: 'Recent repositories',
    recentEmpty: 'Nothing here yet. The repositories you open will be listed for one-click access.',
    recentLoading: 'Loading your recent repositories…',
    unavailable: 'Missing',
    unavailableHint: 'This folder is gone or is no longer a git repository.',
    forget: 'Remove',
    forgetLabel: 'Remove {name} from the list',
    lastOpened: 'Last opened {when}',
    normalized: 'You picked a folder inside the repository, so DiffHacker opened {path} instead.',
  },
  repository: {
    change: 'Change repository',
    path: 'Path',
    noCommits:
      'This repository has no commits yet, so there is no HEAD to compare your working tree against.',
    linkedWorktree: 'This is a linked worktree.',
  },
  changeset: {
    heading: 'Uncommitted changes',
    description: 'Your working tree compared against HEAD — staged and unstaged together.',
    loading: 'Reading your working tree…',
    refresh: 'Refresh',
    includeUntracked: 'Include new files git does not track yet',
    cleanHeading: 'Nothing to review',
    cleanBody: 'Your working tree matches HEAD. Make a change and refresh.',
    cleanBodyUntrackedExcluded:
      'Your working tree matches HEAD, apart from any new files. Turn on untracked files to see those.',
    noCommitsNotice:
      'This repository has no commits, so everything in your working tree reads as newly added.',
    summary: '{files} files · +{added} −{removed}',
    summaryDetail: '{languages} · {projects}',
    hunkCountsUnavailable:
      'Hunk counts could not be attributed to files on this run, so they are not shown.',
    showMore: 'Show {count} more',
    showingCount: 'Showing {shown} of {total}',
    columnFile: 'File',
    columnChange: 'Change',
    renamedFrom: 'was {path}',
    binary: 'Binary',
    submodule: 'Submodule',
    symlink: 'Symlink',
    untracked: 'New',
    nestedRepository: 'Nested repository',
    noLineCounts: 'not counted',
    status: {
      added: 'Added',
      modified: 'Modified',
      deleted: 'Deleted',
      renamed: 'Renamed',
      copied: 'Copied',
    },
    diff: {
      show: 'Show diff',
      hide: 'Hide diff',
      loading: 'Loading the diff…',
      binary: 'This file is binary, so there is no text diff to show.',
      absent: 'There is no diff to show for this file.',
      tooLarge: 'This diff is {size} and is too large to display here.',
    },
  },
  providers: {
    heading: 'LLM providers',
    description:
      'DiffHacker uses your own API key. Keys are stored in your operating system’s secret store and never leave the .NET host.',
    empty: 'No provider configured yet. Add one to run an analysis.',
    loading: 'Loading your providers…',
    add: 'Add a provider',
    edit: 'Edit',
    remove: 'Remove',
    removeConfirm: 'Remove {name}? Its stored API key is deleted too. This cannot be undone.',
    removeConfirmTitle: 'Remove this provider?',
    cancel: 'Cancel',
    save: 'Save',
    saving: 'Saving…',
    active: 'Active',
    makeActive: 'Use this one',
    activeHint: 'The provider analysis runs will use.',
    noKey: 'No API key stored',
    keyStored: 'API key stored',
    typeLabel: 'Provider',
    nameLabel: 'Name',
    namePlaceholder: 'Work account',
    modelLabel: 'Model',
    modelPlaceholder: 'Type the model identifier',
    modelHint:
      'Free text on purpose — hardcoded model lists go stale. Test the connection and DiffHacker will suggest the models your key can reach.',
    baseUrlLabel: 'Base URL',
    baseUrlOptional: 'Base URL (optional)',
    baseUrlPlaceholder: 'https://example.com/v1',
    baseUrlHint: 'Leave blank to use the provider’s standard endpoint.',
    baseUrlRequiredHint: 'Required: the endpoint your OpenAI-compatible server listens on.',
    apiKeyLabel: 'API key',
    apiKeyPlaceholder: 'Paste your key',
    apiKeyUnchanged: 'Leave blank to keep the key already stored.',
    test: 'Test connection',
    testing: 'Testing…',
    testFree:
      'Lists the models your key can reach. No tokens are used, so this costs nothing.',
    testSucceeded: 'Connected. {count} model(s) available to this key.',
    testSucceededNoModels: 'Connected. This provider did not return a model list.',
    testModelMissing:
      'Connected, but “{model}” is not among the {count} models this key can reach. Check the spelling.',
    testFailed: 'The connection failed.',
    providerSaid: 'The provider said:',
    httpStatus: 'HTTP {status}',
    type: {
      openai: 'OpenAI',
      anthropic: 'Anthropic',
      gemini: 'Google Gemini',
      grok: 'Grok (xAI)',
      deepseek: 'DeepSeek',
      openai_compatible: 'OpenAI-compatible endpoint',
    },
  },
  theme: {
    light: 'Light',
    dark: 'Dark',
  },
  error: {
    unknown_error: 'Something went wrong. See log.txt for details.',
    rpc_timeout: 'The host did not respond in time.',

    git_not_found:
      'Git was not found on your PATH. DiffHacker cannot read a repository without it.',
    repository_not_found: 'There is no folder at {path}.',
    repository_not_a_git_repository: '{path} is not a git repository, and is not inside one.',
    repository_is_bare:
      '{path} is a bare repository. It has no working tree, and DiffHacker reviews uncommitted changes in a working tree.',
    repository_access_denied: '{path} could not be read. Check the folder’s permissions.',
    folder_picker_unavailable:
      'The folder picker could not be opened. Type the path instead.',

    changeset_repository_unreadable:
      '{path} could not be read as a git working tree. It may have been moved or deleted.',
    changeset_git_failed: 'Git could not read the changes in {path}. See log.txt for details.',

    provider_not_found: 'That provider is no longer configured.',
    provider_key_missing: 'No API key is stored for this provider. Add one and try again.',
    provider_model_required: 'Enter a model identifier.',
    provider_base_url_required: 'An OpenAI-compatible endpoint needs a base URL.',
    provider_invalid_base_url: 'That base URL is not a valid absolute URL.',

    secret_store_unavailable: 'Your API keys could not be read. See log.txt for details.',
    settings_store_unavailable: 'Your settings could not be read. See log.txt for details.',
  },
  testFailure: {
    provider_invalid_key: 'The provider rejected the API key.',
    provider_forbidden: 'The key is valid but not allowed to do this.',
    provider_quota_exhausted: 'This account has no credit or quota left.',
    provider_rate_limited: 'The provider is rate-limiting this key. Try again shortly.',
    provider_endpoint_not_found: 'Nothing answered at that endpoint. Check the base URL.',
    provider_unreachable: 'The provider could not be reached. Check the URL and your connection.',
    provider_timed_out: 'The provider did not answer in time.',
    provider_unexpected_response: 'The provider returned an unexpected response.',
  },
} as const;

export type Catalogue = typeof en;
