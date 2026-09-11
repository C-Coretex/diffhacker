import { create } from 'zustand';
import type {
  AnalysisFreshness,
  AnalysisLibrary,
  AnalysisOptions,
  AnalysisProgress,
  AnalysisTrace,
  AnalysisView,
  BudgetLimitReached,
  ChangesetResult,
  EditorSettings,
  EnvironmentInfo,
  HostInfo,
  ProfileState,
  ProviderProfile,
  RecentRepository,
  RepositoryInfo,
  ToolCallEvent,
} from '@/contracts';
import { rememberPreference, storedPreference, type ThemePreference } from '@/theme/useTheme';
import { rememberMergePreference, storedMergePreference } from '@/graph/mergePreference';

export type ConnectionStatus = 'connecting' | 'connected' | 'detached' | 'error';
export type EnvironmentStatus = 'checking' | 'ready' | 'error';
export type RecentsStatus = 'loading' | 'ready' | 'error';
export type RepositoryStatus = 'none' | 'opening' | 'open' | 'error';
export type ProvidersStatus = 'loading' | 'ready' | 'error';
export type ChangesetStatus = 'idle' | 'loading' | 'ready' | 'error';
export type ProfileStatus = 'idle' | 'loading' | 'ready' | 'error';
export type AnalysisStatus = 'idle' | 'loading' | 'ready' | 'error';

/** Whether a profile run is in flight. Separate from the profile's own load state. */
export type ProfileRunStatus = 'idle' | 'running';

/**
 * Which screen is showing.
 *
 * A discriminant rather than a router: there is no address bar in a desktop window, and the
 * asset resolver serves exact paths with no SPA fallback, so a URL would be state to keep in
 * sync for nothing. Revisit if a later iteration wants deep links into the graph.
 */
export type Screen = 'welcome' | 'repository' | 'settings' | 'profile' | 'analysis';

/**
 * How many tool-log rows a live run keeps.
 *
 * A run may make five hundred calls, each carrying an argument and a result preview. Keeping all
 * of them would grow the render cost of every subsequent event; the oldest are dropped because the
 * interesting end of a live log is the recent end. The stored trace keeps the whole run.
 */
export const TOOL_LOG_LIMIT = 200;

interface AppState {
  connection: ConnectionStatus;
  hostInfo?: HostInfo;
  connectionError?: string;

  screen: Screen;

  /**
   * Light, dark, or whatever the operating system is asking for.
   *
   * Here rather than local to the header because two things need it — the control that sets it and
   * the hook that applies it to `<html>` — and they are on opposite sides of the tree. Remembered
   * in `localStorage`; see `theme/useTheme.ts` for why that rather than the host.
   */
  themePreference: ThemePreference;

  environment: EnvironmentStatus;
  environmentInfo?: EnvironmentInfo;
  environmentError?: string;

  repository: RepositoryStatus;
  repositoryInfo?: RepositoryInfo;
  repositoryError?: string;
  /** Set when the user picked a subdirectory and the host resolved to the worktree root. */
  repositoryNormalizedFrom?: string;

  recents: RecentsStatus;
  recentRepositories: RecentRepository[];
  recentsError?: string;

  providers: ProvidersStatus;
  providerProfiles: ProviderProfile[];
  activeProviderId?: string;
  providersError?: string;

  /**
   * Whether the reviewer dismissed the "no provider configured" prompt this session.
   *
   * Not persisted: a fresh launch with no provider still configured should say so again, the
   * same way `GitMissingBanner` does not remember being dismissed either.
   */
  providersPromptDismissed: boolean;

  changeset: ChangesetStatus;
  changesetResult?: ChangesetResult;
  changesetError?: string;
  /**
   * Requirement 2's toggle, on by default.
   *
   * Session state rather than a setting: excluding untracked files is a momentary "let me see
   * only what I edited", not a standing preference, and it belongs next to the list it changes.
   */
  includeUntracked: boolean;

  profile: ProfileStatus;
  profileState?: ProfileState;
  profileError?: string;

  /**
   * The live run.
   *
   * Held in the store rather than in the screen so that navigating away and back does not lose a
   * run in flight — the notifications keep arriving either way, and a reviewer who opened the
   * changeset to look something up should come back to the log, not to a blank panel.
   */
  profileRun: ProfileRunStatus;
  profileRunProgress?: AnalysisProgress;
  profileRunEvents: ToolCallEvent[];
  profileRunLatest?: ToolCallEvent;

