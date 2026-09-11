import { render, screen } from '@testing-library/react';
import { act } from 'react';
import { useRef, useState } from 'react';
import { describe, expect, it } from 'vitest';
import { DIFF_EXPLANATION_MIN_CODE, DIFF_EXPLANATION_MIN_HEIGHT } from '@/store/appStore';
import { useVerticalSplitter } from './useVerticalSplitter';

const CONTAINER_HEIGHT = 800;
const CONTAINER_BOTTOM = 900;

/** @see useSplitter.test.tsx — jsdom's zero-sized rectangles are stubbed the same way here. */
function Harness({ onHeight }: { onHeight: (height: number) => void }) {
  const container = useRef<HTMLDivElement>(null);
  const [height, setHeight] = useState(260);

  const splitter = useVerticalSplitter(container, height, (next) => {
    setHeight(next);
    onHeight(next);
  });

  return (
    <div
      ref={(element) => {
        container.current = element;
        if (element) {
          element.getBoundingClientRect = () =>
            ({ bottom: CONTAINER_BOTTOM, height: CONTAINER_HEIGHT, top: 100 }) as DOMRect;
        }
      }}
    >
      <div
        role="separator"
        data-testid="handle"
        data-dragging={splitter.dragging ? 'true' : 'false'}
        onPointerDown={splitter.onPointerDown}
      />
      <span data-testid="height">{height}</span>
    </div>
  );
}

function drag(clientY: number) {
  act(() => {
    window.dispatchEvent(new PointerEvent('pointermove', { clientY, bubbles: true }));
  });
}

function grab() {
  act(() => {
    screen.getByTestId('handle').dispatchEvent(
      new PointerEvent('pointerdown', { bubbles: true, clientY: 700 }),
    );
  });
}

describe('useVerticalSplitter', () => {
  it('resizes from the bottom edge of its container', () => {
    render(<Harness onHeight={() => {}} />);

    grab();
    drag(600);

    // 900 (bottom edge) − 600 (pointer) = 300.
    expect(screen.getByTestId('height')).toHaveTextContent('300');
  });

  it('never lets the explanation take the code’s last strip', () => {
    render(<Harness onHeight={() => {}} />);

    grab();
    drag(0);

    expect(screen.getByTestId('height')).toHaveTextContent(
      String(CONTAINER_HEIGHT - DIFF_EXPLANATION_MIN_CODE),
    );
  });

  it('never lets the explanation shrink past being usable', () => {
    render(<Harness onHeight={() => {}} />);

    grab();
    drag(CONTAINER_BOTTOM);

    expect(screen.getByTestId('height')).toHaveTextContent(String(DIFF_EXPLANATION_MIN_HEIGHT));
  });

  it('stops resizing when the pointer is released, wherever it was released', () => {
    render(<Harness onHeight={() => {}} />);

    grab();
    drag(600);

    act(() => {
      window.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
    });

    drag(500);

    expect(screen.getByTestId('height')).toHaveTextContent('300');
  });

  it('reports that a drag is in progress so the handle can show it', () => {
    render(<Harness onHeight={() => {}} />);

    expect(screen.getByTestId('handle')).toHaveAttribute('data-dragging', 'false');

    grab();
    expect(screen.getByTestId('handle')).toHaveAttribute('data-dragging', 'true');
  });
});
