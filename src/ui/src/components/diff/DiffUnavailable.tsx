import { FileQuestionIcon, FileWarningIcon, HardDriveIcon } from 'lucide-react';
import { useT } from '@/i18n/useT';
import { formatBytes, HOST_MAX_BYTES, type DiffContent } from './diffContent';

/**
 * The three files Monaco should not be given — requirement 8's awkward cases, each said plainly.
 *
 * A binary file gets a sentence, not a hex dump. The iteration asks for "a clear statement, not a
 * wall of bytes", and a hex view would be a second content renderer built for a handful of formats
 * and noise for the rest. What it gets instead is the facts a reviewer can act on — what happened to
 * it, both paths if it moved, how big it is — and the external-editor buttons in the header above,
 * because a reviewer with a tool for that format should be one click from it.
 *
 * Takes a path rather than an `AnalysisNodeInfo`, so it draws the same statement from `DiffPanel` (where
 * the file is still a node — it can be marked reviewed, sits in the reading order, and carries the
 * model's explanation underneath, because having no text diff does not make it less part of the change,
 * §0.2.5) and from the plain changeset list, which has no node at all.
 */
export function DiffUnavailable({
  content,
  path,
  previousPath,
  status,
}: {
  content: DiffContent;
  /** Repository-relative path of the working-tree side. */
  path: string;
  /** Where the file was before it moved, when it moved. */
  previousPath?: string;
  /** The change's status — `modified`, `added`, and so on — shown as the host reported it. */
  status?: string;
}) {
  const t = useT();

  return (
    <div
      className="flex h-full flex-col items-center justify-center gap-3 p-6 text-center"
      data-testid="diff-unavailable"
      data-kind={content.kind}
    >
      <div className="text-muted-foreground">
        {content.kind === 'binary' ? (
          <HardDriveIcon className="size-8" aria-hidden />
        ) : content.kind === 'tooLarge' ? (
          <FileWarningIcon className="size-8" aria-hidden />
        ) : (
          <FileQuestionIcon className="size-8" aria-hidden />
        )}
      </div>

      <div className="flex flex-col gap-1">
        <p className="text-sm font-medium">
          {content.kind === 'binary'
            ? t('analysis.diff.binaryHeading')
            : content.kind === 'tooLarge'
              ? t('analysis.diff.tooLargeHeading')
              : t('analysis.diff.absentHeading')}
        </p>

        <p className="max-w-96 text-xs text-muted-foreground">
          {content.kind === 'binary'
            ? t('analysis.diff.binaryBody')
            : content.kind === 'tooLarge'
              ? t('analysis.diff.tooLargeBody', {
                  size: formatBytes(content.sizeBytes),
                  limit: formatBytes(HOST_MAX_BYTES),
                })
              : t('analysis.diff.absentBody')}
        </p>
      </div>

      <dl className="flex flex-col items-center gap-0.5 font-mono text-[11px] text-muted-foreground">
        <dd className="break-all">{path}</dd>

        {previousPath && (
          <dd className="break-all" data-testid="unavailable-renamed-from">
            {t('analysis.diff.renamedFrom', { path: previousPath })}
          </dd>
        )}

        {status && <dd>{status}</dd>}

        {/*
          The size, even — especially — when the content was withheld for being too big. The host
          reports the true size in every case, so this is never a guess.
        */}
        {content.sizeBytes > 0 && <dd>{formatBytes(content.sizeBytes)}</dd>}
      </dl>
    </div>
  );
}
