import { describe, expect, it } from 'vitest';
import { formatCount } from './format';

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
