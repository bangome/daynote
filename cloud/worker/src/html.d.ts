/**
 * Built site pages are bundled as text (wrangler's default Text rule covers `**\/*.html`), which is
 * how `/delete-account` serves the page `cloud/site` built rather than a copy of it.
 */
declare module '*.html' {
  const content: string;
  export default content;
}