  /**
   * When the run was started, by this renderer's clock, for the elapsed time on the live view. The
   * renderer's own clock rather than an event's timestamp: the first event can take a while to
   * arrive, and a clock that starts then under-reports the wait the reviewer is sitting through.
   */
  profileRunStartedAt?: number;

  /**
   * The last event that carried a context measurement, kept apart from `profileRunLatest`.
   *
   * Only the turn-start and usage events measure the context; a tool_started arriving after one
   * carries none, and reading the meter off "the latest event" would blank it every other row.
   */
  profileRunContext?: ToolCallEvent;

  /**
   * A budget limit the profile run is currently paused at, cleared once the reviewer answers
   * through `run.answerBudgetPrompt`. Undefined the rest of the time.
   */
  profileBudgetPrompt?: BudgetLimitReached;

  analysis: AnalysisStatus;
  analysisView?: AnalysisView;
  analysisError?: string;

  /**
   * The live analysis run, kept separately from the profile's for the same reason the two screens
   * are separate: they are different pieces of work, and a reviewer who started one should not
   * find the other's tool log underneath it.
   */
  analysisRun: ProfileRunStatus;
  analysisRunProgress?: AnalysisProgress;
  analysisRunEvents: ToolCallEvent[];
  analysisRunLatest?: ToolCallEvent;

  /** @see profileRunContext */
  analysisRunContext?: ToolCallEvent;

  /** @see profileBudgetPrompt */
  analysisBudgetPrompt?: BudgetLimitReached;

  /** @see profileRunStartedAt */
  analysisRunStartedAt?: number;

  /**
   * Every stored run of the open repository, most recent first, from `analysis.list`. Undefined
   * until first asked for; refreshed after each run and each delete.
   */
  analysisLibrary?: AnalysisLibrary;

  /**
   * Whether the working tree has moved since the analysis on screen ran. Undefined until a check has
   * answered for *this* analysis — cleared whenever a different one is shown, so a verdict about one
   * run can never sit above another.
   */
  analysisFreshness?: AnalysisFreshness;

  /**
   * The stale prompt the reviewer dismissed, as the analysis id and what differed. A later check that
   * finds something *else* different shows the prompt again: dismissing "one file changed" is not
   * dismissing "now three have".
   */
  analysisStaleDismissed?: string;

  /** The recorded trace of the analysis on screen, fetched when the inspector is first opened. */
  analysisTrace?: AnalysisTrace;

  /**
   * How the graph is being looked at. UI state and nothing else: §0.6 rules out a persisted hand
   * layout, and none of this describes the analysis — it describes this session's view of one.
   * Reset whenever the analysis underneath it changes, because a collapsed-container id from one
   * analysis means nothing in the next.
   */
  graphCollapsed: ReadonlySet<string>;
  graphSearch: string;
  graphFocusedNodeId?: string;
  graphLegendOpen: boolean;
  graphDetailsOpen: boolean;

  /**
   * Projects the reviewer has unchecked in the project filter. Empty means nothing is hidden — the
   * default, and what "show all" restores. Named by what is *hidden* rather than what is shown so an
   * empty set has one unambiguous meaning; a "shown" set would leave unchecking the last project
   * indistinguishable from never having filtered at all.
   *
   * Reset with the rest of `graphDefaults`: a project name from one analysis means nothing in the
   * next. Survives a switch of grouping, like `graphMergeImplementations` — which files belong to
   * which project does not change when the same analysis is redrawn by theme instead of by flow.
   */
  graphHiddenProjects: ReadonlySet<string>;

  /**
   * Whether the overview band is expanded past its summary and risk column.
   *
   * Closed by default. What a reviewer needs on screen at all times is what the change does and
   * what it risks; the reading order, the cluster list, the numbers, the risk register and the
   * run's provenance are worth a toggle each time they are wanted, and the diagram is worth the
   * two hundred pixels they would otherwise take.
   */
  graphOverviewOpen: boolean;

