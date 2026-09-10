import { createContext, useContext } from 'react';
import type {
  AnalysisContainerInfo,
  AnalysisNodeInfo,
  ChangedFileFactsInfo,
  EditorSettings,
  OpenInEditorRequestEditor,
} from '@/contracts';

/**
 * What a box on the diagram is allowed to do, handed down rather than reached for.
 *
 * Every one of these could be pulled out of the store by the node itself. None of them is, and the
 * reason is arithmetic: a three-hundred-node diagram would then hold nine hundred store
 * subscriptions and three hundred copies of the "which editors exist" question, all to render a row
 * of buttons that is invisible until the pointer is on it. The surface asks once and passes the
 * answers down; the boxes hold one `useContext` each.
 *
 * `dismissCard` is the other half of the gesture Iteration 9 settled. Clicking a box keeps its hover
 * card open; clicking a *button on* that box means the reviewer is done reading and wants the thing
 * to happen — so every action here puts the card away first, and the card never ends up floating over
 * a panel that just opened underneath it.
 */
export interface GraphActions {
  /** Which external editors this machine has. Undefined until the host has answered. */
  readonly editors: EditorSettings | undefined;

  /** Opens one file in the diff panel and makes it the reviewer's position. */
  readonly openDiff: (node: AnalysisNodeInfo) => void;

  /** Opens a whole cluster as a queue, at the file its reading order starts with. */
  readonly openContainer: (container: AnalysisContainerInfo) => void;

  /** Marks or unmarks one node, optimistically. */
  readonly toggleReviewed: (node: AnalysisNodeInfo, reviewed: boolean) => void;

  /** Hands one file to one external editor. The host decides diff-versus-goto. */
  readonly openInEditor: (
    node: AnalysisNodeInfo,
    facts: ChangedFileFactsInfo | undefined,
    editor: OpenInEditorRequestEditor,
  ) => void;

  /**
   * A cluster's card, asked for by its title bar and by nothing else.
   *
   * An expanded container is mostly empty canvas — the region a reviewer pans across, drags over and
   * reads their files in — and treating the whole of it as a hover target meant the cluster's
   * explanation appeared every time the pointer crossed the gap between two boxes. So the card belongs
   * to the title bar, which is the one part of a container that is *about* the container. The element
   * is passed rather than measured here because the card is anchored to that bar's own rectangle: a
   * card beside a nine-hundred-pixel region is a card in another postcode.
   */
  readonly showContainerCard: (container: AnalysisContainerInfo, element: Element) => void;

  /** The pointer left the title bar. */
  readonly hideContainerCard: () => void;

  /** The title bar was clicked: show the cluster's card and keep it. @see showContainerCard */
  readonly pinContainerCard: (container: AnalysisContainerInfo, element: Element) => void;
}

const GraphActionsContext = createContext<GraphActions | null>(null);

export const GraphActionsProvider = GraphActionsContext.Provider;

/**
 * The actions, or null outside a diagram.
 *
 * Null rather than a throw: `FileNode` is a React Flow node type and the library is entitled to
 * render one wherever it likes, including in a measurement pass of its own. A box with no buttons is
 * a fine thing to draw; a crash is not.
 */
export function useGraphActions(): GraphActions | null {
  return useContext(GraphActionsContext);
}
