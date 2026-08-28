import { describe, expect, it } from 'vitest';
import policySource from '../../../docs/PRIVACY.md';
import { renderMarkdown } from '../src/privacy';
import { env } from './helpers';

const BASE = 'https://daynote.test';

async function fetchPath(path: string, method = 'GET'): Promise<Response> {
  const worker = (await import('../src/index')).default;
  const ctx = { waitUntil: () => {}, passThroughOnException: () => {} } as unknown as ExecutionContext;
  return worker.fetch(new Request(`${BASE}${path}`, { method }), env as any, ctx);
}

describe('GET /privacy', () => {
  it('serves the repository policy as HTML', async () => {
    const response = await fetchPath('/privacy');

    expect(response.status).toBe(200);
    expect(response.headers.get('content-type')).toBe('text/html; charset=utf-8');

    const html = await response.text();
    expect(html).toContain('<h1>Privacy</h1>');
    expect(html).toContain('<title>Daynote — Privacy</title>');
  });

  it('serves docs/PRIVACY.md itself, not a copy that can drift from it', async () => {
    const html = await (await fetchPath('/privacy')).text();

    // Every heading in the document has to appear on the page. A hand-published copy is exactly
    // what goes stale when a release changes what can leave the user's PC.
    const headings = [...policySource.matchAll(/^## (.+)$/gm)].map((match) => match[1]!);
    expect(headings.length).toBeGreaterThan(4);
    for (const heading of headings) {
      expect(html).toContain(`<h2>${heading.replace(/`([^`]+)`/g, '<code>$1</code>')}</h2>`);
    }
  });

  it('is answered without touching the API surface', async () => {
    // Only GET is the page; anything else falls through to the routing table and 404s like any
    // other unknown endpoint, so the page cannot be used as an unlisted POST target.
    const response = await fetchPath('/privacy', 'POST');

    expect(response.status).toBe(404);
  });
});

describe('renderMarkdown', () => {
  it('renders the constructs the policy uses', () => {
    const html = renderMarkdown(
      [
        '# Title',
        '',
        'A paragraph with **bold**, *italic*, `code`, and a [link](https://example.test).',
        '',
        '## Section',
        '',
        '- first item that wraps',
        '  onto a second line',
        '- second item',
      ].join('\n'),
    );

    expect(html).toContain('<h1>Title</h1>');
    expect(html).toContain('<h2>Section</h2>');
    expect(html).toContain('<strong>bold</strong>');
    expect(html).toContain('<em>italic</em>');
    expect(html).toContain('<code>code</code>');
    expect(html).toContain('<a href="https://example.test">link</a>');
    expect(html).toContain('<li>first item that wraps onto a second line</li>');
    expect(html).toContain('<li>second item</li>');
  });

  it('escapes markup so the document cannot inject any of its own', () => {
    const html = renderMarkdown('A <script>alert(1)</script> line & an ampersand.');

    expect(html).not.toContain('<script>');
    expect(html).toContain('&lt;script&gt;');
    expect(html).toContain('&amp;');
  });

  it('closes emphasis that opens before a code span and ends after it', () => {
    // This shipped once: the two asterisks fell either side of a code span, matched in neither
    // half, and the sentence published with its markers showing.
    const html = renderMarkdown('*Published at `https://example.test/privacy`, from this file.*');

    expect(html).toContain('<em>');
    expect(html).toContain('<code>https://example.test/privacy</code>');
    expect(html).not.toContain('*');
  });

  it('leaves emphasis characters inside code spans alone', () => {
    const html = renderMarkdown('Use `%LocalAppData%\Daynote` and `**not bold**` here.');

    expect(html).toContain('<code>**not bold**</code>');
    expect(html).not.toContain('<strong>not bold</strong>');
  });
});
