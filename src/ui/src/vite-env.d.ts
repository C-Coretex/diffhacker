/// <reference types="vite/client" />

/**
 * Vite's ambient types, for the two virtual imports the renderer uses: `?worker`, which is how both
 * the ELK layout worker and Monaco's diff worker are built, and `import.meta.env`.
 *
 * Added in Iteration 10. Until then every worker was constructed with `new Worker(new URL(...))`,
 * which needs no ambient declaration; Monaco's is a `?worker` import because Monaco supplies the
 * worker entry point and we do not own the module that would go on the other side of a `new URL`.
 */
