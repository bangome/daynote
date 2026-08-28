/**
 * Markdown files are bundled as text (see the Text rule in wrangler.toml), which is how the privacy
 * page serves docs/PRIVACY.md itself rather than a copy of it.
 */
declare module '*.md' {
  const content: string;
  export default content;
}