  /**
   * Whether the band's summary and risk column are showing.
   *
   * Open to begin with — the first thing a reviewer wants is what the change does — and foldable,
   * because prose you have already read is the first thing that should give the diagram its height
   * back. Folded, the strip still carries the risk count.
   */
  graphBandOpen: boolean;

  /**
   * `onlyRenderVisibleElements` on the React Flow surface. Off, and staying off: the iteration
   * fixes that decision, and requirement 11 says to profile and fix what is slow rather than
   * reach for this. Here so that profiling can turn it on to compare, not so that it can ship on.
   */
  graphOnlyRenderVisible: boolean;

  /**
   * Whether an abstraction and its implementations are drawn as one box. The reviewer's toggle, and
   * remembered across restarts like the theme — see `graph/mergePreference.ts`. Not in either reset
   * set: it is how someone reads, not what they are reading, so a new analysis or a switch of
   * grouping keeps it.
   */
  graphMergeImplementations: boolean;

  /**
   * Which node the diff panel is showing, and therefore where the reviewer is.
   *
   * Requirement 6's "current position" and requirement 1's "which file is open" are one piece of
   * state, because they are one fact. Separate from `graphFocusedNodeId`, which is what the search box
   * put a ring on: a reviewer can search for a file to see where it sits without leaving the one they
   * are reading, and collapsing the two would make every search a navigation.
   *
   * Undefined means the panel is closed and the diagram has the whole width.
   */
  diffNodeId?: string;

  /**
   * The cluster the reviewer opened *whole*, when they opened one.
   *
   * Opening a container puts every file in it into the panel as a queue — the cluster is the unit a
   * reviewer actually reads, and picking its twelve boxes off the diagram one at a time is the
   * alphabetical file list wearing a diagram's clothes. Set alongside `diffNodeId`, never instead of
   * it: the panel still shows exactly one file, it just knows which list that file came from.
   *
   * Dropped the moment the reviewer follows an edge out of the cluster, because a queue that quietly
   * kept pointing at the cluster they left would be a queue they no longer chose.
   */
  diffContainerId?: string;

  /**
   * Whether the panel has the window to itself.
   *
   * Requirement 6 is why the *splitter* stops short of the left edge: the reviewer's position has to
   * stay visible in the diagram while they drag. This is not that — it is an explicit mode with an
   * obvious way back out, asked for so a wide diff can be read on a laptop, and the diagram is one
   * button away rather than a drag away. Reset when the panel closes.
   */
  diffFullScreen: boolean;

  /**
   * Whether the explanation under the code is unfolded.
   *
   * Kept across nodes rather than reset per file: a reviewer who folded the prose away to give the
   * code the height did not fold away *that file's* prose, they said how they want to read.
   */
  diffExplanationOpen: boolean;

  /**
   * The last external-editor failure, from wherever it was asked for.
   *
   * One field for both surfaces — the buttons on a box and the buttons in the panel — because it is
   * one message about one thing that did not happen, and a box on the diagram has no room to say it.
   */
  editorError?: string;

  /**
   * Ids of the nodes marked reviewed, seeded from the stored analysis and written back through
   * `analysis.setReviewed`. A set rather than an array because the node boxes ask "is this one
   * marked" three hundred times per paint.
   */
  reviewedNodeIds: ReadonlySet<string>;

  /** Set while a mark is in flight, so a failed call can put the box back the way it was. */
  reviewedError?: string;

  /**
   * Width of the diff panel in pixels. Session state, not persisted: §0.6 keeps hand layout out of
   * this product, and a remembered panel width is the same kind of promise for a different surface.
   */
  diffPanelWidth: number;

  /**
   * Height of the explanation strip under the code, in pixels. Same reasoning as `diffPanelWidth`:
   * session state, not persisted, and dragged the same way — the code is what most reviewers want
   * more of, but a long explanation or a wide risk list is worth trading it for on demand.
   */
  diffExplanationHeight: number;

  /** Which external editors are available, from `editor.describe`. Undefined until asked. */
  editors?: EditorSettings;

  /**
   * The parts of an analysis a run asks for unless told otherwise, from `analysis.getDefaults`.
   * Undefined until asked. Settings writes it; the run options on the analysis screen only read it.
   */
  analysisDefaults?: AnalysisOptions;

  setConnected(hostInfo: HostInfo): void;
  setDetached(): void;
  setConnectionError(message: string): void;

