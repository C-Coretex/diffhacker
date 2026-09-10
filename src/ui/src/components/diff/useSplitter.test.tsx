import { render, screen } from '@testing-library/react';
import { act } from 'react';
import { useRef, useState } from 'react';
import { describe, expect, it } from 'vitest';
import { DIFF_PANEL_MIN_GRAPH, DIFF_PANEL_MIN_WIDTH } from '@/store/appStore';
import { useSplitter } from './useSplitter';

const CONTAINER_WIDTH = 1400;
const CONTAINER_RIGHT = 1500;

/**
 * jsdom gives every element a zero-sized rectangle, so the container's is stubbed. The numbers are
 * what the hook does arithmetic on, and stubbing them is the only way to assert the clamp — which is
 * the whole reason this hook is not a two-line `onMouseMove`.
 */
function Harness({ onWidth }: { onWidth: (width: number) => void }) {
  const container = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(700);

  const splitter = useSplitter(container, width, (next) => {
    setWidth(next);
    onWidth(next);
  });

  return (
    <div
      ref={(element) => {
        container.current = element;
        if (element) {
          element.getBoundingClientRect = () =>
            ({ right: CONTAINER_RIGHT, width: CONTAINER_WIDTH, left: 100 }) as DOMRect;
        }
      }}
    >
      <div
        role="separator"
        data-testid="handle"
        data-dragging={splitter.dragging ? 'true' : 'false'}
        onPointerDown={splitter.onPointerDown}
      />
      <span data-testid="width">{width}</span>
    </div>
  );
}

function drag(clientX: number) {
  act(() => {
    window.dispatchEvent(new PointerEvent('pointermove', { clientX, bubbles: true }));
  });
}

function grab() {
  act(() => {
    screen.getByTestId('handle').dispatchEvent(
      new PointerEvent('pointerdown', { bubbles: true, clientX: 800 }),
    );
  });
}

describe('useSplitter', () => {
  it('resizes from the right edge of its container', () => {
    render(<Harness onWidth={() => {}} />);

    grab();
    drag(1000);

    // 1500 (right edge) − 1000 (pointer) = 500.
    expect(screen.getByTestId('width')).toHaveTextContent('500');
  });

  it('never lets the panel take the diagram’s last strip', () => {
    // Requirement 10, as a number. "Expanded to full width" is as wide as it goes while the diagram
    // keeps a rail, because the reviewer's position has to stay visible in the graph at all times —
    // including then. A panel that covered the diagram would break that.
    render(<Harness onWidth={() => {}} />);

    grab();
    drag(0);

    expect(screen.getByTestId('width')).toHaveTextContent(
      String(CONTAINER_WIDTH - DIFF_PANEL_MIN_GRAPH),
    );
  });

  it('never lets the panel shrink past being readable', () => {
    render(<Harness onWidth={() => {}} />);

    grab();
    drag(CONTAINER_RIGHT);

    expect(screen.getByTestId('width')).toHaveTextContent(String(DIFF_PANEL_MIN_WIDTH));
  });

  it('stops resizing when the pointer is released, wherever it was released', () => {
    render(<Harness onWidth={() => {}} />);

    grab();
    drag(1000);

    act(() => {
      window.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
    });

    drag(1200);

    // The pointer moved again and the panel did not, which is the point: the listeners live on the
    // window during a drag so the pointer can outrun the divider, and must not outlive it.
    expect(screen.getByTestId('width')).toHaveTextContent('500');
  });

  it('reports that a drag is in progress so the handle can show it', () => {
    render(<Harness onWidth={() => {}} />);

    expect(screen.getByTestId('handle')).toHaveAttribute('data-dragging', 'false');

    grab();
    expect(screen.getByTestId('handle')).toHaveAttribute('data-dragging', 'true');
  });
});
