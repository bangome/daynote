import { env as rawTestEnv } from 'cloudflare:test';
import type { Env } from '../src/env';

/**
 * The pool types the test env from generated Cloudflare binding types, which this project does not
 * check in. Asserting our own `Env` shape once, here, keeps the cast out of every test file.
 */
export const env = rawTestEnv as unknown as Env;
