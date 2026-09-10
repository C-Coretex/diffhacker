import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ToolCallEvent } from '@/contracts';
import { ToolLog } from './ProfileRunPanel';

function base(overrides: Partial<ToolCallEvent>): ToolCallEvent {
  return {
    sequence: 1,
    kind: 'tool_started',
    turn: 1,
    atUtc: '2026-05-01T00:00:00Z',
    isError: false,
    inputTokens: 0,
    outputTokens: 0,
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
