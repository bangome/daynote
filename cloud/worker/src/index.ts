import { ApiError, errorResponse, json } from './http';
import { privacyPage } from './privacy';
import { sweep } from './ratelimit';
import { canonicalUtc } from './time';
import * as auth from './auth';
import * as billing from './billing';
import * as sync from './sync';
import type { Env } from './env';

/**
 * Daynote cloud sync Worker — Google sign-in, sessions, and note sync.
 *
 * The asset routes land with the R2 phase. Note bodies arrive encrypted; by default this Worker also
 * holds the key that opens them (src/dek.ts), so sync is encrypted in transit and at rest but is NOT
 * end-to-end encrypted. An account that turns on the opt-in lock takes that key away from us
 * (/v1/auth/protect). See docs/CLOUD_SYNC.md §1 and §4.1b.
 */

type Handler = (request: Request, env: Env, now: Date) => Promise<Response>;

const ROUTES: Record<string, Handler> = {
  'POST /v1/auth/google': auth.google,
  'POST /v1/auth/refresh': auth.refresh,
  'POST /v1/auth/logout': auth.logout,
  'GET /v1/auth/me': auth.me,
  'GET /v1/auth/data-key': auth.dataKey,
  'POST /v1/auth/protect': auth.protect,
  'POST /v1/auth/unprotect': auth.unprotect,
  'GET /v1/billing/status': billing.status,
  'POST /v1/billing/checkout': billing.checkout,
  'POST /v1/billing/portal': billing.portal,
  'POST /v1/billing/webhook': billing.webhook,
  'POST /v1/sync/push': sync.push,
  'GET /v1/sync/pull': sync.pull,
};

/** Chance per request of tidying expired rate-limit rows, in place of a cron trigger. */
const SWEEP_PROBABILITY = 0.02;

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);
    const now = new Date();

    if (url.pathname === '/v1/health') {
      return json({ ok: true, server_utc: canonicalUtc(now) });
    }

    // The privacy policy the Store listing links to, served from docs/PRIVACY.md itself so the
    // published page cannot drift from the document in the repository. Answered before the API
    // routing table: it is a static page, and it must not reach D1 or the rate limiter.
    if (request.method === 'GET' && url.pathname === '/privacy') {
      return privacyPage();
    }

    const handler = ROUTES[`${request.method} ${url.pathname}`];
    if (handler === undefined) {
      return errorResponse(new ApiError('not_found', 'No such endpoint.'));
    }

    try {
      const response = await handler(request, env, now);
      if (Math.random() < SWEEP_PROBABILITY) {
        ctx.waitUntil(sweep(env, now));
      }
      return response;
    } catch (error) {
      if (error instanceof ApiError) {
        return errorResponse(error);
      }

      // Log the detail for us, return nothing useful to the caller. A stack trace in a response
      // body is how schema and secret names leak.
      console.error('unhandled', error instanceof Error ? error.stack : String(error));
      return errorResponse(new ApiError('server_error', 'Something went wrong.'));
    }
  },
} satisfies ExportedHandler<Env>;
