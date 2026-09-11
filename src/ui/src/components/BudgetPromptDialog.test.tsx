import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { BudgetLimitReached } from '@/contracts';
import { RpcProvider } from '@/rpc/RpcProvider';
import { FakeTransport } from '@/test/fakeTransport';
import { BudgetPromptDialog } from './BudgetPromptDialog';

function prompt(overrides: Partial<BudgetLimitReached> = {}): BudgetLimitReached {
  return {
    promptId: 'p1',
    limit: 'tool_calls',
    explanation: 'The run reached its limit of 500 tool calls.',
    toolCallsUsed: 500,
    tokensUsed: 1_200_000,
    turnsUsed: 80,
    ...overrides,
  };
}

describe('BudgetPromptDialog', () => {
  it('renders nothing while there is no prompt', () => {
    const transport = new FakeTransport();
    render(
      <RpcProvider transport={transport}>
        <BudgetPromptDialog prompt={undefined} onResolved={vi.fn()} />
      </RpcProvider>,
    );

    expect(screen.queryByTestId('budget-prompt-dialog')).not.toBeInTheDocument();
  });

  it('shows what was reached and how far the run had got', () => {
    const transport = new FakeTransport();
    render(
      <RpcProvider transport={transport}>
        <BudgetPromptDialog prompt={prompt()} onResolved={vi.fn()} />
      </RpcProvider>,
    );

    expect(screen.getByTestId('budget-prompt-dialog')).toBeInTheDocument();
    expect(screen.getByText(/reached its limit of 500 tool calls/)).toBeInTheDocument();
    expect(screen.getByText(/500 tool call\(s\)/)).toBeInTheDocument();
    expect(screen.getByText(/1,200,000 token\(s\)/)).toBeInTheDocument();
  });

  it('continuing tells the host to raise the limit and clears the prompt', async () => {
    const transport = new FakeTransport();
    const onResolved = vi.fn();

    render(
      <RpcProvider transport={transport}>
        <BudgetPromptDialog prompt={prompt()} onResolved={onResolved} />
      </RpcProvider>,
    );

    await userEvent.click(screen.getByTestId('budget-prompt-continue'));

    expect(onResolved).toHaveBeenCalledOnce();
    await waitFor(() => expect(transport.lastRequest().method).toBe('run.answerBudgetPrompt'));
    expect(transport.lastRequest().params).toEqual([{ promptId: 'p1', decision: 'continue' }]);
  });

  it('stopping tells the host to end the run', async () => {
    const transport = new FakeTransport();
    const onResolved = vi.fn();

    render(
      <RpcProvider transport={transport}>
        <BudgetPromptDialog prompt={prompt({ promptId: 'p2' })} onResolved={onResolved} />
      </RpcProvider>,
    );

    await userEvent.click(screen.getByTestId('budget-prompt-stop'));

    expect(onResolved).toHaveBeenCalledOnce();
    await waitFor(() => expect(transport.lastRequest().method).toBe('run.answerBudgetPrompt'));
    expect(transport.lastRequest().params).toEqual([{ promptId: 'p2', decision: 'stop' }]);
  });
});
