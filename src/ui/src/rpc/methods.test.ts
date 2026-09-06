import { describe, expect, it } from 'vitest';
import { RpcClient } from './client';
import { RpcNotifications, onAnalysisProgress } from './methods';
import { FakeTransport } from '@/test/fakeTransport';
import type { AnalysisProgress } from '@/contracts';

/**
 * The renderer half of Iteration 5's `report_progress` pipe.
 *
 * The host half is proven by `ToolProgressNotifierTests`. Nothing drives the two together through
 * a live window yet, because no screen starts an analysis until Iteration 7 — that is the
 * end-to-end test this iteration hands over rather than the one it writes.
 */
describe('analysis progress', () => {
  const report = (sequence: number, message: string, phase?: AnalysisProgress['phase']): AnalysisProgress => ({
    sequence,
    message,
    phase,
    atUtc: new Date(2026, 0, 1, 12, sequence).toISOString(),
  });

  it('delivers reports the host pushes', () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);
    const seen: string[] = [];

    onAnalysisProgress(client, (progress) => seen.push(progress.message));

    transport.notify(RpcNotifications.analysisProgress, report(1, 'exploring the repository'));
    transport.notify(RpcNotifications.analysisProgress, report(2, 'reading the auth changes'));

    expect(seen).toEqual(['exploring the repository', 'reading the auth changes']);
  });

  it('drops a report that is not newer than the one already shown', () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);
    const seen: number[] = [];

    onAnalysisProgress(client, (progress) => seen.push(progress.sequence));

    transport.notify(RpcNotifications.analysisProgress, report(1, 'first'));
    transport.notify(RpcNotifications.analysisProgress, report(3, 'third'));

    // Late and duplicate arrivals. Progress that appears to run backwards reads as a bug in the
    // analysis rather than in the transport, so the sequence number is what decides.
    transport.notify(RpcNotifications.analysisProgress, report(2, 'second, arriving late'));
    transport.notify(RpcNotifications.analysisProgress, report(3, 'third again'));

    expect(seen).toEqual([1, 3]);
  });

  it('carries the phase through as a key the catalogue can translate', () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);
    const phases: (AnalysisProgress['phase'] | undefined)[] = [];

    onAnalysisProgress(client, (progress) => phases.push(progress.phase));

    transport.notify(RpcNotifications.analysisProgress, report(1, 'looking around', 'exploring'));
    transport.notify(RpcNotifications.analysisProgress, report(2, 'no phase given'));

    expect(phases).toEqual(['exploring', undefined]);
  });

  it('stops after unsubscribe', () => {
    const transport = new FakeTransport();
    const client = new RpcClient(transport);
    const seen: number[] = [];

    const unsubscribe = onAnalysisProgress(client, (progress) => seen.push(progress.sequence));

    transport.notify(RpcNotifications.analysisProgress, report(1, 'first'));
    unsubscribe();
    transport.notify(RpcNotifications.analysisProgress, report(2, 'second'));

    expect(seen).toEqual([1]);
  });
});
