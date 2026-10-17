/**
 * QualGuard IQ brand tokens. One place for the logo colours so the theme, charts and status tones never repeat hex values.
 * Contrast (WCAG): white on blue 6.59:1, white on hover 8.19:1, white on dark 9.75:1, dark text on light blue 5.78:1,
 * dark-mode blue text on #121821 5.50:1. The main blue is not used for text on dark backgrounds (2.70:1).
 */
export const brand = {
  blue: '#1257C4',
  blueLight: '#4A86E0',
  blueDark: '#0B3F94',
  blueHover: '#0E4AA8',
  blueOnDark: '#4A8FF0',
  charcoal: '#29333E',
  tagline: '#3A4552',
  nearWhite: '#F1F4F8',
  copper: '#B4632A',
} as const;

/** Logo files served from web/public/brand (vector first; PNGs for places that cannot draw SVG). */
export const logos = {
  full: { light: '/brand/logo-full.svg', dark: '/brand/logo-full-dark.svg' },
  mark: { light: '/brand/mark.svg', dark: '/brand/mark-dark.svg' },
  wordmark: { light: '/brand/wordmark.svg', dark: '/brand/wordmark-dark.svg' },
} as const;

/** Neutral tones for status and data visualisation, readable on light and dark surfaces. */
export const tones = {
  ok: '#2E7D4F',
  warn: '#B4842A',
  bad: '#A4473B',
  info: '#2F6690',
  slate: '#4F6F7A',
  primary: brand.blue,
  accent: brand.copper,
} as const;
