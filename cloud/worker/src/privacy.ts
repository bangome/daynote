import source from '../../../docs/PRIVACY.md';

/**
 * Serves the privacy policy the Microsoft Store listing links to.
 *
 * The page is `docs/PRIVACY.md` itself, bundled as text — not a copy. A copy is the whole problem
 * this route exists to remove: the policy has to be updated for every release that changes what
 * leaves the user's PC, and a hand-published page drifts from the document that the code review
 * actually looked at. Deploying the Worker now republishes the policy.
 *
 * The renderer covers only the Markdown this one document uses. That is deliberate: a general
 * Markdown dependency in a Worker that holds auth secrets is a supply-chain risk out of all
 * proportion to rendering one static page.
 */

const ESCAPES: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
};

function escapeHtml(text: string): string {
  return text.replace(/[&<>"]/g, (character) => ESCAPES[character] ?? character);
}

/**
 * Inline spans. Code is resolved first and its contents left alone, so a path or a placeholder
 * inside backticks is never re-read as emphasis.
 */
function renderInline(text: string): string {
  return escapeHtml(text)
    .split('`')
    .map((piece, index) => {
      if (index % 2 === 1) {
        return `<code>${piece}</code>`;
      }

      return piece
        .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, '<a href="$2">$1</a>')
        .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
        .replace(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>');
    })
    .join('');
}

/** Blocks: headings, bullet lists, and paragraphs, each of which may wrap across source lines. */
export function renderMarkdown(markdown: string): string {
  const html: string[] = [];
  let paragraph: string[] = [];
  let items: string[] = [];

  const flushParagraph = (): void => {
    if (paragraph.length > 0) {
      html.push(`<p>${renderInline(paragraph.join(' '))}</p>`);
      paragraph = [];
    }
  };

  const flushList = (): void => {
    if (items.length > 0) {
      html.push(`<ul>${items.map((item) => `<li>${renderInline(item)}</li>`).join('')}</ul>`);
      items = [];
    }
  };

  for (const raw of markdown.split(/\r?\n/)) {
    const line = raw.trimEnd();

    if (line.trim() === '') {
      flushParagraph();
      flushList();
      continue;
    }

    const heading = /^(#{1,4})\s+(.*)$/.exec(line);
    if (heading !== null) {
      flushParagraph();
      flushList();
      const level = heading[1]!.length;
      html.push(`<h${level}>${renderInline(heading[2]!)}</h${level}>`);
      continue;
    }

    const bullet = /^-\s+(.*)$/.exec(line);
    if (bullet !== null) {
      flushParagraph();
      items.push(bullet[1]!);
      continue;
    }

    // An indented line continues whichever block is open: a wrapped bullet, or a wrapped paragraph.
    if (/^\s+/.test(line) && items.length > 0) {
      items[items.length - 1] = `${items[items.length - 1]!} ${line.trim()}`;
      continue;
    }

    flushList();
    paragraph.push(line.trim());
  }

  flushParagraph();
  flushList();
  return html.join('\n');
}

const STYLE = `
:root { color-scheme: light dark; }
body {
  margin: 0 auto; padding: 2rem 1.25rem 4rem; max-width: 46rem;
  font: 16px/1.65 -apple-system, "Segoe UI", "Malgun Gothic", system-ui, sans-serif;
  color: #1b1b1f; background: #fff;
}
h1 { font-size: 1.9rem; margin: 0 0 .5rem; }
h2 { font-size: 1.3rem; margin: 2.2rem 0 .6rem; }
h3 { font-size: 1.05rem; margin: 1.6rem 0 .4rem; }
p, li { margin: .6rem 0; }
ul { padding-left: 1.3rem; }
code { font-family: Consolas, "Cascadia Mono", monospace; font-size: .9em;
       background: #f2f2f4; padding: .1em .35em; border-radius: 3px; word-break: break-all; }
a { color: #0067c0; }
strong { font-weight: 650; }
@media (prefers-color-scheme: dark) {
  body { color: #e6e6e9; background: #16161a; }
  code { background: #26262c; }
  a { color: #4cc2ff; }
}
`.trim();

const PAGE = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Daynote — Privacy</title>
<style>${STYLE}</style>
</head>
<body>
${renderMarkdown(source)}
</body>
</html>
`;

/**
 * The only public, unauthenticated page this service serves. It reads nothing, writes nothing, and
 * touches neither D1 nor the rate limiter, so it cannot become a lever on the account endpoints.
 */
export function privacyPage(): Response {
  return new Response(PAGE, {
    headers: {
      'content-type': 'text/html; charset=utf-8',
      'cache-control': 'public, max-age=3600',
      'x-content-type-options': 'nosniff',
    },
  });
}
