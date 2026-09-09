import '@testing-library/jest-dom/vitest';

// jsdom has no matchMedia, and useSystemTheme reads it on first render.
if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
}

/*
  React Flow measures the surface it is given before it draws anything, and jsdom implements none
  of the APIs it measures with. These are the stubs the library's own testing guide prescribes;
  without them every graph test fails inside the library rather than on an assertion.

  Everything reports zero size, which is honest — jsdom lays nothing out — and is why the graph
  tests assert on the nodes that were rendered rather than on where they ended up. Positions come
  from ELK, and `layout.snapshot.test.ts` checks those directly, with no DOM in sight.
*/
if (!globalThis.ResizeObserver) {
  globalThis.ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  } as unknown as typeof ResizeObserver;
}

if (!globalThis.DOMMatrixReadOnly) {
  globalThis.DOMMatrixReadOnly = class {
    m22 = 1;
    constructor(_transform?: string) {}
  } as unknown as typeof DOMMatrixReadOnly;
}

if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {};
}

if (!Element.prototype.hasPointerCapture) {
  Element.prototype.hasPointerCapture = () => false;
  Element.prototype.setPointerCapture = () => {};
  Element.prototype.releasePointerCapture = () => {};
}

Object.defineProperties(globalThis.HTMLElement.prototype, {
  offsetHeight: { get: () => 0, configurable: true },
  offsetWidth: { get: () => 0, configurable: true },
});