  showScreen(screen: Screen): void;
  setThemePreference(preference: ThemePreference): void;

  setEnvironment(info: EnvironmentInfo): void;
  failEnvironment(message: string): void;

  startOpeningRepository(): void;
  setRepository(info: RepositoryInfo, normalizedFrom?: string): void;
  failRepository(message: string): void;
  clearRepositoryError(): void;

  startLoadingRecents(): void;
  setRecents(entries: RecentRepository[]): void;
  failRecents(message: string): void;

  startLoadingProviders(): void;
  setProviders(profiles: ProviderProfile[], activeId?: string): void;
  failProviders(message: string): void;
  dismissProvidersPrompt(): void;

  startLoadingChangeset(): void;
  setChangeset(result: ChangesetResult): void;
  failChangeset(message: string): void;
  setIncludeUntracked(include: boolean): void;

  startLoadingProfile(): void;
  setProfile(state: ProfileState): void;
  failProfile(message: string): void;

  startProfileRun(): void;
  recordProfileProgress(progress: AnalysisProgress): void;
  recordProfileRunEvent(event: ToolCallEvent): void;
  setProfileBudgetPrompt(prompt: BudgetLimitReached | undefined): void;
  endProfileRun(): void;

  startLoadingAnalysis(): void;
  setAnalysis(view: AnalysisView): void;
  failAnalysis(message: string): void;

  startAnalysisRun(): void;
  recordAnalysisProgress(progress: AnalysisProgress): void;
  recordAnalysisRunEvent(event: ToolCallEvent): void;
  setAnalysisBudgetPrompt(prompt: BudgetLimitReached | undefined): void;
  endAnalysisRun(): void;
  setAnalysisLibrary(library: AnalysisLibrary): void;
  setAnalysisFreshness(freshness: AnalysisFreshness): void;
  dismissStaleAnalysis(signature: string): void;
  setAnalysisTrace(trace: AnalysisTrace): void;

  toggleContainerCollapsed(containerId: string): void;
  setAllContainersCollapsed(collapsed: boolean, containerIds: readonly string[]): void;
  setGraphSearch(query: string): void;
  focusGraphNode(nodeId: string | undefined): void;
  revealGraphNode(nodeId: string, containerId: string): void;
  setGraphLegendOpen(open: boolean): void;
  setGraphDetailsOpen(open: boolean): void;
  setGraphOverviewOpen(open: boolean): void;
  setGraphBandOpen(open: boolean): void;
  setGraphOnlyRenderVisible(enabled: boolean): void;
  setGraphMergeImplementations(merge: boolean): void;
  toggleProjectHidden(project: string): void;
  showAllProjects(): void;

  openDiffFor(nodeId: string, containerId: string): void;
  openContainerDiff(containerId: string, firstNodeId: string): void;
  leaveContainerQueue(): void;
  closeDiff(): void;
  setReviewed(nodeIds: readonly string[], reviewed: boolean): void;
  applyReviewedState(nodeIds: readonly string[]): void;
  failReviewed(message: string | undefined): void;
  setDiffPanelWidth(width: number): void;
  setDiffExplanationHeight(height: number): void;
  setDiffFullScreen(fullScreen: boolean): void;
  setDiffExplanationOpen(open: boolean): void;
  setEditorError(message: string | undefined): void;
  setEditors(editors: EditorSettings): void;
  setAnalysisDefaults(defaults: AnalysisOptions): void;
}

/**
 * How wide the diff panel opens, and how narrow it may be dragged.
 *
 * `DIFF_PANEL_MIN_GRAPH` is requirement 10, as a number. The iteration's fixed decisions say the panel
 * "can expand to full width", and its own verification step asks that the current position stay
 * visible **in the graph** at all times, including then — so "full width" is as wide as it goes while
 * the diagram keeps a rail, and the rail is kept centred on the node being read. A panel that covered
 * the diagram entirely would satisfy one of those sentences by breaking the other.
 */
export const DIFF_PANEL_DEFAULT_WIDTH = 720;
export const DIFF_PANEL_MIN_WIDTH = 420;
export const DIFF_PANEL_MIN_GRAPH = 260;

