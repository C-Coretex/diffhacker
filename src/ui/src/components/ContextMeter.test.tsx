import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { ToolCallEvent } from '@/contracts';
import { ContextMeter } from './ContextMeter';

function event(overrides: Partial<ToolCallEvent> = {}): ToolCallEvent {
  return {
    sequence: 1,
    kind: 'usage',
    turn: 4,
    atUtc: '2026-05-01T00:00:00Z',
    isError: false,
    inputTokens: 300_000,
    outputTokens: 4_000,
    contextTokens: 84_312,
    contextWindowTokens: 200_000,
    contextInstructionsCharacters: 7_000,
    contextSchemaCharacters: 12_000,
    contextToolDefinitionCharacters: 3_000,
    contextOpeningCharacters: 20_000,
    contextToolResultCharacters: 150_000,
    contextPrunedCharacters: 400_000,
    contextAssistantCharacters: 8_000,
    ...overrides,
  };
}

describe('ContextMeter', () => {
  it('shows what is used against the window it has', async () => {
    render(<ContextMeter event={event()} />);

    expect(screen.getByText('84,312 / 200,000 (42%)')).toBeInTheDocument();
  });

  it('shows an absolute count and no percentage when the window is unknown', async () => {
    // A guessed denominator makes a meter that is confidently wrong, which is worse than one that
    // admits it does not know.
    render(<ContextMeter event={event({ contextWindowTokens: undefined })} />);

    expect(screen.getByText('84,312 tokens · window unknown')).toBeInTheDocument();
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
  });

  it('says the size is unreported rather than showing zero', async () => {
    // A provider that reports no usage leaves this genuinely unknown, and a zero would read as an
    // empty context.
    render(<ContextMeter event={event({ contextTokens: undefined })} />);

    expect(screen.getByText('not reported by this provider')).toBeInTheDocument();
  });

  it('renders nothing at all before anything has been measured', () => {
    const { container } = render(<ContextMeter event={undefined} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('breaks the context down by category on hover, and says the unit changed', async () => {
    render(<ContextMeter event={event()} />);

    await userEvent.click(screen.getByRole('button'));

    expect(await screen.findByText('What is filling the context')).toBeInTheDocument();
    expect(
      screen.getByText(/The total above is in tokens, counted by the provider/),
    ).toBeInTheDocument();

    expect(screen.getByText('Tool results')).toBeInTheDocument();
    expect(screen.getByText('150,000')).toBeInTheDocument();

    // 150,000 of 200,000 measured characters.
    expect(screen.getByText('75%')).toBeInTheDocument();
  });

  it('says reasoning is unreported rather than counting it as zero', async () => {
    // Most providers bill for reasoning tokens they never show us, so absent is the honest answer
    // and zero would claim the model did none.
    render(<ContextMeter event={event()} />);

    await userEvent.click(screen.getByRole('button'));

    expect(await screen.findByText('Reasoning')).toBeInTheDocument();
    expect(screen.getByText('not reported')).toBeInTheDocument();
  });

  it('counts reasoning when the provider does surface it', async () => {
    render(<ContextMeter event={event({ contextReasoningCharacters: 50_000 })} />);

    await userEvent.click(screen.getByRole('button'));

    expect(await screen.findByText('50,000')).toBeInTheDocument();
    expect(screen.queryByText('not reported')).not.toBeInTheDocument();
  });

  it('reports what pruning has dropped, so forgetting has a visible cause', async () => {
    render(<ContextMeter event={event()} />);

    await userEvent.click(screen.getByRole('button'));

    expect(
      await screen.findByText(/400,000 characters of older tool results have been dropped/),
    ).toBeInTheDocument();
  });

  it('says nothing about pruning before any has happened', async () => {
    render(<ContextMeter event={event({ contextPrunedCharacters: 0 })} />);

    await userEvent.click(screen.getByRole('button'));

    expect(await screen.findByText('What is filling the context')).toBeInTheDocument();
    expect(screen.queryByText(/have been dropped/)).not.toBeInTheDocument();
  });
});
