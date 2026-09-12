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
      profile: 'Repository profile',
      analysis: 'Analysis',
      help: 'Help',
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
    pricingLegend: 'Token prices (optional)',
    pricingHint:
      'DiffHacker ships a price table, but it is a snapshot and goes stale. Fill both boxes in to price this model yourself. Leave them blank and DiffHacker uses the table, or reports the cost as unknown.',
    inputCostLabel: 'Input, $ per million tokens',
    outputCostLabel: 'Output, $ per million tokens',
    costPlaceholder: '0.00',
    contextLegend: 'Context window (optional)',
    contextHint:
      'How large this model’s context is, shown live during a run so you can see a long exploration filling up. DiffHacker ships a table of these and it goes stale, so you can say. Leave it blank to use the table, or to leave the size unknown. It is never a limit: no run is stopped for exceeding it.',
    contextWindowLabel: 'Context window, in tokens',
    contextWindowPlaceholder: '200000',
    budgetLegend: 'Run limits (optional)',
    budgetHint:
      'How much a single run on this model may spend before it pauses to ask whether to keep going, rather than failing outright. Leave either blank to use the default (500 tool calls, 10,000,000 tokens).',
    maxToolCallsLabel: 'Max tool calls',
    maxToolCallsPlaceholder: '500',
    maxTotalTokensLabel: 'Max tokens (input + output)',
    maxTotalTokensPlaceholder: '10000000',
    testFailed: 'The connection failed.',
    providerSaid: 'The provider said:',
    httpStatus: 'HTTP {status}',
    noProviderHeading: 'No LLM provider configured',
    noProviderBody:
      'DiffHacker needs your own API key to analyze changes or generate a repository profile.',
    noProviderAction: 'Add a provider',
    noProviderDismiss: 'Dismiss',
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
    label: 'Colour scheme',
    system: 'Follow the system',
    light: 'Light',
    dark: 'Dark',
  },

  /**
   * External editors. DiffHacker discovers VS Code and Visual Studio itself; the two fields here are
   * for everyone else, which is the whole of what "the external editor command is user-configurable"
   * has to mean once the common cases need no configuring.
   */
  editors: {
    heading: 'External editor',
    description:
      'DiffHacker opens a file in your own editor when you want to leave the diff panel. VS Code and Visual Studio are found automatically; anything else needs a command here.',
    detected: 'Found on this machine',
    detectedNone: 'Neither VS Code nor Visual Studio was found on this machine.',
    vsCode: 'VS Code',
    visualStudio: 'Visual Studio',

    diffLabel: 'Command to compare two files',
    diffHint:
      'The first word is the program; {left} and {right} are the two files. No shell runs it, so quotes and semicolons are ordinary characters.',
    diffPlaceholder: 'meld {left} {right}',

    openLabel: 'Command to open one file',
    openHint:
      'Used where there is nothing to compare — an added file has no committed side. {file} is the path and {line} the line to land on.',
    openPlaceholder: 'subl {file}:{line}',

    save: 'Save editor commands',
    saving: 'Saving…',
    saved: 'Saved.',
  },

  dataManagement: {
    heading: 'Delete all local data',
    description:
      'Removes everything DiffHacker has stored on this machine: every analysis, your provider profiles and their API keys, cached diffs, and the log files. Nothing in any repository you have reviewed is touched.',
    deleteButton: 'Delete all local data',
    confirmTitle: 'Delete all local data?',
    confirmBody:
      'This permanently deletes every stored analysis, provider profile, API key and log file. It cannot be undone. DiffHacker closes immediately afterward — reopen it to keep using the app.',
    confirmCancel: 'Cancel',
    confirmAction: 'Delete everything',
    deleting: 'Deleting…',
    done: 'All local data deleted. DiffHacker is closing now — reopen it when you are ready.',
  },

  /**
   * What the model is doing during a run.
   *
   * Nothing renders these yet — Iteration 5 built the toolbox and the notification channel, but
   * no screen runs an analysis until Iteration 7. They live here for the same reason `runFailure`
   * does: the phase names are already the contract (`AnalysisProgressPhase`), and a phase with no
   * string would reach a reader as `analysing`.
   *
   * Note what is deliberately absent: the message itself. That is the model's own words, produced
   * during the run, and it is shown as written. §0.6 keeps host-authored prose out of the host,
   * not run data out of the screen.
   */
  progress: {
    phase: {
      exploring: 'Exploring the repository',
      analysing: 'Analysing the change',
      grouping: 'Grouping related changes',
      explaining: 'Writing explanations',
      finishing: 'Finishing up',
    },
  },

  /**
   * The repository knowledge base: what DiffHacker knows about a repository before it ever sees
   * a diff.
   */
  profile: {
    heading: 'Repository profile',
    description:
      'Standing knowledge about this repository, produced once and reused by every review. A review without one is guesswork about what the code is for.',
    missingHeading: 'No profile yet',
    missingBody:
      'Reviews of this repository will be weaker without one: the model will have to work out what the project is from the diff alone, every time.',
    generate: 'Analyse repository',
    regenerate: 'Analyse again',
    generating: 'Analysing…',
    cancel: 'Stop',
    cancelled: 'The run was stopped. Nothing was saved.',
    delete: 'Forget this profile',
    deleteConfirmTitle: 'Forget everything about this repository?',
    deleteConfirmBody:
      'This removes the generated profile, your notes, your instructions and your withheld-file list. It cannot be undone.',
    deleteConfirm: 'Forget it',
    keep: 'Keep it',

    generatedFrom: 'Generated {date} from commit {commit} by {model}',
    generatedFromNoCommit: 'Generated {date} by {model}',
    budget: '{count} of {budget} characters',
    budgetExceeded: 'Over the {budget}-character budget. Shorten a section, or raise the budget.',
    userSectionSize: 'Your notes and instructions add {count} characters to every review.',

    driftHeading: 'This repository has moved on',
    driftBody:
      '{changed} of {tracked} files differ from the commit this profile was made at. Analysing again will be more accurate.',
    driftUnreachable:
      'The commit this profile was made at is no longer in the repository, so there is no telling how stale it is. Analysing again is the only way to be sure.',

    generatedSections: 'What the model found',
    generatedWarning:
      'Analysing again replaces everything in this section. Your notes and instructions below are never touched.',
    purpose: 'Purpose',
    architecture: 'Architecture',
    modules: 'Modules',
    moduleName: 'Name',
    modulePath: 'Path',
    moduleSummary: 'What it does',
    moduleRelated: 'Related to',
    moduleRelatedHint: 'Comma-separated module names',
    addModule: 'Add a module',
    removeModule: 'Remove',
    layering: 'Layering',
    patterns: 'Patterns and conventions',
    entryPoints: 'Entry points',
    entryPointPath: 'File',
    entryPointPurpose: 'What starts here',
    addEntryPoint: 'Add an entry point',
    removeEntryPoint: 'Remove',
    testLayout: 'Tests',
    documentationRead: 'Documentation read: {files}',
    noDocumentationRead: 'No repository documentation was read.',

    userSections: 'Yours',
    userSectionsHint: 'Everything here survives analysing again, exactly as you typed it.',
    userNotes: 'Your notes',
    userNotesHint: 'What you know about this repository that the model got wrong or missed.',
    customInstructions: 'Standing instructions',
    customInstructionsHint:
      'Sent with every review of this repository. “This is CQRS.” “Ignore the generated/ folder.” “The Legacy project is being deleted.”',
    budgetLabel: 'Size budget',
    budgetHint:
      'Characters the generated profile may use. Every review of this repository pays for it, on every turn.',

    withheld: 'Files never read',
    withheldHint:
      'Files matching these patterns are listed but never opened, so their contents cannot reach a model. The built-in list covers credentials and key material; add your own below, one per line.',
    withheldBuiltIn: 'Built in',
    withheldCustom: 'Your patterns',

    save: 'Save',
    saving: 'Saving…',
    saved: 'Saved.',
  },

  /** The live view of a run: what the model says, and what it is actually doing. */
  toolLog: {
    heading: 'What it is doing',
    waiting: 'Starting…',
    turn: 'Turn {turn}',
    tokens: '{input} in / {output} out',
    cost: '${cost}',
    costUnknown: 'cost unknown',
    running: 'running…',
    failed: 'failed',
    bytes: '{bytes} bytes',
    retry: 'Retrying (attempt {attempt}) in {delay}s',
    empty: 'No tool calls yet.',
    reasoning: 'Reasoning',

    // The live strip: one label and one moving value each, so a reviewer glancing at a long run can
    // see that it is still going and what it has cost so far.
    statsLabel: 'This run so far',
    phase: 'Phase',
    phaseNone: 'not reported yet',
    phaseUnstated: 'not stated',
    elapsed: 'Elapsed',
    toolCalls: 'Tool calls',
    turnLabel: 'Turn',
    tokensLabel: 'Tokens',
    costLabel: 'Cost',

    context: 'Context',
    contextLabel: 'How full the model’s context is',
    contextOf: '{used} / {window} ({percent}%)',
    contextUnknownWindow: '{used} tokens · window unknown',
    contextUnreported: 'not reported by this provider',

    contextBreakdown: 'What is filling the context',
    contextUnits:
      'The total above is in tokens, counted by the provider. The split below is in characters, counted here — exactly, but a different unit.',
    contextInstructions: 'Instructions',
    contextSchema: 'Answer schema',
    contextTools: 'Tool definitions',
    contextOpening: 'Opening message',
    contextToolResults: 'Tool results',
    contextAssistant: 'The model’s own replies',
    contextReasoning: 'Reasoning',
    contextNotReported: 'not reported',
    contextPruned:
      '{pruned} characters of older tool results have been dropped to make room. The model was told, and can call a tool again.',
  },

  /**
   * A run — analysing a change or profiling a repository, both go through the same tool-calling
   * loop — paused at a configured limit rather than failing outright. Shared between the two
   * screens for the same reason toolLog is. Nothing partial is ever shown or saved (§0.2.8), so
   * the body says as much rather than implying there is something to lose by stopping.
   */
  budgetPrompt: {
    title: 'Run paused: {limit} limit reached',
    limitToolCalls: 'tool-call',
    limitTokens: 'token',
    limitTurns: 'turn',
    limitCost: 'cost',
    body:
      '{explanation} So far it has used {toolCalls} tool call(s) and {tokens} token(s), and produced nothing yet. Continue raises this limit and lets it keep going; stopping ends the run now — either way, what it already spent is spent.',
    continue: 'Continue',
    stop: 'Stop',
  },

  /**
   * The analysis itself: one run over the working tree, and the diagram it produces.
   *
   * Iteration 7 added the result; Iteration 8 added the diagram it is drawn as. The `graph` group
   * below is that diagram — the boxes, the lines between them, and the controls above them.
   */
  analysis: {
    heading: 'Analysis',
    description:
      'What the uncommitted change does, clustered into the parts a reviewer should read together and ordered so the most important comes first.',

    run: 'Analyse this change',
    rerun: 'Analyse again',
    running: 'Analysing…',
    cancel: 'Stop',
    cancelled: 'The run was stopped. Nothing was saved.',
    open: 'Open analysis',

    emptyHeading: 'This change has not been analysed',
    emptyBody:
      'One run reads the working tree, explores the repository and produces the whole result at once. It costs real money, so nothing starts until you ask.',

    /**
     * Which parts of an analysis a run asks the model for. The defaults live in Settings; the run
     * options beside the button that spends the money change them for one run only. Each body says
     * what the part buys and what turning it off leaves, because that is the decision being made.
     */
    parts: {
      heading: 'What an analysis asks for',
      description:
        'What every run asks the model for unless you change it for one run. A part you turn off is left out of the request entirely and the model is told not to do that work, so runs get cheaper and faster.',

      changeClusters: 'Also group by theme',
      changeClustersBody:
        'Asks the same run for a second grouping, so you can switch between them afterwards without paying again. Off: only the dependency-flow grouping exists.',
      implementationGroups: 'Group interfaces with implementations',
      implementationGroupsBody:
        'Asks which changed abstractions — interfaces, abstract bases, traits, headers — have changed implementations beside them, so the diagram can draw each as one box. Off: nothing can be merged.',
      risks: 'Risks',
      risksBody:
        'Asks what could go wrong — for the change as a whole, each cluster, each file and each relationship. Off: no risk columns are shown.',
      nodeExplanations: 'File explanations',
      nodeExplanationsBody:
        'What changed in each file, why, what it affects, and notes on how it is built. Off: each file keeps its title.',
      edgeExplanations: 'Relationship explanations',
      edgeExplanationsBody:
        'A sentence on each line between two files, saying how the change affects that relationship. Off: the lines stay, without text.',
      containerExplanations: 'Cluster explanations',
      containerExplanationsBody:
        'A summary and a longer explanation for each cluster. Off: each cluster keeps its title.',

      groupingsLegend: 'Groupings',
      contentLegend: 'Content',
      verbosity: 'How much to write',
      verbosityBrief: 'Brief',
      verbosityBriefBody: 'About a sentence per field. The cheapest, and usually all a card has room for.',
      verbosityMedium: 'Medium',
      verbosityMediumBody: 'A few sentences per field — the lengths analyses were written at before this setting existed.',
      verbosityDetailed: 'Detailed',
      verbosityDetailedBody: 'Room for the specifics and reasoning a shorter answer drops. The most expensive.',

      save: 'Save defaults',
      saving: 'Saving…',
      saved: 'Saved.',

      trigger: 'Run options',
      triggerAll: '{verbosity} · everything',
      triggerSome: '{verbosity} · {count} part(s) off',
      popoverHeading: 'The next run',
      popoverBody: 'Applies to the next run only and is not remembered. Change the defaults in Settings.',
      changed: 'Changed from your defaults',
      reset: 'Reset to defaults',

      skipped: 'Not asked for: {parts}',
      verbosityLine: '{verbosity} prose',
      skippedRisks: 'risks',
      skippedNodeExplanations: 'file explanations',
      skippedEdgeExplanations: 'relationship explanations',
      skippedContainerExplanations: 'cluster explanations',
      explanationsNotRequested: 'Explanations were not asked for on this run.',
    },

    cleanHeading: 'Nothing to analyse',
    cleanBody: 'The working tree matches HEAD, so there is no change to describe.',

    provenance: 'Analysed {date} by {model}, in {duration}',
    provenanceCommit: 'Analysed {date} from commit {commit} by {model}, in {duration}',
    usage: '{input} in / {output} out',
    cost: '${cost}',
    costUnknown: 'cost unknown',
    repairs: 'Repaired {count} time(s) before it validated',

    /**
     * Requirement 8: every earlier run of this repository, reopened without running again.
     */
    library: {
      button: 'History ({count})',
      heading: 'Previous runs',
      body: 'Every analysis of this repository is kept, so any of them reopens instantly without running again.',
      retention: 'The {limit} most recent runs are kept. Saving another removes the oldest.',
      empty: 'No runs yet.',
      latest: 'Latest',
      current: 'Open now',
      open: 'Open',
      files: '{count} file(s)',
      lines: '+{added} −{removed}',
      toolCalls: '{count} tool calls',
      duration: '{seconds}s',
      deleteLabel: 'Delete the run from {date}',
      deleteTitle: 'Delete this run?',
      deleteBody:
        'The analysis from {date} by {model}, and the files you marked reviewed in it, will be removed. Getting it back means running the analysis again, which costs money.',
      deleteConfirm: 'Delete run',
      deleteCancel: 'Keep it',
      earlier: 'You are looking at an earlier run, from {date}.',
      openLatest: 'Open the latest',
    },

    /**
     * Requirement 9: the working tree moved after the run, and the reviewer is told rather than
     * shown an explanation of a change that no longer exists.
     */
    stale: {
      heading: 'The working tree has changed since this analysis ran',
      body: 'What you see describes the change as it was then. Analyse again to bring it up to date.',
      headMoved: 'HEAD has moved from {from} to {to}.',
      headMovedUnknown: 'HEAD has moved since the run.',
      modified: '{count} file(s) edited since',
      added: '{count} file(s) now changed that the analysis does not cover',
      removed: '{count} file(s) the analysis covers are no longer changed',
      more: '…and {count} more',
      showFiles: 'Show files',
      hideFiles: 'Hide files',
      weakerLineCounts:
        'Some files could only be compared by their line counts — this analysis predates content fingerprints, or a file could not be read — so an edit that keeps a file’s counts would not show here.',
      weakerHeadOnly:
        'This analysis predates per-file records, so only HEAD could be compared. Edits to the working tree cannot be detected for it.',
      reanalyse: 'Analyse again',
      dismiss: 'Dismiss',
    },

    /**
     * Requirement 7: the whole ordered trace of what the model asked for, after the run.
     */
    inspector: {
      toggle: 'Tool calls ({count})',
      body: 'Every tool call the model made, in the order it asked for them, with how large each answer was. Arguments and results are previews of at most {arguments} and {result} characters; sizes are of the whole answer the model received.',
      loading: 'Reading the trace…',
      loadFailed: 'The trace could not be read.',
      empty: 'This run made no tool calls.',
      byTool: 'By tool',
      all: 'All · {count}',
      toolChip: '{tool} · {count} · {size}',
      totals: '{count} calls · {size} returned',
      ordinal: '#',
      turn: 'Turn',
      tool: 'Tool',
      arguments: 'Arguments',
      size: 'Response',
      duration: 'Took',
      failed: 'failed',
      showResult: 'Show what {tool} returned',
      progressHeading: 'What the model said it was doing',
      progressEmpty: 'The model reported no progress.',
    },

    summaryHeading: 'What this change does',
    risksHeading: 'Risks',
    noRisks: 'No risks were reported.',
    riskTotal: '{count} in all',
    containersDescription:
      'In the order a reviewer should read them. Inside each, the file to start from comes first and its consequences follow.',

    containersHeading: 'Clusters',
    containerOrder: 'Cluster {order} of {total}',
    entryNode: 'Start here',
    nodeCount: '{count} file(s)',

    nodeTitle: 'What it does in this change',
    nodeWhatChanged: 'What changed',
    nodeWhyItChanged: 'Why',
    nodeAffects: 'What it affects',
    nodeNotes: 'Worth knowing',
    nodeImportance: 'Importance {value} of 5',
    nodeLines: 'lines {start}–{end}',

    edgesHeading: 'How the parts connect',
    edgeDirect: 'direct',
    edgeConceptual: 'conceptual',
    edgeCrosses: 'crosses clusters',
    noEdges: 'No relationships were recorded.',

    statisticsHeading: 'By the numbers',
    statFiles: 'Files',
    statLines: 'Lines',
    statNodes: 'Nodes',
    statContainers: 'Clusters',
    statEdges: 'Edges',
    statEdgeSplit: '{direct} direct / {conceptual} conceptual',
    statLongestChain: 'Longest chain',
    statFanIn: 'Most depended on',
    statFanOut: 'Most far-reaching',
    statRiskyNodes: 'Risky files',
    statRisks: 'Risks recorded',
    statNone: 'none',

    detailsHeading: 'Details',
    detailsBody:
      'Every cluster, file and link in full, in the model’s own words. The diagram above says the same thing more briefly.',

    /**
     * The band across the top of the analysis screen.
     *
     * Summary and risks are always on it; everything else is one toggle away, because a reviewer
     * looking at a three-hundred-box diagram needs the vertical space more than they need the
     * cluster list on screen at all times.
     */
    overview: {
      toggle: 'Overview',
      toggleBody:
        'Reading order, the clusters and their sizes, the numbers, every risk in one list, and where this analysis came from.',

      readingOrderHeading: 'Read in this order',
      readingOrderBody:
        'One path through the whole change, crossing cluster boundaries. Pick a step to go to it on the diagram.',
      readingOrderEmpty: 'No reading order was recorded.',

      clustersHeading: 'Clusters, in reading order',
      clusterSize: '{count} files',
      clusterRisks: '{count} risks',

      registerHeading: 'Every risk, in one place',
      registerBody:
        'Collected from the change as a whole, from each cluster, from each file and from each link between them.',
      registerEmpty: 'Nothing in this change was flagged as a risk.',
      registerFromOverall: 'The change as a whole',
      registerFromContainer: 'Cluster · {title}',
      registerFromNode: 'File · {path}',
      registerFromEdge: 'Link · {source} → {target}',

      runHeading: 'This run',
      runProvider: 'Provider',
      runModel: 'Model',
      runWhen: 'Finished',
      runCommit: 'From commit',
      runTokensIn: 'Tokens in',
      runTokensOut: 'Tokens out',
      runCost: 'Cost',
      runDuration: 'Took',
      runRepairs: 'Repair rounds',
      runToolCalls: 'Tool calls',
      runSchema: 'Contract',
      runUnknown: 'unknown',
      runSeconds: '{count}s',
    },

    /**
     * The explanation cards.
     *
     * Everything on one is already in the persisted analysis: no call of any kind happens because
     * something was clicked (§0.2.8, and the iteration's own first fixed decision).
     */
    hover: {
      close: 'Close',
      copyPath: 'Copy path',
      copied: 'Path copied',
      copyFailed: 'The path could not be copied',

      nodePlace: 'Rank {rank} in {container} · importance {importance} of 5',
      nodeSymbol: 'in {symbol}',

      edgeHeading: 'How these two relate',
      edgeDirection: 'Read {source}, then {target}',

      containerEntry: 'Starts at {file}',
      containerSize: '{count} files',

      nothingWritten: 'The model wrote nothing here.',
    },

    /** The model's prose, drawn as Markdown. A file it names can be opened from the text. */
    markdown: {
      openReference: 'Open the diff of {path}',
    },

    diagnosticsHeading: 'What validation noticed',
    diagnosticsBody:
      'The result passed every rule that would have stopped it. These are the things worth knowing anyway.',

    /** The diagram: the boxes, the lines between them, and the controls above them. */
    graph: {
      canvasLabel: 'The change as a diagram',
      layingOut: 'Arranging the diagram…',
      layoutFailed: 'The diagram could not be arranged. See log.txt for details.',

      entryPoint: 'Start reading here',
      risky: 'Carries a risk',
      rankTitle: 'Position in this cluster’s reading order',
      binary: 'binary',
      noCounts: 'no line count',

      fileCount: '{count} files',
      startsAt: 'Starts at {file}',
      collapseContainer: 'Collapse {title}',
      expandContainer: 'Expand {title}',

      fitView: 'Fit',
      collapseAll: 'Collapse all',
      expandAll: 'Expand all',
      legend: 'Legend',
      counts: '{containers} clusters · {nodes} files · {edges} links',

      /**
       * The two groupings, and the line that explains why the same change looks different in each.
       * Both bodies are readable before switching — each sits on its own button — so the reviewer
       * can tell what the other view is for without having to go and look.
       */
      grouping: {
        label: 'Grouping',
        dependencyFlow: 'Dependency flow',
        dependencyFlowBody:
          'Clusters follow the change itself: a path stays in one cluster all the way through, even where it crosses database, auth and API.',
        changeClusters: 'Change clusters',
        changeClustersBody:
          'Clusters follow the theme instead: each concern is its own cluster, so you can see what areas were touched — and paths that span several of them are split between clusters.',
        unavailable:
          'This analysis was produced without the change-clusters grouping. Analyse again to get both.',
      },

      searchLabel: 'Find a file on the diagram',
      searchPlaceholder: 'Find a file, node or cluster…',
      searchClear: 'Clear the search',
      searchNoResults: 'Nothing in this change matches “{query}”.',
      searchByPath: 'Files',
      searchByNodeTitle: 'Node titles',
      searchByContainerTitle: 'Clusters',

      legendEdges: 'Lines',
      legendDirect: 'Direct — real code connects the two',
      legendConceptual: 'Conceptual — connected by intent, not by code',
      legendCrossContainer: 'Between clusters — drawn, but kept out of the layout',
      legendBundle: 'Several links, folded into one by a collapsed cluster',

      legendStates: 'Boxes',
      legendStatesNote:
        'A file can be several of these at once. Colour is the project it belongs to, never its state.',

      legendImportance: 'Emphasis',
      legendImportanceHigh: 'The model ranked this one of the changes to read',
      legendImportanceNormal: 'Ordinary',
      legendImportanceLow: 'A mechanical consequence — faded, never hidden',
      legendImportanceNote:
        'Every changed file is on the diagram whatever its rank. A faded box comes back to full the moment you hover it, search for it or jump to it.',

      legendProjects: 'Projects',
      legendNoProjects: 'Nothing in this change was attributed to a project.',
      legendOtherProjects: '{count} other projects',
      legendPaletteFull:
        'Only the {count} largest projects get a colour of their own; every box also prints its project name.',

      /**
       * Filtering by project, unlike the "faded, never hidden" emphasis channel above: unchecking a
       * project here genuinely removes its files from the diagram — the diff panel, the reading order
       * and the risk register are unaffected, and the banner says so for as long as it is in effect.
       */
      projectFilter: {
        // Deliberately not the bare word "Projects" — the legend already uses that for its own
        // heading, and the two controls are visible together once the legend is open.
        label: 'Filter projects',
        labelFiltered: 'Filter projects ({count} hidden)',
        heading: 'Show projects',
        showAll: 'Show all',
        noProjects: 'Nothing in this change was attributed to a project, so there is nothing to filter.',
        fileCount: '{count} files',
        bannerSingle:
          'Hiding {project} — {count} files are off the diagram. Nothing was removed from the analysis.',
        bannerMultiple:
          'Hiding {count} projects — {files} files are off the diagram. Nothing was removed from the analysis.',
      },

      reviewed: 'Reviewed',
      current: 'Open in the diff panel',
      openDiff: 'Open the diff',
      openDiffHint: 'Double-click a box, or use this button',

      /** The controls that live on a box, and on a cluster's title bar. */
      nodeActions: 'What to do with {file}',
      openContainer: 'Open every file',

      /**
       * An abstraction drawn together with its implementations. Which files those are is the
       * model's answer; whether they are drawn as one box is the reviewer's, and costs nothing.
       */
      merged: {
        toggle: 'Merge implementations',
        toggleBody:
          'Draws each interface, abstract base or trait together with the changed files that implement it, as one box with a row per file.',
        notAsked:
          'This analysis was produced without implementation groups. Analyse again with “Group interfaces with implementations” on to get them.',
        none: 'Nothing in this change implements an abstraction that changed beside it, so there is nothing to merge.',
        heading: 'Abstraction · {count} implementing',
        abstraction: 'The abstraction',
        implementation: 'Implements it',
        legend: 'An abstraction and its implementations, merged into one box — each row is still its own file',
      },
    },

    /**
     * The diff panel: reading the code, and moving to the next thing to read.
     *
     * Everything here is about one node at a time. What the change means as a whole is the band's
     * job, and the graph's; this is the half of the review that used to happen in another window.
     */
    diff: {
      heading: 'Diff',
      close: 'Close the diff',
      loading: 'Reading the file…',
      failed: 'The file could not be read.',
      empty: 'Choose a file on the diagram to read it.',
      emptyHint: 'Hover a box and press its diff button, or double-click the box.',

      sideBySide: 'Side by side',
      inline: 'Inline',
      previousHunk: 'Previous change',
      nextHunk: 'Next change',
      resizeHandle: 'Drag to resize the diff panel',
      resizeExplanationHandle: 'Drag to resize the explanation',
      expand: 'Widen the panel',
      collapse: 'Give the diagram its width back',
      fullScreen: 'Full screen',
      exitFullScreen: 'Leave full screen',

      /** Monaco folds unchanged runs by default; this is the way to see what it folded. */
      showWholeFile: 'Whole file',
      showChangesOnly: 'Changes only',

      /** Requirement 4, with a fold over it. */
      explanationHeading: 'What changed and why',

      /** Requirement: a cluster opens as a queue of its files. */
      leaveContainer: 'Stop reading this cluster as a list',
      dismissEditorError: 'Dismiss',

      /** §0.2.12's second write path: editing the working-tree side directly and saving it. */
      save: 'Save',
      saving: 'Saving…',
      discardTitle: 'Discard the unsaved edit?',
      discardBody: 'The change you typed into {path} has not been saved. Leaving now discards it.',
      discardCancel: 'Keep editing',
      discardConfirm: 'Discard edit',

      renamedFrom: 'was {path}',
      region: 'lines {start}–{end}',
      wholeFile: 'the whole file',

      markReviewed: 'Mark reviewed',
      markUnreviewed: 'Mark unreviewed',
      reviewedProgress: '{reviewed} of {total} reviewed',
      reviewedProgressLabel: 'Reviewed',
      reviewFailed: 'That mark could not be saved. See log.txt for details.',

      binaryHeading: 'Binary file',
      binaryBody: 'Git will not produce a text diff for this, so there is nothing to read here.',
      tooLargeHeading: 'Too large to show',
      tooLargeBody:
        'This file is {size}, past the {limit} DiffHacker will move across to the viewer. Open it in an external editor instead.',
      absentHeading: 'Nothing to compare',
      absentBody: 'Neither the committed side nor the working tree has this file.',
      sizeHead: 'Committed: {size}',
      sizeWorking: 'Working tree: {size}',

      degraded:
        'This file is {size}. Syntax highlighting and the minimap are off so the interface stays responsive; the diff itself is complete.',
      fallbackEncoding:
        'This file is not valid UTF-8 and was read as {encoding}, so some characters are a best effort.',

      openIn: 'Open in {editor}',
      openInVsCode: 'VS Code',
      openInVisualStudio: 'Visual Studio',
      openInCustom: 'your editor',

      /** The same three names, for the buttons on a box — which have a fifth of the width. */
      shortVsCode: 'VS Code',
      shortVisualStudio: 'VS',
      shortCustom: 'Editor',

      /** Requirement 5. A choice, never a single arrow: the graph is not a list. */
      navigationHeading: 'Where to go next',
      predecessors: 'Read before this',
      successors: 'Read after this',
      noNeighbours: 'Nothing else in the graph points at this file or away from it.',
      readingOrderHeading: 'Recommended order',
      readingOrderPosition: '{position} of {total}',
      readingOrderPrevious: 'Previous',
      readingOrderNext: 'Next',
      readingOrderPreviousHint: 'Previous in the recommended reading order',
      readingOrderNextHint: 'Next in the recommended reading order',
      crossesClusters: 'in {container}',

      /** Requirement 10: cheap shortcuts, listed where they can be found. */
      shortcutsHeading: 'Keyboard',
      shortcuts:
        'J and K walk the reading order · R marks reviewed · C folds the cluster · Ctrl/Cmd+S saves an edit · Escape leaves full screen, then closes this panel',
    },

    state: {
      changed: 'changed',
      added: 'added',
      deleted: 'deleted',
      unchanged_relevant: 'unchanged',
      risky: 'risky',
      entry_point: 'start here',
    },

    diagnostic: {
      cycle: 'These files depend on each other in a loop.',
      self_edge: 'A file was linked to itself.',
      incomplete_reading_order:
        'The model’s reading order left some files out, so cluster order and rank were used instead.',
      unreachable_node:
        'Nothing in its cluster leads to this file, so a reader arrives at it without being told why.',
      verbose_field:
        'The model wrote far more here than fits where it is read, so it is shown shortened.',
      implementation_group_split:
        'This file implements an abstraction in another cluster, so it is drawn on its own rather than merged with it.',
    },
  },

  /** The documentation generator, and the first of the application's two write paths. */
  documentation: {
    heading: 'Project documentation',
    description:
      'Three documents written from the profile above. They live in DiffHacker; writing them into your repository is a separate, deliberate step.',
    preview: 'Preview documents',
    previewing: 'Preparing…',
    previewHeading: 'Documents to be written',
    previewBody:
      'This is every file and every byte that would be written. Nothing has been written yet.',
    targetLabel: 'Write to',
    targetRoot: 'Repository root',
    targetDocs: 'The docs/ folder',
    exists: 'This file already exists and would be replaced.',
    existsUnreadable:
      'A file already exists at this path and could not be read as text, so no comparison can be shown. It would be replaced.',
    isNew: 'This file does not exist yet.',
    unchanged: 'This file already has exactly this content.',
    diffHeading: 'What would change',
    write: 'Write to repository',
    writing: 'Writing…',
    cancel: 'Cancel, write nothing',
    wrote: 'Wrote {count} files into {path}.',
    wroteWithOverwrites: 'Wrote {count} files into {path}, replacing {replaced}.',
    writeWarning:
      'This is the only thing in DiffHacker that writes to your repository. Nothing else ever does.',
    noProfile: 'Analyse the repository first — documentation is written from its profile.',
  },

  /**
   * The Help screen: a step-by-step guide with screenshots, what the product is, how to read the
   * diagram, the shortcuts, and the questions people ask first.
   *
   * Written in the Markdown subset `lib/markdown.ts` parses, because the same strings are drawn in
   * the window by `Markdown.tsx` *and* rendered into `docs/user-guide.md` by
   * `help/userGuideMarkdown.ts`. That file is generated from this block: edit here, never there.
   *
   * Every claim in this block is a claim about the product. When behaviour changes — a limit, a
   * default, a path — this is one of the places that has to change with it.
   */
  help: {
    heading: 'Help',
    description:
      'How to set DiffHacker up, how to read what it shows you, and answers to the questions people ask first.',
    sectionsLabel: 'Help sections',

    /** Only in `docs/user-guide.md`, which is generated from this block. */
    document: {
      title: 'DiffHacker user guide',
      inApp: 'The same guide is inside the application: press **{help}** in the top right corner of any screen.',
    },

    sections: {
      guide: 'Step-by-step guide',
      about: 'What DiffHacker does',
      diagram: 'Reading the diagram',
      shortcuts: 'Keyboard shortcuts',
      faq: 'FAQ',
      troubleshooting: 'Troubleshooting',
    },

    /** The card on the start screen that points newcomers at the guide. */
    offer: {
      heading: 'New to DiffHacker?',
      body: 'A fifteen-step guide, with screenshots, takes you from adding an API key to reviewing your first change.',
      open: 'Read the step-by-step guide',
      dismiss: 'Not now',
    },

    guide: {
      intro:
        'From a fresh install to a reviewed change. Steps 1 to 3 happen once; after that, every review starts at step 4.',
      stepsLabel: 'Steps',
      position: 'Step {current} of {total}',
      previous: 'Back',
      next: 'Next',
      finish: 'Finish',
      missingScreenshot: 'The screenshot for this step has not been generated yet.',
      keyboardHint: 'Left and right arrow keys move between steps.',

      steps: {
        provider: {
          title: 'Add an LLM provider',
          alt: 'The Settings screen with a new provider being added: provider type, name, model, base URL and a masked API key.',
          body:
            'DiffHacker uses your own API key, so the first stop is **Settings**, in the top right corner.\n\n' +
            '1. Press **Add a provider** and choose the provider: OpenAI, Anthropic, Google Gemini, Grok, DeepSeek, or any OpenAI-compatible endpoint — a server on your own machine included, given its base URL.\n' +
            '2. Give it a name you will recognise, type the model identifier, and paste your API key.\n' +
            '3. Press **Save**. With several providers, **Use this one** chooses the one runs use.\n\n' +
            'The key is kept in your operating system’s secret store. It is never written to the log and never reaches the window you are looking at.',
        },
        testConnection: {
          title: 'Test the connection',
          alt: 'A successful connection test listing the number of models the key can reach.',
          body:
            'Press **Test connection** before saving, or any time after. It lists the models your key can reach, uses no tokens and costs nothing.\n\n' +
            '- A success means the key works. If the model you typed is not among those listed, DiffHacker says so — check the spelling.\n' +
            '- A failure says why: a rejected key, no quota left, or an endpoint that does not answer.\n\n' +
            'The optional fields under it are worth one look: your own token prices if the bundled price table is out of date, the model’s context window, and **run limits** — how many tool calls and tokens one run may spend before it pauses to ask you.',
        },
        defaults: {
          title: 'Choose what an analysis asks for',
          alt: 'The analysis defaults in Settings: the second grouping, implementation groups, risks, three kinds of explanation and the amount of prose.',
          body:
            'Further down **Settings**, **What an analysis asks for** sets the defaults for every run:\n\n' +
            '- **Also group by theme** — a second grouping you can switch to afterwards without paying again.\n' +
            '- **Group interfaces with implementations** — lets the diagram draw an abstraction and the files implementing it as one box.\n' +
            '- **Risks**, and explanations for files, relationships and clusters.\n' +
            '- **How much to write** — brief (the cheapest), medium or detailed.\n\n' +
            'A part you turn off is left out of the request entirely, so runs get cheaper and faster. Press **Save defaults** when you are done.',
        },
        openRepository: {
          title: 'Open a repository',
          alt: 'The start screen with the folder picker button, a path field and the list of recent repositories.',
          body:
            'On the start screen, press **Choose a folder…**, or type a path and press **Open**. A folder anywhere inside a repository works; DiffHacker opens the repository around it.\n\n' +
            'Repositories you have opened are listed underneath for one click next time.\n\n' +
            'DiffHacker reviews **uncommitted changes only** — your working tree against `HEAD` — so there has to be something you have not committed yet.',
        },
        changes: {
          title: 'Check the changed files',
          alt: 'The repository screen listing uncommitted files with their status, line counts, language and project.',
          body:
            'The repository screen lists every uncommitted file — staged, unstaged and new — with its status, line counts, language and project.\n\n' +
            '- **Include new files git does not track yet** is on by default. Turn it off to see only your edits to tracked files.\n' +
            '- **Show diff** on a row gives a quick look at one file.\n' +
            '- **Refresh** after you change something.\n\n' +
            'Nothing has been sent anywhere yet. **Repository profile** and **Analysis** are in the header.',
        },
        profile: {
          title: 'Generate the repository profile',
          alt: 'The repository profile after a run: purpose, architecture and modules, with the user’s own notes and instructions underneath.',
          body:
            'The profile is what DiffHacker knows about a repository before it sees a diff: its purpose, architecture, modules, conventions and entry points. It is made once, reused by every analysis, and makes them noticeably better.\n\n' +
            '1. Open **Repository profile** and press **Analyse repository**. This is an LLM run, so it costs money; you can watch it work and stop it at any time.\n' +
            '2. Correct anything the model got wrong, and press **Save**.\n' +
            '3. Add **Your notes** and **Standing instructions** — “ignore the generated/ folder”, “this is CQRS”. They are sent with every review of the repository, and analysing again never overwrites them.\n\n' +
            'You can skip this step. Analyses still work without a profile, but the model has to work out what the project is from the diff alone, every time.',
        },
        runOptions: {
          title: 'Choose what this run produces',
          alt: 'The run options popover beside the Analyse button, listing the parts of an analysis and the amount of prose for the next run.',
          body:
            'Open **Analysis** from the repository screen. Beside the button that starts a run, **Run options** shows what the next run will ask for — your defaults from Settings.\n\n' +
            'A change made here applies to **this run only**; the next one starts from your defaults again. It is the place to trim an expensive re-run: fewer parts, or brief prose.',
        },
        run: {
          title: 'Run the analysis',
          alt: 'An analysis in progress: the model’s latest progress message, the running figures for elapsed time, tool calls, tokens and cost, and the tool log.',
          body:
            'Press **Analyse this change**. The model explores the repository through DiffHacker’s tools — reading diffs, searching, opening files — and you can watch it: what it says it is doing, every tool call, the tokens, the cost so far and how full its context is.\n\n' +
            '- **Stop** ends the run at once. Nothing is saved, and what was already spent is spent.\n' +
            '- A run that reaches one of your limits **pauses and asks** whether to keep going.\n' +
            '- Nothing appears until the whole result exists and has passed DiffHacker’s checks, one of which is that every changed file is in it.',
        },
        overview: {
          title: 'Read the summary and the risks',
          alt: 'A finished analysis: what the change does on the left, its risks in a column on the right, and the provenance line above.',
          body:
            'A finished analysis opens with **What this change does** on the left and **Risks** in a column of their own on the right — risks are never mixed into the explanations.\n\n' +
            '- **Overview** unfolds the rest: the recommended reading order, the clusters, the numbers, every risk in one list, and the run’s tool calls.\n' +
            '- The line under the heading says which model produced the analysis, when, and what it cost.\n' +
            '- **History** reopens any earlier run of this repository instantly, without running again.',
        },
        diagram: {
          title: 'Read the diagram',
          alt: 'The diagram: clusters of file boxes coloured by project, with solid and dashed lines between them.',
          body:
            'The change is drawn as **clusters** of files that changed for one reason — one box per file, or two for a file that holds two unrelated changes.\n\n' +
            '- Read **top to bottom**. The box marked **start here** is where a cluster begins; the boxes under it follow from it.\n' +
            '- A box’s **colour** is its project. Its **border and corner badge** say whether it was added, changed or deleted, or carries a risk. A faded box is a mechanical consequence — faded, never hidden.\n' +
            '- **Solid lines** are real code dependencies, **dashed lines** are connections by intent, and faint lines cross between clusters.\n' +
            '- **Legend** explains every mark. **Fit**, **Collapse all**, the search box and **Filter projects** help on a large change.',
        },
        explanation: {
          title: 'Click for the explanation',
          alt: 'An explanation card beside a file box: what changed, why, what it affects, and its risks.',
          body:
            'Click a box for what changed, why, what it affects, and its risks. Click a line for how the two files relate, or a cluster’s title bar for what the cluster is about.\n\n' +
            'The card stays where it is while you read, scroll or copy from it; click an empty part of the diagram to put it away. Every explanation was written during the run and stored with it, so opening one asks nothing of the model and costs nothing.',
        },
        diff: {
          title: 'Open the diff',
          alt: 'The diff panel beside the diagram: the code change, the explanation under it and the reading-order navigation.',
          body:
            'Double-click a box — or use the diff button that appears on it — to open the file beside the diagram, with its explanation still under the code.\n\n' +
            '- **Previous** and **Next** follow the recommended reading order. **Where to go next** offers the files that lead here and the ones this leads to.\n' +
            '- **Open every file** on a cluster’s title bar turns the whole cluster into a reading list.\n' +
            '- Drag the panel’s edge to widen it, or use **Full screen**. VS Code, Visual Studio or an editor command of your own opens from the panel’s header.',
        },
        reviewed: {
          title: 'Mark files reviewed',
          alt: 'A file marked reviewed in the diff panel, with the progress counter showing how much of the change has been read.',
          body:
            'Press **Mark reviewed** — or the `R` key — as you finish each file. Reviewed boxes are marked on the diagram, and the counters show how much of the change, and of each cluster, you have read.\n\n' +
            'Marks are saved with the analysis and survive a restart. A new run is a new analysis, so it starts with nothing marked.',
        },
        grouping: {
          title: 'Switch the grouping',
          alt: 'The same change drawn as change clusters: one cluster per theme instead of one per chain of change.',
          body:
            '**Dependency flow** keeps each chain of change in one cluster, even where it crosses the database, the API and the interface. **Change clusters** groups by theme instead, so you can see which areas were touched — at the cost of splitting those chains.\n\n' +
            'Both groupings come out of the same run when it was asked for both, so switching is instant and free. **Merge implementations** draws an interface and the files implementing it as one box.',
        },
        current: {
          title: 'Keep it current',
          alt: 'The banner saying the working tree has changed since the analysis ran, above the diagram.',
          body:
            'Edit a file after the analysis ran and a banner says the working tree has changed, and lists what differs: files edited since, files newly changed, files no longer changed. Put the edit back and the banner goes away.\n\n' +
            '**Analyse again** brings the analysis up to date. Earlier runs stay in **History** — the 20 most recent for each repository — and reopen without spending anything.',
        },
      },
    },

    about: {
      body:
        'DiffHacker turns a large uncommitted change into a diagram you can review in order.\n\n' +
        'An AI agent, or a long day, can leave hundreds of changed files behind, and a file list sorted alphabetically hides the shape of the change. DiffHacker gives an LLM of your choice the tools to explore your repository and asks it to explain the change: which files belong together, where to start reading, how each part leads to the next, and what could go wrong.',
      needsHeading: 'What it needs',
      needs:
        '- `git`, on your PATH.\n' +
        '- An API key for an LLM provider. You pay the provider directly for what each run uses.\n' +
        '- A local repository with uncommitted changes.',
      neverHeading: 'What it never does',
      never:
        '- **Change your repository on its own.** It never commits, stages or checks out anything, and the LLM analysing your change never edits a file. There are two exceptions, both of them things you do yourself: the optional documentation export on the repository profile screen, which shows you every file first and writes only when you confirm; and typing directly into the diff editor and pressing Save, which is an ordinary text edit to the one file open, never a git operation.\n' +
        '- **Read what it should not.** Files git ignores are invisible to it, and files that usually hold credentials are listed but never opened.\n' +
        '- **Show half a result.** A run produces a complete, checked analysis, or nothing.',
    },

    diagram: {
      intro: 'Everything on the diagram is the model’s answer, drawn. The app computes no dependencies of its own.',
      items: {
        clusters: {
          title: 'Clusters',
          body:
            'A cluster is a group of files that changed for one reason, and unrelated changes land in separate clusters. They are laid out in the order to read them. Click a cluster’s title bar for what it is about, fold it with its arrow, or press **Open every file** to read it as a list.',
        },
        boxes: {
          title: 'Boxes',
          body:
            'One box per changed file — or two, when a file holds two unrelated changes; the second box’s name ends in `#` and a short label.\n\n' +
            '- **Fill colour** is the project the file belongs to. Every box also prints the project’s name.\n' +
            '- **Border style and corner badge** say whether the file was added, changed or deleted, and whether it carries a risk.\n' +
            '- **Start here** marks where to begin reading a cluster.\n' +
            '- **Faded** means the model ranked it a mechanical consequence. It is still there, and comes back to full the moment you hover it, search for it or go to it.',
        },
        lines: {
          title: 'Lines',
          body:
            'A line is reading flow: to understand the second file, read the first. It is not a list of calls.\n\n' +
            '- **Solid** — real code connects the two.\n' +
            '- **Dashed** — connected by intent or by the order to read them, not by code.\n' +
            '- **Faint** — the line crosses between clusters. It is drawn, but kept out of the layout.\n' +
            '- While a cluster is folded, one line can stand for several links.',
        },
        order: {
          title: 'Reading order',
          body:
            'Inside a cluster, top to bottom is the order to read. **Read in this order**, under **Overview**, gives one path through the whole change, and **Previous** and **Next** in the diff panel walk it.',
        },
        groupings: {
          title: 'Two groupings',
          body:
            '**Dependency flow** keeps a chain of change whole; **Change clusters** groups by theme. The second exists only when the run was asked for it — **Also group by theme** — and its button says so when it was not.',
        },
        merged: {
          title: 'Merged boxes',
          body:
            'With **Merge implementations** on, an interface, abstract base or trait is drawn together with the changed files implementing it, as one box with a row per file. Each row is still its own file: it opens its own diff and takes its own reviewed mark.',
        },
        finding: {
          title: 'Finding your way',
          body:
            '- The **search box** finds files, box titles and clusters, and takes you to the one you pick.\n' +
            '- **Filter projects** takes whole projects off the diagram. The analysis is untouched, and a banner says what is hidden.\n' +
            '- **Fit** shows the whole change; **Collapse all** folds every cluster.',
        },
      },
    },

    shortcuts: {
      intro:
        'On the analysis screen, whenever you are not typing into a field or into the code:',
      keyColumn: 'Key',
      actionColumn: 'What it does',
      keys: {
        next: { key: 'J', action: 'Open the next file in the recommended reading order' },
        previous: { key: 'K', action: 'Open the previous file in the reading order' },
        open: { key: 'Enter', action: 'Open the diff of the box the search took you to' },
        reviewed: { key: 'R', action: 'Mark the open file reviewed, or unmark it' },
        fold: { key: 'C', action: 'Fold the open file’s cluster' },
        escape: { key: 'Escape', action: 'Leave full screen, then close the diff panel' },
      },
      helpNote: 'In this guide, the left and right arrow keys move between steps.',
    },

    faq: {
      items: {
        readOnly: {
          question: 'Does DiffHacker change my repository?',
          answer:
            'No. It never commits, stages, checks out or edits files, and it runs only git commands that read. The single exception is the optional documentation export on the repository profile screen: it previews every file first, and writes nothing unless you press **Write to repository**.',
        },
        sentToModel: {
          question: 'What is sent to the LLM provider?',
          answer:
            'The first request carries the list of changed files, the repository profile and your standing instructions. Everything else the model asks for through DiffHacker’s tools — a diff, a file, a search — so **portions of your source code do reach the provider**, as with any AI review.\n\n' +
            'The tools see only what git sees: nothing inside `.git/`, and nothing your `.gitignore` excludes. Files that usually hold credentials — `.env`, private keys, `.npmrc` and the like, plus any patterns you add under **Files never read** on the profile screen — are listed but never opened.',
        },
        keys: {
          question: 'Where is my API key kept?',
          answer:
            'Encrypted in DiffHacker’s data folder, under a key held by your operating system: DPAPI on Windows, the Keychain on macOS, libsecret on Linux. Where no keyring is available the key is derived from the machine and your user account instead, and Settings says so. Keys are never written to the log and never reach the interface.',
        },
        cost: {
          question: 'How much does a run cost?',
          answer:
            'It depends on the model and the size of the change: you pay your provider for the tokens a run uses. The live view shows the cost so far and every finished analysis shows what it cost. Prices come from a table bundled with DiffHacker, or from the prices you enter for a provider; a model in neither shows as **cost unknown**, never as free.\n\n' +
            'To spend less, turn parts off in **Run options**, choose brief prose, or set run limits on the provider.',
        },
        limits: {
          question: 'What happens when a run reaches a limit?',
          answer:
            'It pauses and asks. **Continue** raises the limit and carries on; **Stop** ends the run, and nothing is saved. Limits are set for each provider in Settings — by default 500 tool calls and 10,000,000 tokens per run.',
        },
        profileNeeded: {
          question: 'Do I need a repository profile?',
          answer:
            'No, but analyses are better with one. Without it the model has to work out what the project is, how it is organised and what its conventions are from the diff alone, on every run. A profile is made once and reused until you analyse the repository again.',
        },
        branches: {
          question: 'Can I review a branch, a commit or a pull request?',
          answer:
            'Not directly. DiffHacker reviews uncommitted changes only: your working tree against `HEAD`, staged, unstaged and new files together.\n\n' +
            'To review a finished branch without touching your own checkout, make a second worktree at its base and squash the branch into it without committing — `git worktree add --detach ../review main`, then `git merge --squash feature` inside `../review` — and open that folder.',
        },
        stale: {
          question: 'Why does it say the analysis is out of date?',
          answer:
            'Every run records a fingerprint of each changed file. When the working tree no longer matches — a file edited, a new change, a change undone, a new commit — the analysis describes a change that no longer exists, and the banner says what differs. Undo the edit and the analysis is current again; **Analyse again** brings it up to date.',
        },
        twoBoxes: {
          question: 'Why does one file appear as two boxes?',
          answer:
            'The model splits a file only when it holds two unrelated changes, so that each can sit in the cluster it belongs to. Both boxes open the same file, each focused on its own lines.',
        },
        dashed: {
          question: 'Why are two files connected when no code links them?',
          answer:
            'A dashed line is a connection the model made by intent or by reading order — a migration and the endpoint that relies on it — rather than by an import. DiffHacker deliberately does not draw a dependency graph: a cluster held together by intent alone is exactly the thing a file list cannot show you.',
        },
        groupingDisabled: {
          question: 'Why can I not switch to Change clusters?',
          answer:
            'The run behind this analysis was not asked for the second grouping. Turn on **Also group by theme** in **Run options**, or in Settings for every run, and analyse again.',
        },
        large: {
          question: 'Does it work on very large changes?',
          answer:
            'Yes — ten files or fifteen hundred. A large change takes longer and costs more, and on the diagram **Collapse all**, the search box and **Filter projects** keep it manageable. Every changed file is still in the result: DiffHacker checks that after every run.',
        },
        model: {
          question: 'Which model should I use?',
          answer:
            'A capable one that is good at calling tools and answering in a fixed structure, with a large context window — the better the model, the better the clusters and explanations. If a run fails because the answer could not be read as an analysis, try a more capable model. **Test connection** lists the models your key can reach.',
        },
        local: {
          question: 'Can I use a model running on my own machine?',
          answer:
            'Yes, if it is served through an OpenAI-compatible API: choose **OpenAI-compatible endpoint** and give its base URL. It has to support tool calling, and small models often struggle to produce a complete result for a large change.',
        },
        data: {
          question: 'Where does DiffHacker keep its data?',
          answer:
            'In a folder of its own, never in your repository:\n\n' +
            '- Windows: `%LOCALAPPDATA%\\DiffHacker`\n' +
            '- macOS: `~/Library/Application Support/DiffHacker`\n' +
            '- Linux: `~/.local/share/DiffHacker`, or under `$XDG_DATA_HOME` when it is set\n\n' +
            'It holds the settings database with your stored analyses, the encrypted keys, and the log.',
        },
        agent: {
          question: 'Can my own coding agent use DiffHacker’s tools?',
          answer:
            'Yes. `diffhacker-mcp` serves the same read-only toolbox to any MCP client over stdio. The project’s README shows how to register it.',
        },
      },
    },

    troubleshooting: {
      items: {
        gitMissing: {
          title: 'Git was not found',
          body:
            'DiffHacker reads your repository through the git command line and can do nothing without it. Install git, check that `git --version` works in a terminal, then restart DiffHacker.',
        },
        noProvider: {
          title: 'No LLM provider configured',
          body:
            'Add one in **Settings**. With several, press **Use this one** on the provider runs should use.',
        },
        connection: {
          title: 'Test connection fails',
          body:
            'The message names the cause. A rejected key: paste it again. No credit or quota: top up with the provider. Nothing answered: check the base URL — an OpenAI-compatible server usually wants one ending in `/v1`.',
        },
        nothingToAnalyse: {
          title: 'Nothing to analyse',
          body:
            'The working tree matches `HEAD`, so there is no change to describe. DiffHacker reviews uncommitted changes; make one, or open the repository that has it.',
        },
        runFailed: {
          title: 'A run failed',
          body:
            '- **Could not be read as an analysis**, or **could not produce a result that covered the whole change**: the model’s answer did not pass DiffHacker’s checks, even after it was asked to repair it. Try again, or use a more capable model.\n' +
            '- **Too large for this model’s context window**: use a model with a larger one, or turn parts off and choose brief prose.\n' +
            '- **Rate-limiting** or **no quota left**: the provider refused the run. Wait, or top up.',
        },
        diagram: {
          title: 'The diagram could not be arranged',
          body:
            'Laying the diagram out failed. The analysis itself is stored and safe; reopen it, and if it happens again, the details are in the log.',
        },
        log: {
          title: 'Where is the log?',
          body:
            'In `logs/log.txt` inside DiffHacker’s data folder — the FAQ lists where that is on each system. API keys are never written to it. It is the first thing worth attaching to a bug report.',
        },
      },
    },
  },

  error: {
    unknown_error: 'Something went wrong. See log.txt for details.',
    rpc_timeout: 'The host did not respond in time.',
    rpc_cancelled: 'That was cancelled.',

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

    changeset_save_conflict:
      '{path} changed on disk since it was opened here, so the edit was not saved. Reopen the diff to see the current file.',
    changeset_save_outside_repository: 'That file is not inside the repository, so the edit was not saved.',
    changeset_save_failed: 'The edit to {path} could not be saved. See log.txt for details.',

    provider_not_found: 'That provider is no longer configured.',
    provider_key_missing: 'No API key is stored for this provider. Add one and try again.',
    provider_model_required: 'Enter a model identifier.',
    provider_base_url_required: 'An OpenAI-compatible endpoint needs a base URL.',
    provider_invalid_base_url: 'That base URL is not a valid absolute URL.',
    provider_invalid_cost: 'A token price cannot be negative.',
    provider_invalid_context_window:
      'A context window must be a positive number of tokens.',

    secret_store_unavailable: 'Your API keys could not be read. See log.txt for details.',
    settings_store_unavailable: 'Your settings could not be read. See log.txt for details.',

    profile_no_provider:
      'No LLM provider is set up. Add one in settings, and mark it active if you have several.',
    profile_repository_unreadable:
      '{path} could not be read as a git working tree. It may have been moved or deleted.',
    profile_run_failed: 'The repository could not be profiled. See log.txt for details.',
    profile_unreadable_answer:
      'The model answered, but not with something that could be read as a profile. Try again, or use a more capable model.',
    profile_not_found: 'There is no profile for this repository to edit.',

    analysis_no_provider:
      'No LLM provider is set up. Add one in settings, and mark it active if you have several.',
    analysis_repository_unreadable:
      '{path} could not be read as a git working tree. It may have been moved or deleted.',
    analysis_clean_changeset:
      'There is nothing uncommitted to analyse. Make a change first.',
    analysis_unreadable_answer:
      'The model answered, but not with something that could be read as an analysis. Try again, or use a more capable model.',
    analysis_validation_failed:
      'The model could not produce a result that covered the whole change, even after being asked to fix it. See the detail below, or try a more capable model.',
    analysis_run_failed: 'The change could not be analysed. See log.txt for details.',
    analysis_not_found:
      'That analysis is not stored for this repository. It may have been deleted, or removed to make room for newer runs. Open another from the history, or analyse the change.',
    analysis_node_not_found:
      '{nodeId} is not part of this analysis. It may have been produced by an earlier run — reopen the analysis and try again.',

    editor_not_found:
      'That editor was not found on this machine. Install it, put its command line on your PATH, or configure your own command in settings.',
    editor_not_configured:
      'No editor command is configured. Set one in settings, using the placeholders the field describes.',
    editor_launch_failed: 'The editor would not start. See log.txt for details.',
    editor_invalid_diff_command:
      'A comparison command has to say where the two files go. Include {placeholder} in it.',
    editor_invalid_open_command:
      'An open command has to say where the file goes. Include {placeholder} in it.',

    llm_result_rejected:
      'The model’s answer never passed DiffHacker’s checks, even after being asked to repair it. Nothing was saved.',

    documentation_no_profile:
      'Analyse the repository first — documentation is written from its profile.',
    documentation_preview_required:
      'The profile changed since you previewed the documents, so nothing was written. Preview again.',
    documentation_write_failed:
      'The documents could not be written into {path}. Check the folder’s permissions.',
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

  /**
   * Why an analysis run stopped.
   *
   * Nothing renders these yet — Iteration 4 built the provider layer but no screen that runs
   * a conversation. They live here rather than arriving with Iteration 7 because the codes
   * they translate are already the contract (`LlmFailures`), and a code with no message would
   * reach a reader as `llm_context_overflow`.
   */
  runFailure: {
    llm_invalid_key: 'The provider rejected the API key. Check it in settings.',
    llm_forbidden: 'The key is valid but not allowed to use this model.',
    llm_model_not_found:
      'The provider does not recognise “{model}”. Test the connection to see which models this key can reach.',
    llm_context_overflow:
      'The change was too large for this model’s context window. Try a model with a larger one.',
    llm_content_filter: 'The provider’s safety system refused this request.',
    llm_quota_exhausted: 'This account has no credit or quota left. Waiting will not help.',
    llm_rate_limited:
      'The provider is rate-limiting this key. DiffHacker retried and gave up; try again shortly.',
    llm_unreachable: 'The provider could not be reached. Check your connection.',
    llm_timed_out: 'The provider did not answer in time.',
    llm_invalid_response:
      'The model did not answer in the shape DiffHacker asked for, twice. Try a more capable model.',
    llm_budget_exceeded: 'The run hit a limit and stopped. Nothing here is a complete result.',
    llm_unexpected_response: 'The provider returned an unexpected response. See log.txt for details.',
  },
} as const;

export type Catalogue = typeof en;