/**
 * How tall the explanation strip under the diff opens, and how it may be dragged.
 *
 * `DIFF_EXPLANATION_MIN_CODE` is this feature's own version of `DIFF_PANEL_MIN_GRAPH`: the code is
 * the reason the panel is open at all, so the tallest the explanation goes still leaves that much of
 * Monaco on screen.
 */
export const DIFF_EXPLANATION_DEFAULT_HEIGHT = 260;
export const DIFF_EXPLANATION_MIN_HEIGHT = 120;
export const DIFF_EXPLANATION_MIN_CODE = 160;

/** The graph view's own state, in one place so both reset paths use the same words. */
const graphDefaults = {
  graphCollapsed: new Set<string>() as ReadonlySet<string>,
  graphSearch: '',
  graphFocusedNodeId: undefined,
  graphLegendOpen: false,
  graphDetailsOpen: false,
  graphHiddenProjects: new Set<string>() as ReadonlySet<string>,
  graphOverviewOpen: false,
  graphBandOpen: true,
  diffNodeId: undefined,
  diffContainerId: undefined,
  diffFullScreen: false,
  reviewedError: undefined,
  editorError: undefined,
} as const;

/**
 * The subset of that which is keyed to a *container*, for a change of grouping mode.
 *
 * Switching grouping keeps the same analysis and the same nodes, so most of the view state is still
 * meaningful — but a collapsed set and an open cluster queue hold container ids, and the other
 * grouping's containers are different clusters with different ids. Carrying them across would fold
 * whichever cluster happened to share an id and leave the reviewer in a queue that no longer exists.
 *
 * Everything node-keyed survives deliberately: the file open in the diff panel, the reviewed marks
 * (requirement 6), the panel width and the band folds. A reviewer who switches grouping while
 * reading a file is still reading that file.
 */
const groupingDefaults = {
  graphCollapsed: new Set<string>() as ReadonlySet<string>,
  graphFocusedNodeId: undefined,
  diffContainerId: undefined,
} as const;

