/** JSON request/response plumbing and the single error shape the client parses. */

/**
 * Auth bodies are a few hundred bytes; anything larger is a bug or an attack. Sync bodies carry
 * ciphertext and are legitimately far bigger, so the limit is per route: a single cap sized for auth
 * would 413 every real push and sync would silently never progress.
 */
export const AUTH_BODY_LIMIT = 16 * 1024;
export const SYNC_BODY_LIMIT = 8 * 1024 * 1024;

export type ErrorCode =
  | 'bad_request'
  | 'invalid_credentials'
  | 'email_taken'
  | 'unauthorized'
  | 'not_found'
  | 'rate_limited'
  | 'payload_too_large'
  | 'server_error';

const STATUS: Record<ErrorCode, number> = {
  bad_request: 400,
  invalid_credentials: 401,
  unauthorized: 401,
  email_taken: 409,
  not_found: 404,
  rate_limited: 429,
  payload_too_large: 413,
  server_error: 500,
};

export class ApiError extends Error {
  constructor(
    readonly code: ErrorCode,
    message?: string,
    readonly retryAfterSeconds?: number,
  ) {
    super(message ?? code);
    this.name = 'ApiError';
  }

  get status(): number {
    return STATUS[this.code];
  }
}

export function json(body: unknown, status = 200, extraHeaders?: HeadersInit): Response {
  const headers = new Headers(extraHeaders);
  headers.set('content-type', 'application/json; charset=utf-8');
  // No browser reaches this API — a desktop client does — so lock the surface down.
  headers.set('cache-control', 'no-store');
  headers.set('x-content-type-options', 'nosniff');
  headers.set('referrer-policy', 'no-referrer');
  return new Response(JSON.stringify(body), { status, headers });
}

export function noContent(): Response {
  return new Response(null, { status: 204, headers: { 'cache-control': 'no-store' } });
}

export function errorResponse(error: ApiError): Response {
  const headers: Record<string, string> = {};
  if (error.retryAfterSeconds !== undefined) {
    headers['retry-after'] = String(error.retryAfterSeconds);
  }
  return json({ error: error.code, message: error.message }, error.status, headers);
}

export async function readJsonObject(
  request: Request,
  maxBytes: number = AUTH_BODY_LIMIT,
): Promise<Record<string, unknown>> {
  const declaredLength = Number(request.headers.get('content-length') ?? '0');
  if (declaredLength > maxBytes) {
    throw new ApiError('payload_too_large', 'The request body is too large.');
  }

  const raw = await request.text();
  if (raw.length > maxBytes) {
    throw new ApiError('payload_too_large', 'The request body is too large.');
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new ApiError('bad_request', 'The request body must be JSON.');
  }

  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new ApiError('bad_request', 'The request body must be a JSON object.');
  }

  return parsed as Record<string, unknown>;
}

export function clientIp(request: Request): string {
  return request.headers.get('cf-connecting-ip') ?? 'unknown';
}

export function bearerToken(request: Request): string | null {
  const header = request.headers.get('authorization');
  if (header === null) {
    return null;
  }

  const match = /^Bearer (\S+)$/.exec(header);
  return match === null ? null : match[1]!;
}
