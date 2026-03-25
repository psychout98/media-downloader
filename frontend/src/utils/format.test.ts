import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { formatSize, formatMs, timeAgo, escapeHtml, hashColor } from './format';

// AC-23: Frontend — Utility Functions

describe('AC-23.1: formatSize', () => {
  it('returns "0 B" for 0', () => {
    expect(formatSize(0)).toBe('0 B');
  });

  it('formats bytes', () => {
    expect(formatSize(500)).toBe('500 B');
  });

  it('formats KB', () => {
    expect(formatSize(1024)).toBe('1.0 KB');
    expect(formatSize(1536)).toBe('1.5 KB');
  });

  it('formats MB', () => {
    expect(formatSize(1_048_576)).toBe('1.0 MB');
    expect(formatSize(5_242_880)).toBe('5.0 MB');
  });

  it('formats GB', () => {
    expect(formatSize(1_073_741_824)).toBe('1.0 GB');
    expect(formatSize(1_500_000_000)).toBe('1.4 GB');
  });

  it('formats TB', () => {
    expect(formatSize(1_099_511_627_776)).toBe('1.0 TB');
  });
});

describe('AC-23.2: formatMs', () => {
  it('returns "0:00" for negative', () => {
    expect(formatMs(-1000)).toBe('0:00');
  });

  it('returns "0:00" for zero', () => {
    expect(formatMs(0)).toBe('0:00');
  });

  it('formats seconds', () => {
    expect(formatMs(45000)).toBe('0:45');
  });

  it('formats minutes and seconds with padding', () => {
    expect(formatMs(125000)).toBe('2:05');
  });

  it('formats hours:minutes:seconds', () => {
    expect(formatMs(3661000)).toBe('1:01:01');
  });

  it('pads minutes and seconds in hours mode', () => {
    expect(formatMs(3600000)).toBe('1:00:00');
  });
});

describe('AC-23.3: timeAgo', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns "just now" within 60s', () => {
    const now = new Date('2024-01-15T12:00:00Z');
    vi.setSystemTime(now);
    const recent = new Date(now.getTime() - 30000).toISOString(); // 30s ago
    expect(timeAgo(recent)).toBe('just now');
  });

  it('returns "Xm ago" for minutes', () => {
    const now = new Date('2024-01-15T12:00:00Z');
    vi.setSystemTime(now);
    const fiveMinAgo = new Date(now.getTime() - 300000).toISOString();
    expect(timeAgo(fiveMinAgo)).toBe('5m ago');
  });

  it('returns "Xh ago" for hours', () => {
    const now = new Date('2024-01-15T12:00:00Z');
    vi.setSystemTime(now);
    const twoHoursAgo = new Date(now.getTime() - 7200000).toISOString();
    expect(timeAgo(twoHoursAgo)).toBe('2h ago');
  });

  it('returns "Xd ago" for days', () => {
    const now = new Date('2024-01-15T12:00:00Z');
    vi.setSystemTime(now);
    const threeDaysAgo = new Date(now.getTime() - 259200000).toISOString();
    expect(timeAgo(threeDaysAgo)).toBe('3d ago');
  });

  it('returns localized date for old dates', () => {
    const now = new Date('2024-01-15T12:00:00Z');
    vi.setSystemTime(now);
    const oldDate = new Date('2023-06-01T12:00:00Z').toISOString();
    const result = timeAgo(oldDate);
    // Should be a localized date string, not "Xd ago"
    expect(result).not.toContain('ago');
  });
});

describe('AC-23.4: escapeHtml', () => {
  it('escapes &', () => {
    expect(escapeHtml('a & b')).toBe('a &amp; b');
  });

  it('escapes <', () => {
    expect(escapeHtml('<div>')).toBe('&lt;div&gt;');
  });

  it('escapes >', () => {
    expect(escapeHtml('a > b')).toBe('a &gt; b');
  });

  it('escapes "', () => {
    expect(escapeHtml('say "hello"')).toBe('say &quot;hello&quot;');
  });

  it("escapes '", () => {
    expect(escapeHtml("it's")).toBe('it&#039;s');
  });

  it('handles normal strings unchanged', () => {
    expect(escapeHtml('hello world')).toBe('hello world');
  });

  it('handles empty strings', () => {
    expect(escapeHtml('')).toBe('');
  });
});

describe('AC-23.5: hashColor', () => {
  it('returns valid HSL string', () => {
    const result = hashColor('test');
    expect(result).toMatch(/^hsl\(\d+,\s*60%,\s*45%\)$/);
  });

  it('is deterministic', () => {
    expect(hashColor('hello')).toBe(hashColor('hello'));
  });

  it('produces different colors for different inputs', () => {
    expect(hashColor('abc')).not.toBe(hashColor('xyz'));
  });

  it('handles empty string', () => {
    const result = hashColor('');
    expect(result).toMatch(/^hsl\(\d+,\s*60%,\s*45%\)$/);
  });

  it('handles special characters', () => {
    const result = hashColor('!@#$%^&*()');
    expect(result).toMatch(/^hsl\(\d+,\s*60%,\s*45%\)$/);
  });

  it('handles unicode', () => {
    const result = hashColor('🎬🎥');
    expect(result).toMatch(/^hsl\(\d+,\s*60%,\s*45%\)$/);
  });
});
