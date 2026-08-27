import { applyD1Migrations } from 'cloudflare:test';
import { beforeAll, inject } from 'vitest';
import { env } from './env';

/**
 * Applies `migrations/*.sql` to the local D1 once per worker, using the migration list read in
 * `vitest.config.ts`. Individual test files clear rows between cases; they never redefine schema.
 */
beforeAll(async () => {
  await applyD1Migrations(env.DB, inject('migrations'));
});
