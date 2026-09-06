import type {
  BrowseFolderRequest,
  BrowseFolderResult,
  ChangesetRequest,
  ChangesetResult,
  EnvironmentInfo,
  FileContentInfo,
  FileContentRequest,
  FileDiffInfo,
  FileDiffRequest,
  ForgetRecentRequest,
  HostInfo,
  OpenRepositoryRequest,
  OpenRepositoryResult,
  ProviderIdRequest,
  ProviderProfileList,
  RecentRepositoryList,
  SaveProviderRequest,
  TestConnectionResult,
} from '@/contracts';
import type { RpcClient } from './client';

/**
 * Loading a changeset runs several full `git diff` passes. On a cold, very large working tree
 * that is minutes, not milliseconds, and the default request deadline would report a timeout
 * for work that was going to finish.
 */
const CHANGESET_TIMEOUT_MS = 5 * 60_000;

/**
 * The host's method surface, typed from the generated contracts.
 *
 * Every name here matches a `[JsonRpcMethod]` attribute in `DiffHacker.Host`. Adding a method
 * on one side without the other is a compile error on this side and a missing-method error on
 * the wire, which is the intended failure mode.
 */
export const RpcMethods = {
  ping: 'host.ping',

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

  loadChangeset: 'changeset.load',
  fileDiff: 'changeset.fileDiff',
  fileContent: 'changeset.fileContent',
} as const;

/**
 * Server-to-client notification names.
 *
 * Empty for now. `RpcClient.on` is still the way to subscribe to one; Iteration 5's
 * `report_progress` is the first notification the application will actually receive.
 */
export const RpcNotifications = {} as const;

export function ping(client: RpcClient): Promise<HostInfo> {
  return client.call<HostInfo>(RpcMethods.ping);
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

/**
 * The working tree against HEAD. Metadata for every changed file and no content at all, so the
 * payload stays bounded whether the change is ten files or fifteen hundred.
 */
export function loadChangeset(client: RpcClient, request: ChangesetRequest): Promise<ChangesetResult> {
  return client.callWithTimeout<ChangesetResult>(
    RpcMethods.loadChangeset,
    CHANGESET_TIMEOUT_MS,
    request,
  );
}

export function fileDiff(client: RpcClient, request: FileDiffRequest): Promise<FileDiffInfo> {
  return client.call<FileDiffInfo>(RpcMethods.fileDiff, request);
}

export function fileContent(client: RpcClient, request: FileContentRequest): Promise<FileContentInfo> {
  return client.call<FileContentInfo>(RpcMethods.fileContent, request);
}
