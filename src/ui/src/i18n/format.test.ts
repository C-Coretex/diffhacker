import { describe, expect, it } from 'vitest';
import { formatBytes, formatCount, formatElapsed, formatTime } from './format';

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

describe('formatBytes', () => {
  it('is exact below a kilobyte and one decimal above, in binary units', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1023)).toBe('1,023 B');
    expect(formatBytes(1024)).toBe('1.0 KB');
    expect(formatBytes(48 * 1024)).toBe('48.0 KB');
    expect(formatBytes(3 * 1024 * 1024 + 512 * 1024)).toBe('3.5 MB');
  });
});

describe('formatElapsed', () => {
  it('counts whole seconds as m:ss, and hours only once there are any', () => {
    expect(formatElapsed(0)).toBe('0:00');
    expect(formatElapsed(999)).toBe('0:00');
    expect(formatElapsed(61_000)).toBe('1:01');
    expect(formatElapsed(10 * 60_000 + 5_000)).toBe('10:05');
    expect(formatElapsed(3_600_000 + 2 * 60_000 + 3_000)).toBe('1:02:03');
  });

  it('never goes negative when two clocks disagree by a moment', () => {
    expect(formatElapsed(-500)).toBe('0:00');
  });
});
