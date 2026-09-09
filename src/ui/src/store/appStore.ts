import { create } from 'zustand';
import type {
  AnalysisProgress,
  AnalysisView,
  ChangesetResult,
  EnvironmentInfo,
  HostInfo,
  ProfileState,
  ProviderProfile,
  RecentRepository,
  RepositoryInfo,
  ToolCallEvent,
} from '@/contracts';
import { rememberPreference, storedPreference, type ThemePreference } from '@/theme/useTheme';

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
   * The last event that carried a context measurement, kept apart from `profileRunLatest`.
   *
   * Only the turn-start and usage events measure the context; a tool_started arriving after one
   * carries none, and reading the meter off "the latest event" would blank it every other row.
   */
  profileRunContext?: ToolCallEvent;

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
  endProfileRun(): void;

  startLoadingAnalysis(): void;
  setAnalysis(view: AnalysisView): void;
  failAnalysis(message: string): void;

  startAnalysisRun(): void;
  recordAnalysisProgress(progress: AnalysisProgress): void;
  recordAnalysisRunEvent(event: ToolCallEvent): void;
  endAnalysisRun(): void;

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
}

/** The graph view's own state, in one place so both reset paths use the same words. */
const graphDefaults = {
  graphCollapsed: new Set<string>() as ReadonlySet<string>,
  graphSearch: '',
  graphFocusedNodeId: undefined,
  graphLegendOpen: false,
  graphDetailsOpen: false,
  graphOverviewOpen: false,
  graphBandOpen: true,
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
      // And so does the analysis: it describes one repository's uncommitted change and means
      // nothing beside another's.
      analysis: 'idle',
      analysisView: undefined,
      analysisError: undefined,
      analysisRunProgress: undefined,
      analysisRunEvents: [],
      analysisRunLatest: undefined,
      analysisRunContext: undefined,
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

  endProfileRun: () => set({ profileRun: 'idle' }),

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
    })),
  failAnalysis: (message) => set({ analysis: 'error', analysisError: message }),

  startAnalysisRun: () =>
    set({
      analysisRun: 'running',
      analysisRunProgress: undefined,
      analysisRunEvents: [],
      analysisRunLatest: undefined,
      analysisRunContext: undefined,
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

  endAnalysisRun: () => set({ analysisRun: 'idle' }),

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
}));

/**
 * Whether an event earns a row in the tool log.
 *
 * Turn and usage events move the running totals without adding one: the log is about what the
 * model did, and "turn 14 started" is not something it did.
 */
function isToolLogRow(event: ToolCallEvent): boolean {
  return event.kind === 'tool_started' || event.kind === 'tool_finished' || event.kind === 'retry';
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
