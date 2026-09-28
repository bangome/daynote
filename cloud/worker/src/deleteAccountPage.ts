import korean from '../../site/public/delete-account/index.html';
import english from '../../site/public/en/delete-account/index.html';

/**
 * `GET /delete-account` — the account-deletion page Google Play's Data safety form links to.
 *
 * The pages themselves are the brand site's (`cloud/site/content/delete-account.*.html`, built into
 * `public/`), and the static assets already serve them at `/delete-account/` and
 * `/en/delete-account/`. This route exists for the bare path without the slash: that is the URL on
 * the store listing, and the assets layer would answer it with a redirect, which a reviewer's link
 * checker does not always follow. Serving the built file rather than a copy keeps one source.
 *
 * The site's default language is Korean (`/` is ko, `/en/` is en), but a store reviewer is more
 * likely to read English, so this one path picks by Accept-Language and falls back to English.
 * Like `/privacy`, it reads nothing and touches neither D1 nor the rate limiter.
 */
export function deleteAccountPage(request: Request): Response {
  const preferred = (request.headers.get('accept-language') ?? '').trim().toLowerCase();
  return new Response(preferred.startsWith('ko') ? korean : english, {
    headers: {
      'content-type': 'text/html; charset=utf-8',
      'cache-control': 'public, max-age=3600',
      'vary': 'accept-language',
      'x-content-type-options': 'nosniff',
    },
  });
}
