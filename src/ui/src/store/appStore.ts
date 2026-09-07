import { create } from 'zustand';
import type {
  AnalysisProgress,
  ChangesetResult,
  EnvironmentInfo,
  HostInfo,
  ProfileState,
  ProviderProfile,
  RecentRepository,
  RepositoryInfo,
  ToolCallEvent,
} from '@/contracts';

export type ConnectionStatus = 'connecting' | 'connected' | 'detached' | 'error';
export type EnvironmentStatus = 'checking' | 'ready' | 'error';
export type RecentsStatus = 'loading' | 'ready' | 'error';
export type RepositoryStatus = 'none' | 'opening' | 'open' | 'error';
export type ProvidersStatus = 'loading' | 'ready' | 'error';
export type ChangesetStatus = 'idle' | 'loading' | 'ready' | 'error';
export type ProfileStatus = 'idle' | 'loading' | 'ready' | 'error';

/** Whether a profile run is in flight. Separate from the profile's own load state. */
export type ProfileRunStatus = 'idle' | 'running';

/**
 * Which screen is showing.
 *
 * A discriminant rather than a router: there is no address bar in a desktop window, and the
 * asset resolver serves exact paths with no SPA fallback, so a URL would be state to keep in
 * sync for nothing. Revisit if a later iteration wants deep links into the graph.
 */
export type Screen = 'welcome' | 'repository' | 'settings' | 'profile';

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

  setConnected(hostInfo: HostInfo): void;
  setDetached(): void;
  setConnectionError(message: string): void;

  showScreen(screen: Screen): void;

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
}

export const useAppStore = create<AppState>((set) => ({
  connection: 'connecting',

  screen: 'welcome',

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

  setConnected: (hostInfo) => set({ connection: 'connected', hostInfo, connectionError: undefined }),
  setDetached: () => set({ connection: 'detached' }),
  setConnectionError: (message) => set({ connection: 'error', connectionError: message }),

  showScreen: (screen) => set({ screen }),

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
    }),

  recordProfileProgress: (profileRunProgress) => set({ profileRunProgress }),

  recordProfileRunEvent: (event) =>
    set((state) => {
      // Turn and usage events move the running totals without adding a row: the log is about what
      // the model did, and "turn 14 started" is not something it did.
      const isRow = event.kind === 'tool_started' || event.kind === 'tool_finished' || event.kind === 'retry';
      const events = isRow ? [...state.profileRunEvents, event] : state.profileRunEvents;

      return {
        profileRunEvents: events.length > TOOL_LOG_LIMIT ? events.slice(-TOOL_LOG_LIMIT) : events,
        profileRunLatest: event,
      };
    }),

  endProfileRun: () => set({ profileRun: 'idle' }),
}));
