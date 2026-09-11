import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ToolCallEvent } from '@/contracts';
import { RunPanel, ToolLog } from './ProfileRunPanel';

function base(overrides: Partial<ToolCallEvent>): ToolCallEvent {
  return {
    sequence: 1,
    kind: 'tool_started',
    turn: 1,
    atUtc: '2026-05-01T00:00:00Z',
    isError: false,
    inputTokens: 0,
    outputTokens: 0,
    toolCallCount: 0,
    ...overrides,
  };
}

describe('ToolLog', () => {
  it('puts the newest event at the top, oldest at the bottom', () => {
    const events: ToolCallEvent[] = [
      base({ sequence: 1, kind: 'tool_started', toolName: 'read_file', atUtc: '2026-05-01T00:00:00Z' }),
      base({
        sequence: 2,
        kind: 'tool_finished',
        toolName: 'read_file',
        resultPreview: '# DiffHacker',
        atUtc: '2026-05-01T00:00:01Z',
      }),
      base({
        sequence: 3,
        kind: 'assistant_message',
        turn: 2,
        responseText: 'Here is what changed.',
        atUtc: '2026-05-01T00:00:02Z',
      }),
    ];

    const { container } = render(<ToolLog events={events} />);
    const rows = container.querySelectorAll('li');

    expect(rows).toHaveLength(2);
    expect(rows[0]?.textContent).toContain('Here is what changed.');
    expect(rows[1]?.textContent).toContain('read_file');
  });

  it('collapses reasoning by default but shows the reply text open', () => {
    const events: ToolCallEvent[] = [
      base({
        sequence: 1,
        kind: 'assistant_message',
        reasoningText: 'Checking whether the cache eviction path changed.',
        responseText: 'The eviction path is unchanged.',
      }),
    ];

    render(<ToolLog events={events} />);

    expect(screen.getByText('The eviction path is unchanged.')).toBeInTheDocument();

    const details = screen.getByText('Reasoning').closest('details');
    expect(details).not.toBeNull();
    expect(details?.open).toBe(false);
    expect(details?.textContent).toContain('Checking whether the cache eviction path changed.');
  });

  it('renders a timestamp on a tool row', () => {
    const events: ToolCallEvent[] = [
      base({ sequence: 1, kind: 'tool_started', toolName: 'read_file', atUtc: '2026-05-01T00:00:00Z' }),
    ];

    const { container } = render(<ToolLog events={events} />);

    const time = container.querySelector('time');
    expect(time?.getAttribute('dateTime')).toBe('2026-05-01T00:00:00Z');
    expect(time?.textContent).toMatch(/^\d{2}:\d{2}:\d{2}$/);
  });

  it('shows nothing was logged yet when the run has produced no events', () => {
    render(<ToolLog events={[]} />);

    expect(screen.getByText('No tool calls yet.')).toBeInTheDocument();
  });
});

describe('RunPanel', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows the phase, a ticking clock and the running tool-call count from the first frame', () => {
    // Requirement 6. The strip is there before the first event — the clock and a zero are what say
    // a run has started — and every figure but the clock is the host's running total.
    vi.useFakeTimers();
    const startedAt = Date.now();

    const { rerender } = render(<RunPanel events={[]} startedAt={startedAt} onCancel={() => {}} />);

    expect(screen.getByTestId('run-elapsed')).toHaveTextContent('0:00');
    expect(screen.getByTestId('run-tool-calls')).toHaveTextContent('0');
    expect(screen.getByTestId('run-phase')).toHaveTextContent('not reported yet');

    act(() => {
      vi.advanceTimersByTime(65_000);
    });

    expect(screen.getByTestId('run-elapsed')).toHaveTextContent('1:05');

    rerender(
      <RunPanel
        events={[]}
        startedAt={startedAt}
        progress={{ sequence: 1, message: 'Reading the auth changes', phase: 'exploring', atUtc: '2026-05-01T00:00:00Z' }}
        latest={base({ kind: 'tool_finished', turn: 3, toolCallCount: 1_234, inputTokens: 5_000, costUsd: 0.1234 })}
        onCancel={() => {}}
      />,
    );

    expect(screen.getByText('Reading the auth changes')).toBeInTheDocument();
    expect(screen.getByTestId('run-phase')).toHaveTextContent('Exploring the repository');
    expect(screen.getByTestId('run-tool-calls')).toHaveTextContent('1,234');
    expect(screen.getByTestId('run-turn')).toHaveTextContent('3');
    expect(screen.getByTestId('run-cost')).toHaveTextContent('$0.1234');
  });

  it('says a phase was not stated rather than inventing one when a report names none', () => {
    render(
      <RunPanel
        events={[]}
        progress={{ sequence: 1, message: 'Looking around', atUtc: '2026-05-01T00:00:00Z' }}
        onCancel={() => {}}
      />,
    );

    expect(screen.getByTestId('run-phase')).toHaveTextContent('not stated');
  });
});
