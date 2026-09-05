import { create } from 'zustand';
import type {
  EnvironmentInfo,
  HostInfo,
  ProgressNotification,
  ProviderProfile,
  RecentRepository,
  RepositoryInfo,
} from '@/contracts';

export type ConnectionStatus = 'connecting' | 'connected' | 'detached' | 'error';
export type DemoStatus = 'idle' | 'running' | 'done' | 'error';
export type EnvironmentStatus = 'checking' | 'ready' | 'error';
export type RecentsStatus = 'loading' | 'ready' | 'error';
export type RepositoryStatus = 'none' | 'opening' | 'open' | 'error';
export type ProvidersStatus = 'loading' | 'ready' | 'error';

/**
 * Which screen is showing.
 *
 * A discriminant rather than a router: there is no address bar in a desktop window, and the
 * asset resolver serves exact paths with no SPA fallback, so a URL would be state to keep in
 * sync for nothing. Revisit if a later iteration wants deep links into the graph.
 */
export type Screen = 'welcome' | 'repository' | 'settings';

interface AppState {
  connection: ConnectionStatus;
  hostInfo?: HostInfo;
  connectionError?: string;

  demo: DemoStatus;
  demoError?: string;
  progress: ProgressNotification[];

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

  setConnected(hostInfo: HostInfo): void;
  setDetached(): void;
  setConnectionError(message: string): void;

  startDemo(): void;
  recordProgress(notification: ProgressNotification): void;
  failDemo(message: string): void;

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
}

export const useAppStore = create<AppState>((set) => ({
  connection: 'connecting',
  demo: 'idle',
  progress: [],

  screen: 'welcome',

  environment: 'checking',

  repository: 'none',

  recents: 'loading',
  recentRepositories: [],

  providers: 'loading',
  providerProfiles: [],

  setConnected: (hostInfo) => set({ connection: 'connected', hostInfo, connectionError: undefined }),
  setDetached: () => set({ connection: 'detached' }),
  setConnectionError: (message) => set({ connection: 'error', connectionError: message }),

  startDemo: () => set({ demo: 'running', demoError: undefined, progress: [] }),

  recordProgress: (notification) =>
    set((state) => ({
      progress: [...state.progress, notification],
      demo: notification.completed ? 'done' : state.demo,
    })),

  failDemo: (message) => set({ demo: 'error', demoError: message }),

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
}));
