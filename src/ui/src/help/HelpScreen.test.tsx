import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { en } from '@/i18n/en';
import { useAppStore } from '@/store/appStore';
import { GUIDE_STEPS } from './guideSteps';
import { HelpScreen } from './HelpScreen';
import { FAQ_IDS, SHORTCUT_IDS, TROUBLESHOOTING_IDS } from './helpContent';

const steps = en.help.guide.steps;
const total = GUIDE_STEPS.length;

function position(current: number) {
  return `Step ${current} of ${total}`;
}

describe('HelpScreen', () => {
  beforeEach(() => {
    useAppStore.setState({
      screen: 'help',
      helpReturnScreen: 'analysis',
      helpSection: 'guide',
      helpGuideStep: 0,
    });
  });

  it('opens on the first step of the guide', () => {
    render(<HelpScreen />);

    expect(screen.getByTestId('guide-position')).toHaveTextContent(position(1));
    expect(screen.getByRole('heading', { level: 3, name: steps.provider.title })).toBeInTheDocument();
    for (const back of screen.getAllByRole('button', { name: en.help.guide.previous })) {
      expect(back).toBeDisabled();
    }
  });

  it('pages forward and back, and remembers the step in the store', async () => {
    render(<HelpScreen />);

    // Two pairs of buttons — above the instructions and under the screenshot — do the same thing.
    await userEvent.click(screen.getAllByRole('button', { name: en.help.guide.next })[0]!);
    expect(screen.getByTestId('guide-position')).toHaveTextContent(position(2));
    expect(screen.getByRole('heading', { level: 3, name: steps.testConnection.title })).toBeInTheDocument();
    expect(useAppStore.getState().helpGuideStep).toBe(1);

    await userEvent.click(screen.getAllByRole('button', { name: en.help.guide.previous })[1]!);
    expect(screen.getByTestId('guide-position')).toHaveTextContent(position(1));
  });

  it('jumps to any step from the list, and marks the one showing', async () => {
    render(<HelpScreen />);

    const list = screen.getByRole('navigation', { name: en.help.guide.stepsLabel });
    await userEvent.click(within(list).getByRole('button', { name: /Open the diff/ }));

    expect(screen.getByTestId('guide-position')).toHaveTextContent(position(12));
    expect(within(list).getByRole('button', { name: /Open the diff/ })).toHaveAttribute('aria-current', 'step');
    expect(within(list).getAllByRole('button')).toHaveLength(total);
  });

  it('moves between steps with the arrow keys', () => {
    render(<HelpScreen />);
    const guide = screen.getByTestId('guide');

    fireEvent.keyDown(guide, { key: 'ArrowRight' });
    fireEvent.keyDown(guide, { key: 'ArrowRight' });
    expect(screen.getByTestId('guide-position')).toHaveTextContent(position(3));

    fireEvent.keyDown(guide, { key: 'ArrowLeft' });
    expect(screen.getByTestId('guide-position')).toHaveTextContent(position(2));
  });

  it('draws the instructions as Markdown rather than as literal asterisks', () => {
    render(<HelpScreen />);

    const guide = screen.getByTestId('guide');
    expect(within(guide).getByText('Add a provider', { selector: 'strong' })).toBeInTheDocument();
    expect(guide).not.toHaveTextContent('**');
  });

  it('ends on Finish, which goes back to where Help was opened from', async () => {
    useAppStore.setState({ helpGuideStep: total - 1 });
    render(<HelpScreen />);

    expect(screen.getByTestId('guide-position')).toHaveTextContent(position(total));
    expect(screen.queryByRole('button', { name: en.help.guide.next })).not.toBeInTheDocument();

    await userEvent.click(screen.getAllByRole('button', { name: en.help.guide.finish })[0]!);

    expect(useAppStore.getState().screen).toBe('analysis');
    expect(useAppStore.getState().helpReturnScreen).toBeUndefined();
  });

  it('keeps a stored step inside the guide, whatever it says', () => {
    useAppStore.setState({ helpGuideStep: 99 });
    render(<HelpScreen />);

    expect(screen.getByTestId('guide-position')).toHaveTextContent(position(total));
  });

  it('switches between its sections', async () => {
    render(<HelpScreen />);
    const sections = screen.getByRole('group', { name: en.help.sectionsLabel });

    await userEvent.click(within(sections).getByRole('button', { name: en.help.sections.faq }));

    expect(within(sections).getByRole('button', { name: en.help.sections.faq })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByTestId('guide')).not.toBeInTheDocument();
    expect(useAppStore.getState().helpSection).toBe('faq');
  });

  it('lists every question in the FAQ, each opening to its answer', async () => {
    useAppStore.setState({ helpSection: 'faq' });
    render(<HelpScreen />);

    const faq = screen.getByTestId('help-faq');
    expect(faq.querySelectorAll('details')).toHaveLength(FAQ_IDS.length);

    const readOnly = screen.getByTestId('help-faq-readOnly');
    expect(readOnly).not.toHaveAttribute('open');

    await userEvent.click(within(readOnly).getByText(en.help.faq.items.readOnly.question));
    expect(readOnly).toHaveAttribute('open');
    expect(readOnly).toHaveTextContent('It never commits, stages, checks out or edits files');
  });

  it('lists every troubleshooting entry', () => {
    useAppStore.setState({ helpSection: 'troubleshooting' });
    render(<HelpScreen />);

    expect(screen.getByTestId('help-troubleshooting').querySelectorAll('details')).toHaveLength(
      TROUBLESHOOTING_IDS.length,
    );
  });

  it('lists the shortcuts the analysis screen answers to', () => {
    useAppStore.setState({ helpSection: 'shortcuts' });
    render(<HelpScreen />);

    const rows = within(screen.getByTestId('help-shortcuts')).getAllByRole('row');
    // One header row, then one per shortcut.
    expect(rows).toHaveLength(SHORTCUT_IDS.length + 1);
    expect(screen.getByText('J', { selector: 'kbd' })).toBeInTheDocument();
  });

  it('describes what the product never does', () => {
    useAppStore.setState({ helpSection: 'about' });
    render(<HelpScreen />);

    expect(screen.getByRole('heading', { name: en.help.about.neverHeading })).toBeInTheDocument();
    expect(
      screen.getByText('Change your repository on its own.', { selector: 'strong' }),
    ).toBeInTheDocument();
  });
});

describe('opening and closing Help', () => {
  beforeEach(() => {
    useAppStore.setState({ screen: 'repository', helpReturnScreen: undefined });
  });

  it('returns to the screen it was opened from', () => {
    useAppStore.getState().openHelp();
    expect(useAppStore.getState().screen).toBe('help');

    useAppStore.getState().closeHelp();
    expect(useAppStore.getState().screen).toBe('repository');
  });

  it('keeps the original way back when opened again from Help itself', () => {
    useAppStore.getState().openHelp();
    useAppStore.getState().openHelp();

    useAppStore.getState().closeHelp();
    expect(useAppStore.getState().screen).toBe('repository');
  });
});
