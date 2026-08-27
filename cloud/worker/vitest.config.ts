import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';
import { cloudflareTest, readD1Migrations } from '@cloudflare/vitest-pool-workers';

/**
 * Tests run inside workerd against a real (local) D1, so SQL, constraint violations, and `batch()`
 * semantics are exercised for real rather than against a mock that would happily accept invalid SQL.
 *
 * Migrations are read here (in Node) and applied inside the worker by `test/setup.ts`, so the suite
 * runs the same `migrations/*.sql` that ships — a migration that does not parse fails the suite.
 */
export default defineConfig(async () => {
  const migrations = await readD1Migrations(
    fileURLToPath(new URL('./migrations', import.meta.url)),
  );

  return {
    plugins: [
      cloudflareTest({
        wrangler: { configPath: './wrangler.toml' },
        miniflare: {
          bindings: {
            JWT_SECRET: 'test-secret-that-is-long-enough-to-pass-validation',
            ACCESS_TOKEN_TTL_SECONDS: '900',
            REFRESH_TOKEN_TTL_DAYS: '60',
          },
        },
      }),
    ],
    test: {
      setupFiles: ['./test/setup.ts'],
      provide: { migrations },
    },
  };
});
