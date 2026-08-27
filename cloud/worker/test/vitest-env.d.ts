import type { D1Migration } from '@cloudflare/vitest-pool-workers';

declare module 'vitest' {
  interface ProvidedContext {
    /** Provided by `vitest.config.ts`; applied to the local D1 by `test/setup.ts`. */
    migrations: D1Migration[];
  }
}
