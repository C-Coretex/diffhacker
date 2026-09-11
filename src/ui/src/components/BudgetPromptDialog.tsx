import { useEffect, useRef } from 'react';
import type { BudgetLimitReached } from '@/contracts';
import { formatCount } from '@/i18n/format';
import { useT } from '@/i18n/useT';
import { answerBudgetPrompt } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';

const limitLabelKeys = {
  tool_calls: 'budgetPrompt.limitToolCalls',
  tokens: 'budgetPrompt.limitTokens',
  turns: 'budgetPrompt.limitTurns',
  cost: 'budgetPrompt.limitCost',
} as const satisfies Record<BudgetLimitReached['limit'], string>;

/**
 * A run paused at a configured budget limit, asking whether to raise it and keep going or stop.
 *
 * Shared between the analysis screen and the profile screen — both go through the same
 * tool-calling loop on the host, so both can pause at the same limits. `prompt` undefined means
 * closed; `onResolved` clears the caller's own slice of it once the host has been told, so the
 * dialog can only ever answer the one it is currently showing.
 */
export function BudgetPromptDialog({
  prompt,
  onResolved,
}: {
  prompt: BudgetLimitReached | undefined;
  onResolved(): void;
}) {
  const t = useT();
  const client = useRpc();

  // AlertDialogAction/Cancel close the dialog themselves on click, which fires onOpenChange(false)
  // in addition to the onClick handler below — without this guard, clicking either button would
  // answer the prompt twice, once explicitly and once more as an implicit "stop" from the close.
  // Reset whenever a new prompt arrives, so a later one is not silently ignored.
  const decided = useRef(false);

  useEffect(() => {
    decided.current = false;
  }, [prompt?.promptId]);

  const decide = (decision: 'continue' | 'stop') => {
    if (!client || !prompt || decided.current) return;

    decided.current = true;
    const { promptId } = prompt;

    // Cleared immediately rather than after the call resolves: the reviewer has made the
    // decision, and the dialog reappearing because the RPC call is still in flight would look
    // like the click did nothing.
    onResolved();

    answerBudgetPrompt(client, { promptId, decision }).catch((caught: unknown) => {
      // The run keeps waiting on the host either way; there is nothing here to retry into and no
      // result to show for a failed acknowledgement.
      console.warn('[budget] The run could not be told to continue or stop.', caught);
    });
  };

  return (
    <AlertDialog open={prompt !== undefined} onOpenChange={(next) => !next && decide('stop')}>
      <AlertDialogContent data-testid="budget-prompt-dialog">
        <AlertDialogHeader>
          <AlertDialogTitle>
            {t('budgetPrompt.title', {
              limit: prompt ? t(limitLabelKeys[prompt.limit]) : '',
            })}
          </AlertDialogTitle>
          <AlertDialogDescription>
            {prompt &&
              t('budgetPrompt.body', {
                explanation: prompt.explanation,
                toolCalls: formatCount(prompt.toolCallsUsed),
                tokens: formatCount(prompt.tokensUsed),
              })}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel data-testid="budget-prompt-stop" onClick={() => decide('stop')}>
            {t('budgetPrompt.stop')}
          </AlertDialogCancel>
          <AlertDialogAction data-testid="budget-prompt-continue" onClick={() => decide('continue')}>
            {t('budgetPrompt.continue')}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