export const useAppStore = create<AppState>((set) => ({
  connection: 'connecting',

  screen: 'welcome',
  themePreference: storedPreference(),

  environment: 'checking',

  repository: 'none',

  recents: 'loading',
  recentRepositories: [],

  providers: 'loading',
  providerProfiles: [],
  providersPromptDismissed: false,

  changeset: 'idle',
  includeUntracked: true,

  profile: 'idle',
  profileRun: 'idle',
  profileRunEvents: [],

  analysis: 'idle',
  analysisRun: 'idle',
  analysisRunEvents: [],

  ...graphDefaults,
  graphOnlyRenderVisible: false,
  graphMergeImplementations: storedMergePreference(),

  // Not in graphDefaults, and deliberately: the marks are not this session's view of an analysis, they
  // are part of it. setAnalysis seeds them from what the host stored rather than clearing them.
  reviewedNodeIds: new Set<string>() as ReadonlySet<string>,
  diffPanelWidth: DIFF_PANEL_DEFAULT_WIDTH,
  diffExplanationHeight: DIFF_EXPLANATION_DEFAULT_HEIGHT,

  // Not in graphDefaults either: this one is how the reviewer reads, not what they are reading.
  diffExplanationOpen: true,

  setConnected: (hostInfo) => set({ connection: 'connected', hostInfo, connectionError: undefined }),
  setDetached: () => set({ connection: 'detached' }),
  setConnectionError: (message) => set({ connection: 'error', connectionError: message }),

  showScreen: (screen) => set({ screen }),

  setThemePreference: (themePreference) => {
    rememberPreference(themePreference);
    set({ themePreference });
  },

  setEnvironment: (environmentInfo) =>
    set({ environment: 'ready', environmentInfo, environmentError: undefined }),
  failEnvironment: (message) => set({ environment: 'error', environmentError: message }),

  startOpeningRepository: () => set({ repository: 'opening', repositoryError: undefined }),

  setRepository: (repositoryInfo, normalizedFrom) =>
    set({
      repository: 'open',
      repositoryInfo,
      repositoryError: undefined,
      repositoryNormalizedFrom: normalizedFrom,
      screen: 'repository',
      // A changeset belongs to the repository it came from. Leaving the previous one visible
      // while the new one loads would show a reviewer another project's files.
      changeset: 'idle',
      changesetResult: undefined,
      changesetError: undefined,
      // The profile belongs to a repository too. Showing the previous repository's architecture
      // beside this one's files would be worse than showing nothing.
      profile: 'idle',
      profileState: undefined,
      profileError: undefined,
      profileRunProgress: undefined,
      profileRunEvents: [],
      profileRunLatest: undefined,
      profileRunContext: undefined,
      profileBudgetPrompt: undefined,
      // And so does the analysis: it describes one repository's uncommitted change and means
      // nothing beside another's.
      analysis: 'idle',
      analysisView: undefined,
      analysisError: undefined,
      analysisRunProgress: undefined,
      analysisRunEvents: [],
      analysisRunLatest: undefined,
      analysisRunContext: undefined,
      analysisBudgetPrompt: undefined,
      analysisLibrary: undefined,
      analysisFreshness: undefined,
      analysisStaleDismissed: undefined,
      analysisTrace: undefined,
      ...graphDefaults,
    }),

  failRepository: (message) => set({ repository: 'none', repositoryError: message }),
  clearRepositoryError: () => set({ repositoryError: undefined }),

  startLoadingRecents: () => set({ recents: 'loading', recentsError: undefined }),
  setRecents: (recentRepositories) =>
    set({ recents: 'ready', recentRepositories, recentsError: undefined }),
  failRecents: (message) => set({ recents: 'error', recentsError: message }),

  startLoadingProviders: () => set({ providers: 'loading', providersError: undefined }),
  setProviders: (providerProfiles, activeProviderId) =>
    set({ providers: 'ready', providerProfiles, activeProviderId, providersError: undefined }),
  failProviders: (message) => set({ providers: 'error', providersError: message }),
  dismissProvidersPrompt: () => set({ providersPromptDismissed: true }),

  startLoadingChangeset: () => set({ changeset: 'loading', changesetError: undefined }),
  setChangeset: (changesetResult) =>
    set({ changeset: 'ready', changesetResult, changesetError: undefined }),
  failChangeset: (message) => set({ changeset: 'error', changesetError: message }),
  setIncludeUntracked: (includeUntracked) => set({ includeUntracked }),

  startLoadingProfile: () => set({ profile: 'loading', profileError: undefined }),
  setProfile: (profileState) => set({ profile: 'ready', profileState, profileError: undefined }),
  failProfile: (message) => set({ profile: 'error', profileError: message }),

  startProfileRun: () =>
    set({
      profileRun: 'running',
      profileRunProgress: undefined,
      profileRunEvents: [],
      profileRunLatest: undefined,
      profileRunContext: undefined,
      profileRunStartedAt: Date.now(),
      profileBudgetPrompt: undefined,
    }),

  recordProfileProgress: (profileRunProgress) => set({ profileRunProgress }),

  recordProfileRunEvent: (event) =>
    set((state) => {
      const events = isToolLogRow(event) ? [...state.profileRunEvents, event] : state.profileRunEvents;

      return {
        profileRunEvents: events.length > TOOL_LOG_LIMIT ? events.slice(-TOOL_LOG_LIMIT) : events,
        profileRunLatest: event,
        profileRunContext: carriesContext(event) ? event : state.profileRunContext,
      };
    }),

  setProfileBudgetPrompt: (profileBudgetPrompt) => set({ profileBudgetPrompt }),

  endProfileRun: () => set({ profileRun: 'idle', profileBudgetPrompt: undefined }),

  startLoadingAnalysis: () => set({ analysis: 'loading', analysisError: undefined }),
  setAnalysis: (analysisView) =>
    set((state) => ({
      analysis: 'ready',
      analysisView,
      analysisError: undefined,

      // A different analysis is a different graph. Carrying the collapsed set across would hide
      // clusters the reviewer never collapsed, using ids that happen to match; carrying the search
      // across would highlight a file that is no longer in the change.
      ...(state.analysisView?.analysisId === analysisView.analysisId ? {} : graphDefaults),

      // And what was learned about the old one — whether it was stale, what its trace was — is not
      // true of the new one, so it goes until the new one is asked about.
      ...(state.analysisView?.analysisId === analysisView.analysisId
        ? {}
        : { analysisFreshness: undefined, analysisTrace: undefined }),

      // The same analysis in the other grouping is the same nodes in different clusters, so only
      // the container-keyed part of the view is stale. See groupingDefaults.
      ...(state.analysisView?.analysisId === analysisView.analysisId &&
      state.analysisView?.grouping !== analysisView.grouping
        ? groupingDefaults
        : {}),

      // The marks come from the host every time, in both branches. They are the one thing on this
      // screen the reviewer authored, and the stored analysis is the only authority on them — a
      // re-read that kept a local set would show marks a restart would then lose.
      reviewedNodeIds: new Set(analysisView.reviewedNodeIds),
    })),
  failAnalysis: (message) => set({ analysis: 'error', analysisError: message }),

  startAnalysisRun: () =>
    set({
      analysisRun: 'running',
      analysisRunProgress: undefined,
      analysisRunEvents: [],
      analysisRunLatest: undefined,
      analysisRunContext: undefined,
      analysisRunStartedAt: Date.now(),
      analysisBudgetPrompt: undefined,
    }),

  recordAnalysisProgress: (analysisRunProgress) => set({ analysisRunProgress }),

  recordAnalysisRunEvent: (event) =>
    set((state) => {
      const events = isToolLogRow(event) ? [...state.analysisRunEvents, event] : state.analysisRunEvents;

      return {
        analysisRunEvents: events.length > TOOL_LOG_LIMIT ? events.slice(-TOOL_LOG_LIMIT) : events,
        analysisRunContext: carriesContext(event) ? event : state.analysisRunContext,
        analysisRunLatest: event,
      };
    }),

  setAnalysisBudgetPrompt: (analysisBudgetPrompt) => set({ analysisBudgetPrompt }),

  endAnalysisRun: () => set({ analysisRun: 'idle', analysisBudgetPrompt: undefined }),

  setAnalysisLibrary: (analysisLibrary) => set({ analysisLibrary }),

  // Dropped rather than stored when it answers for an analysis no longer on screen: a check started
  // before the reviewer reopened another run finishes after it, and its verdict is about the old one.
  setAnalysisFreshness: (analysisFreshness) =>
    set((state) =>
      state.analysisView?.analysisId === analysisFreshness.analysisId ? { analysisFreshness } : {},
    ),

  dismissStaleAnalysis: (analysisStaleDismissed) => set({ analysisStaleDismissed }),

  setAnalysisTrace: (analysisTrace) =>
    set((state) =>
      state.analysisView?.analysisId === analysisTrace.analysisId ? { analysisTrace } : {},
    ),

  toggleContainerCollapsed: (containerId) =>
    set((state) => {
      const next = new Set(state.graphCollapsed);
      if (!next.delete(containerId)) next.add(containerId);
      return { graphCollapsed: next };
    }),

  setAllContainersCollapsed: (collapsed, containerIds) =>
    set({ graphCollapsed: collapsed ? new Set(containerIds) : new Set<string>() }),

  setGraphSearch: (graphSearch) =>
    // Clearing the box clears the focus with it. A ring left pulsing on a node nobody searched for
    // any more is a highlight with no explanation on the screen.
    set(graphSearch.length === 0 ? { graphSearch, graphFocusedNodeId: undefined } : { graphSearch }),

  focusGraphNode: (graphFocusedNodeId) => set({ graphFocusedNodeId }),

  // Expanding first is not a nicety: centring on a node inside a folded cluster pans to an empty
  // patch of canvas and looks broken. Every way of saying "take me to this file" — the search box,
  // the reading order, the cluster list, the risk register — goes through here so all of them
  // behave the same.
  revealGraphNode: (nodeId, containerId) =>
    set((state) => {
      const next = new Set(state.graphCollapsed);
      next.delete(containerId);
      return { graphCollapsed: next, graphFocusedNodeId: nodeId };
    }),
  setGraphLegendOpen: (graphLegendOpen) => set({ graphLegendOpen }),
  setGraphDetailsOpen: (graphDetailsOpen) => set({ graphDetailsOpen }),
  setGraphOverviewOpen: (graphOverviewOpen) => set({ graphOverviewOpen }),
  setGraphBandOpen: (graphBandOpen) => set({ graphBandOpen }),
  setGraphOnlyRenderVisible: (graphOnlyRenderVisible) => set({ graphOnlyRenderVisible }),

  setGraphMergeImplementations: (graphMergeImplementations) => {
    rememberMergePreference(graphMergeImplementations);
    set({ graphMergeImplementations });
  },

  toggleProjectHidden: (project) =>
    set((state) => {
      const next = new Set(state.graphHiddenProjects);
      if (!next.delete(project)) next.add(project);
      return { graphHiddenProjects: next };
    }),

  showAllProjects: () => set({ graphHiddenProjects: new Set<string>() }),

  // Opening a diff is also a navigation, so it expands the container and centres the diagram the same
  // way every other "take me to this file" does. Requirement 6 is the other half of the same set: the
  // node the panel is showing is the node the diagram rings as current.
  openDiffFor: (nodeId, containerId) =>
    set((state) => {
      const next = new Set(state.graphCollapsed);
      next.delete(containerId);

      return {
        graphCollapsed: next,
        graphFocusedNodeId: nodeId,
        diffNodeId: nodeId,
        // Following an edge out of the cluster the reviewer opened whole ends that queue. Keeping it
        // would leave the strip pointing at a list the panel is no longer walking.
        diffContainerId: state.diffContainerId === containerId ? containerId : undefined,
        reviewedError: undefined,
        editorError: undefined,
      };
    }),

  // Opening a cluster is opening its first file *and* remembering the list. Everything else — the
  // fold, the focus ring, the centring — is what opening any file does, because it is one.
  openContainerDiff: (containerId, firstNodeId) =>
    set((state) => {
      const next = new Set(state.graphCollapsed);
      next.delete(containerId);

      return {
        graphCollapsed: next,
        graphFocusedNodeId: firstNodeId,
        diffNodeId: firstNodeId,
        diffContainerId: containerId,
        reviewedError: undefined,
        editorError: undefined,
      };
    }),

  // Puts the queue down without closing the file being read. "Next" goes back to meaning the next
  // file in the whole change, which is what the reviewer is asking for by leaving.
  leaveContainerQueue: () => set({ diffContainerId: undefined }),

  closeDiff: () =>
    set({ diffNodeId: undefined, diffContainerId: undefined, diffFullScreen: false }),

  // Applied before the call is made, so the checkbox answers the click rather than the network. The
  // host's answer replaces the whole set through applyReviewedState, and a failure puts it back.
  setReviewed: (nodeIds, reviewed) =>
    set((state) => {
      const next = new Set(state.reviewedNodeIds);

      for (const nodeId of nodeIds) {
        if (reviewed) next.add(nodeId);
        else next.delete(nodeId);
      }

      return { reviewedNodeIds: next, reviewedError: undefined };
    }),

  applyReviewedState: (nodeIds) => set({ reviewedNodeIds: new Set(nodeIds) }),

  failReviewed: (reviewedError) => set({ reviewedError }),

  setDiffPanelWidth: (diffPanelWidth) => set({ diffPanelWidth }),

  setDiffExplanationHeight: (diffExplanationHeight) => set({ diffExplanationHeight }),

  setDiffFullScreen: (diffFullScreen) => set({ diffFullScreen }),

  setDiffExplanationOpen: (diffExplanationOpen) => set({ diffExplanationOpen }),

  setEditorError: (editorError) => set({ editorError }),

  setEditors: (editors) => set({ editors }),

  setAnalysisDefaults: (analysisDefaults) => set({ analysisDefaults }),
}));

/**
 * Whether an event earns a row in the tool log.
 *
 * Turn and usage events move the running totals without adding one: the log is about what the
 * model did and said, and "turn 14 started" is not something it did.
 */
function isToolLogRow(event: ToolCallEvent): boolean {
  return (
    event.kind === 'tool_started' ||
    event.kind === 'tool_finished' ||
    event.kind === 'retry' ||
    event.kind === 'assistant_message'
  );
}

/**
 * Whether an event measured the context.
 *
 * The instruction count is the tell: it is set on every event the host measured and absent on every
 * event it did not, and unlike the token count it is never legitimately missing when a measurement
 * did happen — a run always has a system prompt, and a provider that reports no usage still has one.
 */
function carriesContext(event: ToolCallEvent): boolean {
  return event.contextInstructionsCharacters !== undefined;
}
