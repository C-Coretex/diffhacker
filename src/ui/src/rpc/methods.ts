import type {
  HostInfo,
  ProgressNotification,
  SelfTestResult,
  StartDemoRequest,
  StartDemoResponse,
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
