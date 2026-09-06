import { InfoIcon } from 'lucide-react';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { ChangesetPanel } from '@/components/ChangesetPanel';

/**
 * The open repository and its current change.
 *
 * The header confirms which working tree this is and surfaces the two conditions that change
 * what a diff means — no commits, and a linked worktree. Below it, the changeset.
 */
export function RepositoryScreen() {
  const t = useT();

  const repository = useAppStore((state) => state.repositoryInfo);
  const normalizedFrom = useAppStore((state) => state.repositoryNormalizedFrom);
  const showScreen = useAppStore((state) => state.showScreen);

  if (!repository) {
    return null;
  }

  return (
    <div className="flex flex-col gap-6">
      {normalizedFrom && (
        <Alert>
          <InfoIcon />
          <AlertDescription>
            {t('welcome.normalized', { path: repository.path })}
          </AlertDescription>
        </Alert>
      )}

      <Card>
        <CardHeader>
          <div className="flex items-center gap-2">
            <CardTitle>{repository.name}</CardTitle>
            {repository.isLinkedWorktree && (
              <Badge variant="secondary">{t('repository.linkedWorktree')}</Badge>
            )}
          </div>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-col gap-1">
            <span className="text-muted-foreground text-xs">{t('repository.path')}</span>
            <code className="bg-secondary/60 rounded px-2 py-1 font-mono text-xs break-all">
              {repository.path}
            </code>
          </div>

          {!repository.hasCommits && (
            <Alert variant="warning">
              <InfoIcon />
              <AlertDescription>{t('repository.noCommits')}</AlertDescription>
            </Alert>
          )}

          <Button variant="outline" className="w-fit" onClick={() => showScreen('welcome')}>
            {t('repository.change')}
          </Button>
        </CardContent>
      </Card>

      <ChangesetPanel />
    </div>
  );
}
