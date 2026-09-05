import { create } from 'zustand';
import type { HostInfo, ProgressNotification } from '@/contracts';

export type ConnectionStatus = 'connecting' | 'connected' | 'detached' | 'error';
export type DemoStatus = 'idle' | 'running' | 'done' | 'error';

interface AppState {
  connection: ConnectionStatus;
  hostInfo?: HostInfo;
  connectionError?: string;

  demo: DemoStatus;
  demoError?: string;
  progress: ProgressNotification[];

  setConnected(hostInfo: HostInfo): void;
  setDetached(): void;
  setConnectionError(message: string): void;

  startDemo(): void;
  recordProgress(notification: ProgressNotification): void;
  failDemo(message: string): void;
}

export const useAppStore = create<AppState>((set) => ({
  connection: 'connecting',
  demo: 'idle',
  progress: [],

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
}));
