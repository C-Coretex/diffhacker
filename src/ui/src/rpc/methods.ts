import type {
  AnalysisFreshness,
  AnalysisLibrary,
  AnalysisProgress,
  AnalysisRefRequest,
  AnalysisRequest,
  AnalysisTrace,
  AnalysisView,
  AnswerBudgetPromptRequest,
  BrowseFolderRequest,
  BrowseFolderResult,
  BudgetLimitReached,
  ChangesetRequest,
  ChangesetResult,
  DocumentationExportRequest,
  DocumentationExportResult,
  DocumentationPreview,
  DocumentationRequest,
  EditorSettings,
  EnvironmentInfo,
  FileContentInfo,
  FileContentRequest,
  FileDiffInfo,
  FileDiffRequest,
  ForgetRecentRequest,
  HostInfo,
  OpenInEditorRequest,
  OpenRepositoryRequest,
  OpenRepositoryResult,
  ProfileRequest,
  ProfileState,
  ProviderIdRequest,
  ProviderProfileList,
  RecentRepositoryList,
  ReviewedState,
  SaveEditorSettingsRequest,
  SaveProfileNotesRequest,
  SaveProfileRequest,
  SaveProviderRequest,
  SetGroupingRequest,
  SetNodesReviewedRequest,
  TestConnectionRequest,
  TestConnectionResult,
  ToolCallEvent,
} from '@/contracts';
import type { RpcClient } from './client';

/**
 * Loading a changeset runs several full `git diff` passes. On a cold, very large working tree
 * that is minutes, not milliseconds, and the default request deadline would report a timeout
 * for work that was going to finish.
 */
const CHANGESET_TIMEOUT_MS = 5 * 60_000;

/**
 * Profiling a repository is an LLM run: hundreds of tool calls, several minutes on a large
 * codebase, and real money. The deadline is generous because giving up on a run that is still
 * working wastes everything it has spent so far; the user cancels when they want it stopped,
 * which is what the abort signal is for.
 */
const PROFILE_TIMEOUT_MS = 30 * 60_000;

/**
 * Longer than a profile's, because the work is larger by construction: a profile reads a
 * repository once, an analysis reads a change that §0.2.10 allows to be fifteen hundred files.
 * The user can stop it at any point, so this is the ceiling on a run nobody is watching rather
 * than a promise about how long one takes.
 */
const ANALYSIS_TIMEOUT_MS = 60 * 60_000;

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

  getProfile: 'profile.get',
  generateProfile: 'profile.generate',
  saveProfileDocument: 'profile.saveDocument',
  saveProfileNotes: 'profile.saveNotes',
  deleteProfile: 'profile.delete',
  previewDocumentation: 'profile.previewDocumentation',
  exportDocumentation: 'profile.exportDocumentation',
  getAnalysis: 'analysis.get',
  runAnalysis: 'analysis.run',
  setNodesReviewed: 'analysis.setReviewed',
  setGrouping: 'analysis.setGrouping',
  listAnalyses: 'analysis.list',
  getAnalysisTrace: 'analysis.trace',
  checkAnalysisFreshness: 'analysis.checkFreshness',
  deleteAnalysis: 'analysis.delete',

  describeEditors: 'editor.describe',
  saveEditorSettings: 'editor.save',
  openInEditor: 'editor.open',

  answerBudgetPrompt: 'run.answerBudgetPrompt',
} as const;

/**
 * Server-to-client notification names.
 *
 * Every name here matches a method the host passes to `IRpcNotifier.NotifyAsync`. Subscribe with
 * `RpcClient.on`, which returns its own unsubscribe.
 *
 * `analysisProgress` carries the toolbox's `report_progress` — what the model says it is doing —
 * so a long run shows its work instead of a spinner. `analysisToolCall` carries the mechanical
 * traffic underneath it: which tool, with what arguments, for how long, and what came back.
 *
 * Two channels rather than one stream with more event kinds, because they are read differently: a
 * reviewer reads the progress sentences, and consults the tool log. Interleaving them would put a
 * hundred `read_file` rows between two sentences someone was reading.
 */
export const RpcNotifications = {
  analysisProgress: 'analysis.progress',
  analysisToolCall: 'analysis.toolCall',
  budgetLimitReached: 'run.budgetLimitReached',
} as const;

