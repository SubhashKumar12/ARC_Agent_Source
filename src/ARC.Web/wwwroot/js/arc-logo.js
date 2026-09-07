/** Self-contained ARC logo SVG with scoped dimensions (no global svg rules). */
let arcLogoCounter = 0;

function arcLogoSvg(className, suffix) {
    const id = suffix || `js${++arcLogoCounter}`;
    const gl = `arcGradLeft-${id}`;
    const gr = `arcGradRight-${id}`;
    const gb = `arcGradBar-${id}`;
    const cls = className || 'arc-logo';

    return `<svg class="${cls}" viewBox="0 0 48 48" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
  <defs>
    <linearGradient id="${gl}" x1="8" y1="8" x2="28" y2="42" gradientUnits="userSpaceOnUse">
      <stop stop-color="#93C5FD"/><stop offset="1" stop-color="#2563EB"/>
    </linearGradient>
    <linearGradient id="${gr}" x1="20" y1="8" x2="42" y2="42" gradientUnits="userSpaceOnUse">
      <stop stop-color="#60A5FA"/><stop offset="1" stop-color="#1D4ED8"/>
    </linearGradient>
    <linearGradient id="${gb}" x1="14" y1="30" x2="34" y2="36" gradientUnits="userSpaceOnUse">
      <stop stop-color="#3B82F6"/><stop offset="1" stop-color="#1E40AF"/>
    </linearGradient>
  </defs>
  <path d="M6 42L24 6L24 20.5L12.5 42H6Z" fill="url(#${gl})"/>
  <path d="M42 42H35.5L24 20.5L24 6L42 42Z" fill="url(#${gr})"/>
  <path d="M14.5 30.5H33.5L31.8 35.8H16.2L14.5 30.5Z" fill="url(#${gb})"/>
</svg>`;
}

function arcLogoInAvatar(suffix) {
    return `<span class="arc-assistant-avatar-inner">${arcLogoSvg('arc-logo arc-logo-avatar', suffix)}</span>`;
}
