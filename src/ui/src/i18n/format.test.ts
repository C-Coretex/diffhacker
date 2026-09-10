import { describe, expect, it } from 'vitest';
import { formatCount, formatTime } from './format';

describe('formatCount', () => {
  it('groups thousands the same way whatever ICU data the runtime carries', () => {
    // The reason this helper exists: toLocaleString() renders 1200 in one environment and 1,200
    // in another, which showed up as a test and a WebView disagreeing about the same number.
    expect(formatCount(1200)).toBe('1,200');
    expect(formatCount(12000)).toBe('12,000');
    expect(formatCount(1234567)).toBe('1,234,567');
  });

  it('leaves small numbers alone', () => {
    expect(formatCount(0)).toBe('0');
    expect(formatCount(7)).toBe('7');
    expect(formatCount(999)).toBe('999');
  });

  it('keeps a sign in front of the grouping', () => {
    expect(formatCount(-4200)).toBe('-4,200');
  });

  it('truncates rather than rounding, so a count never reads as more than it is', () => {
    expect(formatCount(1999.9)).toBe('1,999');
  });
});

describe('formatTime', () => {
  it('renders the reviewer’s own wall-clock time, whatever timezone the machine is in', () => {
    // Round-tripped through toISOString() rather than a hardcoded UTC string, so this passes the
    // same way on a machine in any timezone: the local instant that goes in is the local time
    // that must come back out.
    const local = new Date(2026, 4, 1, 9, 5, 3);
    expect(formatTime(local.toISOString())).toBe('09:05:03');
  });

  it('pads single digits', () => {
    const local = new Date(2026, 4, 1, 0, 3, 7);
    expect(formatTime(local.toISOString())).toBe('00:03:07');
  });

  it('renders nothing for a value that is not a valid instant', () => {
    expect(formatTime('not a date')).toBe('');
  });
});
