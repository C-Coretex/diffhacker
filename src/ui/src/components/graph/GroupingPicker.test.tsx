import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { en } from '@/i18n/en';
import { GroupingPicker, groupingBodyKey } from './GroupingPicker';

/**
 * The control that answers "why does the same change look different?".
 *
 * Requirement 3 is not just that the mode is visible: it is that a one-line explanation of each is
 * visible, so the reviewer understands the difference rather than discovering it. Both lines are
 * asserted, not only the active one.
 */
describe('GroupingPicker', () => {
  it('shows which grouping is active without disguising it as unavailable', () => {
    render(
      <GroupingPicker
        active="dependency_flow"
        available={['dependency_flow', 'change_clusters']}
        onChange={() => {}}
        busy={false}
      />,
    );

    const flow = screen.getByTestId('grouping-dependency_flow');
    const clusters = screen.getByTestId('grouping-change_clusters');

    expect(flow).toHaveAttribute('aria-pressed', 'true');
    expect(clusters).toHaveAttribute('aria-pressed', 'false');

    // The active one is disabled — asking the host for the picture already on screen is a wasted
    // round trip — but it is the state, not an absence of the option.
    expect(flow).toBeDisabled();
    expect(clusters).toBeEnabled();
  });

  it('explains both groupings, so the other one can be understood before switching', () => {
    render(
      <GroupingPicker
        active="dependency_flow"
        available={['dependency_flow', 'change_clusters']}
        onChange={() => {}}
        busy={false}
      />,
    );

    expect(screen.getByTestId('grouping-dependency_flow')).toHaveAttribute(
      'title',
      `${en.analysis.graph.grouping.dependencyFlow} — ${en.analysis.graph.grouping.dependencyFlowBody}`,
    );

    expect(screen.getByTestId('grouping-change_clusters')).toHaveAttribute(
      'title',
      `${en.analysis.graph.grouping.changeClusters} — ${en.analysis.graph.grouping.changeClustersBody}`,
    );
  });

  it('says why a grouping is unavailable rather than hiding that it exists', () => {
    // An analysis produced before groupings existed, or by a run told not to bother with the second
    // one. Hiding the option would make the feature look absent instead of unbought.
    render(
      <GroupingPicker
        active="dependency_flow"
        available={['dependency_flow']}
        onChange={() => {}}
        busy={false}
      />,
    );

    const clusters = screen.getByTestId('grouping-change_clusters');

    expect(clusters).toBeVisible();
    expect(clusters).toBeDisabled();
    expect(clusters).toHaveAttribute('title', en.analysis.graph.grouping.unavailable);
  });

  it('asks for the other grouping when it is clicked', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(
      <GroupingPicker
        active="dependency_flow"
        available={['dependency_flow', 'change_clusters']}
        onChange={onChange}
        busy={false}
      />,
    );

    await user.click(screen.getByTestId('grouping-change_clusters'));

    expect(onChange).toHaveBeenCalledWith('change_clusters');
  });

  it('takes no second click while a switch is in flight', async () => {
    // Two views racing to replace the state would leave whichever answered last on screen, which
    // need not be the one the reviewer asked for second.
    const onChange = vi.fn();
    const user = userEvent.setup({ pointerEventsCheck: 0 });

    render(
      <GroupingPicker
        active="dependency_flow"
        available={['dependency_flow', 'change_clusters']}
        onChange={onChange}
        busy
      />,
    );

    await user.click(screen.getByTestId('grouping-change_clusters'));

    expect(onChange).not.toHaveBeenCalled();
  });

  it('names the line that explains whichever grouping is on screen', () => {
    expect(groupingBodyKey('dependency_flow')).toBe('analysis.graph.grouping.dependencyFlowBody');
    expect(groupingBodyKey('change_clusters')).toBe('analysis.graph.grouping.changeClustersBody');
  });
});