/**
 * Subscribes to analysis progress. Returns the unsubscribe function.
 *
 * Reports are dropped unless their sequence is newer than the last one seen: notifications have
 * no ordering guarantee worth relying on, and progress that appears to run backwards reads as a
 * bug in the analysis rather than in the transport.
 */
export function onAnalysisProgress(
  client: RpcClient,
  handler: (progress: AnalysisProgress) => void,
): () => void {
  let latest = 0;

  return client.on<AnalysisProgress>(RpcNotifications.analysisProgress, (progress) => {
    if (progress.sequence <= latest) {
      return;
    }

    latest = progress.sequence;
    handler(progress);
  });
}

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

/**
 * Tests whatever is currently in the provider form, saved or not — not necessarily the last
 * saved values, which is why this takes the full shape rather than an id alone.
 */
export function testProviderConnection(
  client: RpcClient,
  request: TestConnectionRequest,
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

/**
 * Subscribes to the live tool log. Returns the unsubscribe function.
 *
 * Unlike progress, these are not deduplicated by sequence: every event is a distinct moment in the
 * run, and the sequence exists so the view can order them, not so it can drop them.
 */
export function onToolCallEvent(
  client: RpcClient,
  handler: (event: ToolCallEvent) => void,
): () => void {
  return client.on<ToolCallEvent>(RpcNotifications.analysisToolCall, handler);
}

/**
 * Subscribes to a run pausing at a budget limit. Returns the unsubscribe function.
 *
 * Fired by either kind of run — analysing a change or profiling a repository — since both go
 * through the same tool-calling loop. The run stays paused on the host until
 * {@link answerBudgetPrompt} names the same `promptId`.
 */
export function onBudgetLimitReached(
  client: RpcClient,
  handler: (limit: BudgetLimitReached) => void,
): () => void {
  return client.on<BudgetLimitReached>(RpcNotifications.budgetLimitReached, handler);
}

/** Answers a paused run's budget prompt: raise the limit and keep going, or stop it. */
export function answerBudgetPrompt(
  client: RpcClient,
  request: AnswerBudgetPromptRequest,
): Promise<void> {
  return client.call<void>(RpcMethods.answerBudgetPrompt, request);
}

export function getProfile(client: RpcClient, request: ProfileRequest): Promise<ProfileState> {
  return client.call<ProfileState>(RpcMethods.getProfile, request);
}

/**
 * Profiles a repository. The one call in the application that spends money and runs for minutes,
 * so it takes an abort signal: cancelling sends `$/cancelRequest`, the host unwinds the run, and
 * what it spent is still reported.
 */
export function generateProfile(
  client: RpcClient,
  request: ProfileRequest,
  signal: AbortSignal,
): Promise<ProfileState> {
  return client.callAbortable<ProfileState>(
    RpcMethods.generateProfile,
    signal,
    PROFILE_TIMEOUT_MS,
    request,
  );
}

/** Saves the generated sections after the user has edited them. Regeneration replaces these. */
export function saveProfileDocument(
  client: RpcClient,
  request: SaveProfileRequest,
): Promise<ProfileState> {
  return client.call<ProfileState>(RpcMethods.saveProfileDocument, request);
}

/** Saves everything the user authored. Regeneration never touches any of it. */
export function saveProfileNotes(
  client: RpcClient,
  request: SaveProfileNotesRequest,
): Promise<ProfileState> {
  return client.call<ProfileState>(RpcMethods.saveProfileNotes, request);
}

export function deleteProfile(client: RpcClient, request: ProfileRequest): Promise<ProfileState> {
  return client.call<ProfileState>(RpcMethods.deleteProfile, request);
}

/** Every file the generator would write, with full content. Nothing is written by this call. */
export function previewDocumentation(
  client: RpcClient,
  request: DocumentationRequest,
): Promise<DocumentationPreview> {
  return client.call<DocumentationPreview>(RpcMethods.previewDocumentation, request);
}

/**
 * Writes the previewed documentation into the repository. The only call in the application that
 * writes to the user's files, and it carries the token from the preview they approved — the host
 * recomputes it and refuses anything that does not match.
 */
export function exportDocumentation(
  client: RpcClient,
  request: DocumentationExportRequest,
): Promise<DocumentationExportResult> {
  return client.call<DocumentationExportResult>(RpcMethods.exportDocumentation, request);
}

/** The stored analysis of a repository, or an empty view when it has never been analysed. */
export function getAnalysis(client: RpcClient, request: AnalysisRequest): Promise<AnalysisView> {
  return client.call<AnalysisView>(RpcMethods.getAnalysis, request);
}

/**
 * Analyses the working tree. The most expensive call in the application and the longest running,
 * so it takes an abort signal: cancelling sends `$/cancelRequest`, the host unwinds the run, and
 * what it spent is still reported.
 */
export function runAnalysis(
  client: RpcClient,
  request: AnalysisRequest,
  signal: AbortSignal,
): Promise<AnalysisView> {
  return client.callAbortable<AnalysisView>(
    RpcMethods.runAnalysis,
    signal,
    ANALYSIS_TIMEOUT_MS,
    request,
  );
}

/**
 * Shows the stored analysis in the other grouping, and answers with the whole view.
 *
 * A plain `call` with the default timeout, not the analysis one: both groupings came out of the run
 * that was already paid for, so this reads the stored document a second way and starts no
 * conversation. Nothing here can spend money.
 */
export function setGrouping(
  client: RpcClient,
  request: SetGroupingRequest,
): Promise<AnalysisView> {
  return client.call<AnalysisView>(RpcMethods.setGrouping, request);
}

/**
 * Marks nodes reviewed or unreviewed, and answers with every mark — not a delta.
 *
 * The one analysis method that does not return the whole view. It returns the whole of what changed,
 * which is the same convention; sending three hundred nodes and every explanation on them back for
 * each checkbox is what it declines to do.
 */
export function setNodesReviewed(
  client: RpcClient,
  request: SetNodesReviewedRequest,
): Promise<ReviewedState> {
  return client.call<ReviewedState>(RpcMethods.setNodesReviewed, request);
}

/**
 * Every stored run of a repository, most recent first. Reads no document on the host, so it is
 * cheap enough to ask for whenever the analysis screen opens.
 */
export function listAnalyses(client: RpcClient, request: AnalysisRequest): Promise<AnalysisLibrary> {
  return client.call<AnalysisLibrary>(RpcMethods.listAnalyses, request);
}

/**
 * The full ordered trace of one stored run: every tool call and how large each answer was. Asked
 * for when the inspector is opened rather than carried on the view, which travels on every read.
 */
export function getAnalysisTrace(client: RpcClient, request: AnalysisRefRequest): Promise<AnalysisTrace> {
  return client.call<AnalysisTrace>(RpcMethods.getAnalysisTrace, request);
}

/**
 * Whether the working tree has moved since a run. Reads and hashes the changeset host-side, so it
 * gets the changeset's deadline rather than the default — and it is asked after the analysis is
 * already on screen, so however long it takes never delays opening one.
 */
export function checkAnalysisFreshness(
  client: RpcClient,
  request: AnalysisRefRequest,
): Promise<AnalysisFreshness> {
  return client.callWithTimeout<AnalysisFreshness>(
    RpcMethods.checkAnalysisFreshness,
    CHANGESET_TIMEOUT_MS,
    request,
  );
}

/** Forgets one stored run, and answers with the library as it now stands. */
export function deleteAnalysis(client: RpcClient, request: AnalysisRefRequest): Promise<AnalysisLibrary> {
  return client.call<AnalysisLibrary>(RpcMethods.deleteAnalysis, request);
}

/** Which external editors this machine has, and the command configured for one it does not. */
export function describeEditors(client: RpcClient): Promise<EditorSettings> {
  return client.call<EditorSettings>(RpcMethods.describeEditors);
}

export function saveEditorSettings(
  client: RpcClient,
  request: SaveEditorSettingsRequest,
): Promise<EditorSettings> {
  return client.call<EditorSettings>(RpcMethods.saveEditorSettings, request);
}

/**
 * Opens one file in an external editor. Resolves once the editor has started, not once it has been
 * closed, and rejects with a code the catalogue phrases when there is nothing to start.
 *
 * No command line crosses the bridge. The renderer names an editor and a file; the host decides
 * whether that becomes a comparison or a single file at a line, from which sides actually exist.
 */
export function openInEditor(client: RpcClient, request: OpenInEditorRequest): Promise<void> {
  return client.call<void>(RpcMethods.openInEditor, request);
}
