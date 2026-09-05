import { CONTRACT_VERSION, type HostInfo, type SelfTestCheck, type SelfTestResult } from '@/contracts';
import type { RpcClient } from '@/rpc/client';
import { onProgress, startCountdown } from '@/rpc/methods';

/** How many notifications the stream check expects. The iteration's bar is "at least three". */
const REQUIRED_NOTIFICATIONS = 3;

const STREAM_TIMEOUT_MS = 15_000;

/**
 * The renderer's own verification of the host bridge, run when the host was launched with
 * `--self-test`.
 *
 * This exists because a screenshot cannot tell a working WebView from a blank one. CI gates on
 * the verdict this produces, so a broken bridge fails the build instead of producing a
 * plausible-looking image.
 */
export async function runSelfTest(client: RpcClient, hostInfo: HostInfo): Promise<SelfTestResult> {
  const checks: SelfTestCheck[] = [
    {
      name: 'rpc_round_trip',
      passed: true,
      detail: `host.ping returned ${hostInfo.platform}/${hostInfo.processArchitecture}`,
    },
    contractCheck(hostInfo),
  ];

  checks.push(await notificationStreamCheck(client));
  checks.push(await contentSecurityPolicyCheck());

  return { succeeded: checks.every((check) => check.passed), checks };
}

/**
 * Proves the Content-Security-Policy is actually enforced, by trying to load a remote script
 * and requiring the WebView to report a violation.
 *
 * WebView2, WKWebView and WebKitGTK each apply CSP slightly differently, and a policy that is
 * present in the markup but not enforced looks identical to one that works. The host is
 * `.invalid`, a reserved TLD that can never resolve, and success is defined as a
 * `securitypolicyviolation` event rather than a load failure — so a merely-unreachable host
 * fails this check instead of passing it by accident.
 */
function contentSecurityPolicyCheck(): Promise<SelfTestCheck> {
  return new Promise((resolve) => {
    let violated = false;

    const onViolation = (event: SecurityPolicyViolationEvent) => {
      if (event.violatedDirective.startsWith('script-src')) {
        violated = true;
      }
    };

    document.addEventListener('securitypolicyviolation', onViolation);

    const script = document.createElement('script');
    script.src = 'https://blocked.invalid/csp-probe.js';

    const finish = () => {
      document.removeEventListener('securitypolicyviolation', onViolation);
      script.remove();

      resolve({
        name: 'csp_blocks_remote_script',
        passed: violated,
        detail: violated
          ? 'the WebView reported a script-src violation for a remote script'
          : 'no script-src violation was reported; the policy may not be enforced',
      });
    };

    script.addEventListener('load', finish);
    script.addEventListener('error', finish);
    document.head.appendChild(script);

    // The violation event fires synchronously on append in every engine we target; the timer
    // is only there so a silent engine cannot hang the run.
    setTimeout(finish, 2_000);
  });
}

function contractCheck(hostInfo: HostInfo): SelfTestCheck {
  const matches = hostInfo.contractVersion === CONTRACT_VERSION;
  return {
    name: 'contract_version_match',
    passed: matches,
    detail: matches
      ? `both sides are on contract ${CONTRACT_VERSION}`
      : `host reported ${hostInfo.contractVersion} but the renderer was built against ${CONTRACT_VERSION}`,
  };
}

async function notificationStreamCheck(client: RpcClient): Promise<SelfTestCheck> {
  const received: number[] = [];

  try {
    const settled = new Promise<void>((resolve, reject) => {
      const timer = setTimeout(
        () => reject(new Error(`only ${received.length} of ${REQUIRED_NOTIFICATIONS} notifications arrived`)),
        STREAM_TIMEOUT_MS,
      );

      const unsubscribe = onProgress(client, (notification) => {
        received.push(notification.step);
        if (notification.completed) {
          clearTimeout(timer);
          unsubscribe();
          resolve();
        }
      });
    });

    const response = await startCountdown(client, { steps: REQUIRED_NOTIFICATIONS, delayMilliseconds: 20 });
    await settled;

    if (received.length < REQUIRED_NOTIFICATIONS) {
      return {
        name: 'notification_stream',
        passed: false,
        detail: `expected ${REQUIRED_NOTIFICATIONS} notifications for ${response.operationId}, received ${received.length}`,
      };
    }

    return {
      name: 'notification_stream',
      passed: true,
      detail: `received ${received.length} notification(s) for ${response.operationId}`,
    };
  } catch (error) {
    return {
      name: 'notification_stream',
      passed: false,
      detail: error instanceof Error ? error.message : String(error),
    };
  }
}
