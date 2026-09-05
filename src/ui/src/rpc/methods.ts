import type {
  BrowseFolderRequest,
  BrowseFolderResult,
  EnvironmentInfo,
  ForgetRecentRequest,
  HostInfo,
  OpenRepositoryRequest,
  OpenRepositoryResult,
  ProgressNotification,
  ProviderIdRequest,
  ProviderProfileList,
  RecentRepositoryList,
  SaveProviderRequest,
  SelfTestResult,
  StartDemoRequest,
  StartDemoResponse,
  TestConnectionResult,
} from '@/contracts';
import type { RpcClient } from './client';

/**
 * The host's method surface, typed from the generated contracts.
 *
 * Every name here matches a `[JsonRpcMethod]` attribute in `DiffHacker.Host`. Adding a method
 * on one side without the other is a compile error on this side and a missing-method error on
 * the wire, which is the intended failure mode.
 */
export const RpcMethods = {
  ping: 'host.ping',
  reportSelfTest: 'host.reportSelfTest',
  startCountdown: 'demo.startCountdown',

  describeEnvironment: 'environment.describe',

  browseForRepository: 'repository.browse',
  openRepository: 'repository.open',
  listRecentRepositories: 'repository.listRecent',
  forgetRecentRepository: 'repository.forgetRecent',

  listProviders: 'providers.list',
  saveProvider: 'providers.save',
  deleteProvider: 'providers.delete',
  setActiveProvider: 'providers.setActive',
  testProviderConnection: 'providers.testConnection',
} as const;

export const RpcNotifications = {
  progress: 'demo/progress',
} as const;

export function ping(client: RpcClient): Promise<HostInfo> {
  return client.call<HostInfo>(RpcMethods.ping);
}

export function startCountdown(client: RpcClient, request: StartDemoRequest): Promise<StartDemoResponse> {
  return client.call<StartDemoResponse>(RpcMethods.startCountdown, request);
}

export function reportSelfTest(client: RpcClient, result: SelfTestResult): Promise<void> {
  return client.call<void>(RpcMethods.reportSelfTest, result);
}

export function onProgress(client: RpcClient, handler: (notification: ProgressNotification) => void): () => void {
  return client.on<ProgressNotification>(RpcNotifications.progress, handler);
}

export function describeEnvironment(client: RpcClient): Promise<EnvironmentInfo> {
  return client.call<EnvironmentInfo>(RpcMethods.describeEnvironment);
}

export function browseForRepository(
  client: RpcClient,
  request: BrowseFolderRequest,
): Promise<BrowseFolderResult> {
  return client.call<BrowseFolderResult>(RpcMethods.browseForRepository, request);
}

export function openRepository(
  client: RpcClient,
  request: OpenRepositoryRequest,
): Promise<OpenRepositoryResult> {
  return client.call<OpenRepositoryResult>(RpcMethods.openRepository, request);
}

export function listRecentRepositories(client: RpcClient): Promise<RecentRepositoryList> {
  return client.call<RecentRepositoryList>(RpcMethods.listRecentRepositories);
}

export function forgetRecentRepository(
  client: RpcClient,
  request: ForgetRecentRequest,
): Promise<void> {
  return client.call<void>(RpcMethods.forgetRecentRepository, request);
}

export function listProviders(client: RpcClient): Promise<ProviderProfileList> {
  return client.call<ProviderProfileList>(RpcMethods.listProviders);
}

/**
 * The only call in the application that carries an API key, and it carries it one way. The
 * host writes it to the secret store and no response ever contains it (CLAUDE.md §0.2.13).
 */
export function saveProvider(
  client: RpcClient,
  request: SaveProviderRequest,
): Promise<ProviderProfileList> {
  return client.call<ProviderProfileList>(RpcMethods.saveProvider, request);
}

export function deleteProvider(
  client: RpcClient,
  request: ProviderIdRequest,
): Promise<ProviderProfileList> {
  return client.call<ProviderProfileList>(RpcMethods.deleteProvider, request);
}

export function setActiveProvider(
  client: RpcClient,
  request: ProviderIdRequest,
): Promise<ProviderProfileList> {
  return client.call<ProviderProfileList>(RpcMethods.setActiveProvider, request);
}

export function testProviderConnection(
  client: RpcClient,
  request: ProviderIdRequest,
): Promise<TestConnectionResult> {
  return client.call<TestConnectionResult>(RpcMethods.testProviderConnection, request);
}
