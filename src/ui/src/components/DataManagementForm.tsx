import { useCallback, useState } from 'react';
import { Loader2Icon } from 'lucide-react';
import { describeError } from '@/i18n/errors';
import { useT } from '@/i18n/useT';
import { deleteAllLocalData } from '@/rpc/methods';
import { useRpc } from '@/rpc/RpcProvider';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
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
 * The one destructive action in Settings: wipe the database, every stored API key, cached diffs
 * and the log files. The host closes the window right after it succeeds — there is nothing left
 * worth keeping it open for — so the confirmed state stays on screen rather than trying to return
 * to a settings form whose backing store just disappeared.
 */
export function DataManagementForm() {
  const t = useT();
  const client = useRpc();

  const [confirming, setConfirming] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string>();

  const confirmDelete = useCallback(async () => {
    if (!client) return;

    setDeleting(true);
    setError(undefined);

    try {
      await deleteAllLocalData(client);
      setDone(true);
      setConfirming(false);
    } catch (caught) {
      setError(describeError(caught));
      setDeleting(false);
    }
  }, [client]);

  return (
    <Card className="border-destructive/50">
      <CardHeader>
        <CardTitle>{t('dataManagement.heading')}</CardTitle>
        <CardDescription>{t('dataManagement.description')}</CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-3">
        {done ? (
          <p aria-live="polite" className="text-sm">
            {t('dataManagement.done')}
          </p>
        ) : (
          <>
            <Button
              variant="destructive"
              className="w-fit"
              disabled={!client}
              data-testid="delete-all-data-button"
              onClick={() => setConfirming(true)}
            >
              {t('dataManagement.deleteButton')}
            </Button>

            <AlertDialog
              open={confirming}
              onOpenChange={(open) => {
                setConfirming(open);
                if (!open) setError(undefined);
              }}
            >
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{t('dataManagement.confirmTitle')}</AlertDialogTitle>
                  <AlertDialogDescription>{t('dataManagement.confirmBody')}</AlertDialogDescription>
                </AlertDialogHeader>

                {error && (
                  <p role="alert" className="text-sm text-destructive">
                    {error}
                  </p>
                )}

                <AlertDialogFooter>
                  <AlertDialogCancel disabled={deleting}>{t('dataManagement.confirmCancel')}</AlertDialogCancel>
                  <AlertDialogAction
                    disabled={deleting}
                    data-testid="delete-all-data-confirm"
                    onClick={(event) => {
                      event.preventDefault();
                      void confirmDelete();
                    }}
                  >
                    {deleting && <Loader2Icon className="size-4 animate-spin" aria-hidden />}
                    {t(deleting ? 'dataManagement.deleting' : 'dataManagement.confirmAction')}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </>
        )}
      </CardContent>
    </Card>
  );
}
