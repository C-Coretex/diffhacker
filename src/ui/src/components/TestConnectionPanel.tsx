import { useCallback, useState } from 'react';
import { CheckCircle2Icon, Loader2Icon, PlugZapIcon, TriangleAlertIcon } from 'lucide-react';
import type { TestConnectionRequest, TestConnectionResult } from '@/contracts';
import { en } from '@/i18n/en';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { testProviderConnection } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { Button } from '@/components/ui/button';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { translate, type ResourceKey } from '@/i18n/translate';

interface TestConnectionPanelProps {
  /**
   * Exactly what to test. The caller recomputes this from its own live state on every render, so
   * a test always checks what is on screen right now rather than what was last saved.
   */
  request: TestConnectionRequest;
  /** False disables the button — typically because there is nothing to authenticate with yet. */
  canTest: boolean;
  /** The model the outcome is checked against, for the "found but not this one" message. */
  model: string;
  /** Called with the provider's model list after a test that succeeded and returned one. */
  onSucceeded?(models: readonly string[]): void | Promise<void>;
}

export function TestConnectionPanel({ request, canTest, model, onSucceeded }: TestConnectionPanelProps) {
  const t = useT();
  const client = useRpc();

  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState<TestConnectionResult>();
  const [error, setError] = useState<string>();

  const run = useCallback(async () => {
    if (!client) return;

    setTesting(true);
    setError(undefined);
    setResult(undefined);
    try {
      const outcome = await testProviderConnection(client, request);
      setResult(outcome);

      if (outcome.succeeded && outcome.availableModels.length > 0) {
        await onSucceeded?.(outcome.availableModels);
      }
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setTesting(false);
    }
  }, [client, request, onSucceeded]);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={testing || !client || !canTest}
          onClick={() => void run()}
        >
          {testing ? (
            <>
              <Loader2Icon className="animate-spin" aria-hidden />
              {t('providers.testing')}
            </>
          ) : (
            <>
              <PlugZapIcon aria-hidden />
              {t('providers.test')}
            </>
          )}
        </Button>
        {/* Requirement 5 asks for a real request; listing models is one, and it is free. */}
        <span className="text-muted-foreground text-xs">{t('providers.testFree')}</span>
      </div>

      <div aria-live="polite">
        {error && (
          <p role="alert" className="text-destructive text-sm">
            {error}
          </p>
        )}
        {result && <TestOutcome result={result} model={model} />}
      </div>
    </div>
  );
}

function TestOutcome({ result, model }: { result: TestConnectionResult; model: string }) {
  const t = useT();

  if (result.succeeded) {
    const count = result.availableModels.length;

    if (count === 0) {
      return (
        <Alert>
          <CheckCircle2Icon />
          <AlertTitle>{t('providers.testSucceededNoModels')}</AlertTitle>
        </Alert>
      );
    }

    // The key works but the model name does not match anything it can reach — almost always a
    // typo, and worth saying now rather than at the first analysis run.
    if (!result.modelVerified) {
      return (
        <Alert variant="warning">
          <TriangleAlertIcon />
          <AlertTitle>{t('providers.testModelMissing', { model, count: String(count) })}</AlertTitle>
        </Alert>
      );
    }

    return (
      <Alert>
        <CheckCircle2Icon />
        <AlertTitle>{t('providers.testSucceeded', { count: String(count) })}</AlertTitle>
      </Alert>
    );
  }

  return (
    <Alert variant="destructive" role="alert">
      <TriangleAlertIcon />
      <AlertTitle>{describeFailure(result.failureCode)}</AlertTitle>
      <AlertDescription>
        {/*
          Requirement 5 wants the provider's *actual* error, not a generic one. The host has
          already scrubbed the API key out of this text before it crossed the bridge.
        */}
        {result.providerMessage && (
          <>
            <span className="text-xs opacity-80">{t('providers.providerSaid')}</span>
            <code className="bg-destructive/10 max-h-40 w-full overflow-auto rounded p-2 font-mono text-xs break-all whitespace-pre-wrap">
              {result.providerMessage}
            </code>
          </>
        )}
        {result.httpStatus !== undefined && result.httpStatus > 0 && (
          <span className="text-xs opacity-70">
            {t('providers.httpStatus', { status: String(result.httpStatus) })}
          </span>
        )}
      </AlertDescription>
    </Alert>
  );
}

/**
 * The host sends a stable failure category, never a sentence — the same rule the RPC error
 * codes follow. An unrecognised one falls back to the generic message rather than rendering a
 * raw code at the user.
 */
function describeFailure(code: string | undefined): string {
  if (code && code in en.testFailure) {
    return translate(`testFailure.${code}` as ResourceKey);
  }

  if (code) {
    console.error(`[i18n] No message for connection failure '${code}'`);
  }

  return translate('providers.testFailed');
}
