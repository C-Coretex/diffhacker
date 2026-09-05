import { CONTRACT_VERSION } from '@/contracts';
import { useT } from '@/i18n/useT';
import { useAppStore } from '@/store/appStore';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';

/** What the host reported through `host.ping`, and whether both sides agree on the contract. */
export function HostPanel() {
  const t = useT();
  const connection = useAppStore((state) => state.connection);
  const hostInfo = useAppStore((state) => state.hostInfo);
  const connectionError = useAppStore((state) => state.connectionError);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('host.heading')}</CardTitle>
      </CardHeader>
      <CardContent>
        {connection === 'connecting' && <p className="text-muted-foreground text-sm">{t('host.connecting')}</p>}

        {connection === 'detached' && <p className="text-muted-foreground text-sm">{t('host.detached')}</p>}

        {connection === 'error' && (
          <p role="alert" className="text-destructive text-sm">
            {connectionError}
          </p>
        )}

        {connection === 'connected' && hostInfo && (
          <>
            {hostInfo.contractVersion !== CONTRACT_VERSION && (
              <p role="alert" className="text-destructive mb-4 text-sm">
                {t('host.contractMismatch', { host: hostInfo.contractVersion, ui: CONTRACT_VERSION })}
              </p>
            )}

            <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
              <Row label={t('host.appVersion')} value={hostInfo.appVersion} />
              <Row label={t('host.platform')} value={hostInfo.platform} />
              <Row label={t('host.architecture')} value={hostInfo.processArchitecture} />
              <Row label={t('host.os')} value={hostInfo.osDescription} />
              <Row label={t('host.contract')} value={hostInfo.contractVersion} />
              <Row label={t('host.started')} value={new Date(hostInfo.startedAtUtc).toLocaleString()} />
            </dl>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-mono text-xs break-all">{value}</dd>
    </>
  );
}
