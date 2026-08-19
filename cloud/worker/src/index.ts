import { ApiError, errorResponse, json } from './http';
import { sweep } from './ratelimit';
import { canonicalUtc } from './time';
import * as auth from './auth';
import type { Env } from './env';

/**
 * Daynote cloud sync Worker — Phase 1: accounts and sessions only.
 *
 * The sync and asset routes land in later phases. This Worker holds auth secrets but never a
 * content-encryption key: every note body it will eventually store arrives already encrypted.
 * See docs/CLOUD_SYNC.md.
 */

type Handler = (request: Request, env: Env, now: Date) => Promise<Response>;

const ROUTES: Record<string, Handler> = {
  'POST /v1/auth/register': auth.register,
  'POST /v1/auth/login': auth.login,
  'POST /v1/auth/refresh': auth.refresh,
  'POST /v1/auth/logout': auth.logout,
  'POST /v1/auth/password': auth.changePassword,
  'GET /v1/auth/me': auth.me,
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
