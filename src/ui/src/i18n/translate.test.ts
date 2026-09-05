import { describe, expect, it, vi } from 'vitest';
import { RpcError } from '@/rpc/protocol';
import { describeError } from './errors';
import { translate } from './translate';

describe('translate', () => {
  it('resolves a nested key', () => {
    expect(translate('app.title')).toBe('DiffHacker');
  });

  it('substitutes named arguments', () => {
    expect(translate('demo.step', { step: 2, total: 5 })).toBe('Processing step 2 of 5');
  });

  it('leaves a placeholder alone when no argument is supplied', () => {
    expect(translate('demo.step', { step: 2 })).toContain('{total}');
  });

  it('returns the key and reports when a key is missing', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});

    // Deliberately bypasses the compile-time key check to exercise the run-time guard.
    expect(translate('app.nope' as never)).toBe('app.nope');
    expect(error).toHaveBeenCalled();

    error.mockRestore();
  });
});

describe('describeError', () => {
  it('renders a known host error code with its arguments', () => {
    const error = new RpcError(-32000, 'developer detail', {
      code: 'demo_steps_out_of_range',
      args: { steps: '0' },
    });

    expect(describeError(error)).toBe('The host rejected a step count of 0.');
  });

  it('never leaks the developer message for an unknown code', () => {
    const error = new RpcError(-32000, 'stack trace and secrets', { code: 'not_a_real_code' });
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});

    const message = describeError(error);

    expect(message).toBe(translate('error.unknown_error'));
    expect(message).not.toContain('stack trace');

    consoleError.mockRestore();
  });

  it('falls back for a non-RPC failure', () => {
    expect(describeError(new Error('boom'))).toBe(translate('error.unknown_error'));
  });
});
