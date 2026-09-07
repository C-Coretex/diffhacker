import { useCallback, useId, useState } from 'react';
import type { DocumentationPreview, DocumentationPreviewTarget } from '@/contracts';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { exportDocumentation, previewDocumentation } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';

/**
 * The documentation generator, and the confirmation in front of the only write path in the
 * application.
 *
 * The generated documents live in DiffHacker. Writing them into the repository is a separate act,
 * and it happens only after a dialog has shown every file and every byte — with a diff for anything
 * it would replace. Cancelling writes nothing, and the token the preview returned is checked by the
 * host against what it is about to write, so the interface being wrong cannot turn into a file
 * being written.
 */
export function DocumentationPanel({ repositoryPath }: { repositoryPath: string }) {
  const t = useT();
  const client = useRpc();
  const targetId = useId();

  const [target, setTarget] = useState<DocumentationPreviewTarget>('docs_directory');
  const [preview, setPreview] = useState<DocumentationPreview>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [result, setResult] = useState<string>();

  const openPreview = useCallback(async () => {
    if (!client) return;

    setBusy(true);
    setError(undefined);
    setResult(undefined);

    try {
      setPreview(await previewDocumentation(client, { repositoryPath, target }));
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
    }
  }, [client, repositoryPath, target]);

  const write = useCallback(async () => {
    if (!client || !preview) return;

    setBusy(true);
    setError(undefined);

    try {
      const written = await exportDocumentation(client, {
        repositoryPath,
        target: preview.target,
        previewToken: preview.previewToken,
      });

      setResult(
        written.overwrittenPaths.length > 0
          ? t('documentation.wroteWithOverwrites', {
              count: written.writtenPaths.length,
              path: repositoryPath,
              replaced: written.overwrittenPaths.join(', '),
            })
          : t('documentation.wrote', {
              count: written.writtenPaths.length,
              path: repositoryPath,
            }),
      );
    } catch (caught) {
      setError(describeError(caught));
    } finally {
      setBusy(false);
      setPreview(undefined);
    }
  }, [client, preview, repositoryPath, t]);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('documentation.heading')}</CardTitle>
        <CardDescription>{t('documentation.description')}</CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-1">
            <Label htmlFor={targetId}>{t('documentation.targetLabel')}</Label>
            <Select
              id={targetId}
              value={target}
              onChange={(event) => setTarget(event.target.value as DocumentationPreviewTarget)}
            >
              <option value="docs_directory">{t('documentation.targetDocs')}</option>
              <option value="repository_root">{t('documentation.targetRoot')}</option>
            </Select>
          </div>

          <Button variant="outline" disabled={busy} onClick={() => void openPreview()}>
            {busy && !preview ? t('documentation.previewing') : t('documentation.preview')}
          </Button>
        </div>

        {error && (
          <p role="alert" className="text-destructive text-sm">
            {error}
          </p>
        )}

        {result && (
          <p aria-live="polite" className="text-sm">
            {result}
          </p>
        )}
      </CardContent>

      <AlertDialog open={preview !== undefined} onOpenChange={(open) => !open && setPreview(undefined)}>
        <AlertDialogContent className="max-h-[85vh] max-w-4xl overflow-y-auto">
          <AlertDialogHeader>
            <AlertDialogTitle>{t('documentation.previewHeading')}</AlertDialogTitle>
            <AlertDialogDescription>{t('documentation.previewBody')}</AlertDialogDescription>
          </AlertDialogHeader>

          <Alert variant="warning">
            <AlertDescription>{t('documentation.writeWarning')}</AlertDescription>
          </Alert>

          <div className="flex flex-col gap-6">
            {preview?.files.map((file) => (
              <section key={file.relativePath} className="flex flex-col gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <h3 className="font-mono text-sm font-medium">{file.relativePath}</h3>
                  {file.exists ? (
                    <Badge variant="warning">{t('documentation.exists')}</Badge>
                  ) : (
                    <Badge variant="secondary">{t('documentation.isNew')}</Badge>
                  )}
                </div>

                {file.existingIsUnreadable && (
                  <p className="text-muted-foreground text-xs">{t('documentation.existsUnreadable')}</p>
                )}

                {file.exists && !file.existingIsUnreadable && !file.unifiedDiff && (
                  <p className="text-muted-foreground text-xs">{t('documentation.unchanged')}</p>
                )}

                {file.unifiedDiff && (
                  <>
                    <h4 className="text-muted-foreground text-xs font-medium">
                      {t('documentation.diffHeading')}
                    </h4>
                    <DiffView diff={file.unifiedDiff} />
                  </>
                )}

                <pre className="bg-muted max-h-64 overflow-auto rounded-md p-3 text-xs whitespace-pre-wrap">
                  {file.content}
                </pre>
              </section>
            ))}
          </div>

          <AlertDialogFooter>
            <AlertDialogCancel>{t('documentation.cancel')}</AlertDialogCancel>
            <AlertDialogAction disabled={busy} onClick={() => void write()}>
              {busy ? t('documentation.writing') : t('documentation.write')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  );
}

/**
 * A unified diff, coloured. Monaco's diff editor arrives in Iteration 10 and is not pulled forward
 * for one confirmation dialog; the host computed this text, and the whole of rendering it is
 * deciding what colour each line's first character makes it.
 */
function DiffView({ diff }: { diff: string }) {
  return (
    <pre className="bg-muted max-h-64 overflow-auto rounded-md p-3 text-xs">
      {diff.split('\n').map((line, index) => (
        <div key={index} className={lineClass(line)}>
          {line}
        </div>
      ))}
    </pre>
  );
}

function lineClass(line: string): string {
  if (line.startsWith('+++') || line.startsWith('---')) return 'text-muted-foreground font-medium';
  if (line.startsWith('@@')) return 'text-muted-foreground';
  if (line.startsWith('+')) return 'text-emerald-700 dark:text-emerald-400';
  if (line.startsWith('-')) return 'text-destructive';
  return '';
}
