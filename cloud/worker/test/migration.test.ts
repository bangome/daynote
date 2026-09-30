import { applyD1Migrations } from 'cloudflare:test';
import { afterAll, describe, expect, it, inject } from 'vitest';
import { env } from './helpers';

/**
 * 0009 rebuilds `users` under tables that point at it with ON DELETE CASCADE. A fresh database
 * cannot show what that does to existing rows — every other test starts empty — so this one winds
 * the schema back to 0008, fills every table the way production is filled, and applies 0009 on top.
 *
 * It uses the shared test database and leaves it fully migrated again, so the order files run in
 * does not matter.
 */

const TABLES = [
  'change_log', 'files', 'assets', 'notes', 'billing_events', 'subscriptions', 'refresh_tokens',
  'reset_tokens', 'rate_limits', 'users', 'users_new', 'd1_migrations',
];

async function windBackTo0008(): Promise<void> {
  for (const table of TABLES) {
    await env.DB.prepare(`DROP TABLE IF EXISTS ${table}`).run();
  }
  const migrations = inject('migrations');
  const upTo0008 = migrations.filter((migration) => migration.name < '0009');
  expect(upTo0008.map((migration) => migration.name).at(-1)).toBe('0008_trial_backfill.sql');
  await applyD1Migrations(env.DB, upTo0008);
}

const USER = '11111111-1111-4111-8111-111111111111';
const NOTE = '22222222-2222-4222-8222-222222222222';
const STAMP = '2026-09-01T00:00:00.0000000Z';

async function populate(): Promise<void> {
  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO users (id, google_sub, email, protection, wrapped_dek, created_utc, last_seen_utc,
                          trial_ends_utc)
       VALUES (?1, 'google-sub-old', 'old@example.test', 'server', 's1.sealed', ?2, ?2, ?2)`,
    ).bind(USER, STAMP),
    env.DB.prepare(
      `INSERT INTO notes (user_id, id, payload, updated_utc) VALUES (?1, ?2, 'v1.x.y', ?3)`,
    ).bind(USER, NOTE, STAMP),
    env.DB.prepare(
      `INSERT INTO change_log (user_id, entity, entity_id, written_utc) VALUES (?1, 'note', ?2, ?3)`,
    ).bind(USER, NOTE, STAMP),
    env.DB.prepare(
      `INSERT INTO refresh_tokens (token_hash, user_id, family_id, device_name, issued_utc, expires_utc)
       VALUES ('hash', ?1, 'family', 'PC', ?2, ?2)`,
    ).bind(USER, STAMP),
    env.DB.prepare(
      `INSERT INTO subscriptions (user_id, status, updated_utc) VALUES (?1, 'active', ?2)`,
    ).bind(USER, STAMP),
    env.DB.prepare(
      `INSERT INTO files (user_id, id, payload, blinded_key, stored_bytes, updated_utc)
       VALUES (?1, ?2, 'v1.x.y', ?3, 10, ?4)`,
    ).bind(USER, NOTE, 'a'.repeat(64), STAMP),
    env.DB.prepare(
      `INSERT INTO assets (user_id, blinded_key, stored_bytes, ref_count) VALUES (?1, ?2, 10, 1)`,
    ).bind(USER, 'a'.repeat(64)),
  ]);
}

async function count(table: string): Promise<number> {
  const row = await env.DB.prepare(`SELECT COUNT(*) AS n FROM ${table}`).first<{ n: number }>();
  return row?.n ?? -1;
}

afterAll(async () => {
  // Whatever happened above, hand the next file a fully migrated database.
  await applyD1Migrations(env.DB, inject('migrations'));
});

describe('0009 over an existing database', () => {
  it('keeps every Google account and every row that hangs off one', async () => {
    await windBackTo0008();
    await populate();

    await applyD1Migrations(env.DB, inject('migrations'));

    const user = await env.DB.prepare(
      'SELECT google_sub, apple_sub, apple_refresh_token, email, wrapped_dek, trial_ends_utc FROM users WHERE id = ?1',
    )
      .bind(USER)
      .first();
    expect(user).toEqual({
      google_sub: 'google-sub-old',
      apple_sub: null,
      apple_refresh_token: null,
      email: 'old@example.test',
      wrapped_dek: 's1.sealed',
      trial_ends_utc: STAMP,
    });

    // The rebuild's DROP must not have cascaded into the children.
    for (const table of ['notes', 'change_log', 'refresh_tokens', 'subscriptions', 'files', 'assets']) {
      expect(await count(table), table).toBe(1);
    }
  });

  it('leaves the children pointing at the rebuilt table, cascade included', async () => {
    for (const table of ['notes', 'refresh_tokens', 'subscriptions', 'files', 'assets']) {
      const { results } = await env.DB.prepare(`PRAGMA foreign_key_list(${table})`).all<{
        table: string;
        on_delete: string;
      }>();
      expect(results, table).toEqual([
        expect.objectContaining({ table: 'users', on_delete: 'CASCADE' }),
      ]);
    }

    // Nothing may still point at the temporary name, or the next rebuild would inherit it.
    const { results: stale } = await env.DB.prepare(
      "SELECT name FROM sqlite_master WHERE sql LIKE '%users_new%' OR name LIKE '%_new'",
    ).all();
    expect(stale).toEqual([]);
  });

  it('recreates every index the earlier migrations made', async () => {
    const { results } = await env.DB.prepare(
      "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%' ORDER BY name",
    ).all<{ name: string }>();
    expect(results.map((row) => row.name)).toEqual([
      'assets_user_unreferenced',
      'billing_events_received',
      'billing_events_user',
      'change_log_user_seq',
      'rate_limits_expiry',
      'refresh_tokens_family',
      'refresh_tokens_user',
      'subscriptions_customer',
      'subscriptions_subscription',
      'users_email',
    ]);
  });

  it('still refuses a child row with no account behind it', async () => {
    await expect(
      env.DB.prepare(
        `INSERT INTO notes (user_id, id, payload, updated_utc) VALUES ('no-such-user', 'n', NULL, ?1)`,
      )
        .bind(STAMP)
        .run(),
    ).rejects.toThrow(/FOREIGN KEY/);
  });

  it('allows an Apple-only account and refuses one with no identity at all', async () => {
    await env.DB.prepare(
      `INSERT INTO users (id, apple_sub, email, wrapped_dek, created_utc, last_seen_utc)
       VALUES ('apple-only', 'apple-sub', '', 's1.sealed', ?1, ?1)`,
    )
      .bind(STAMP)
      .run();

    await expect(
      env.DB.prepare(
        `INSERT INTO users (id, email, wrapped_dek, created_utc, last_seen_utc)
         VALUES ('nobody', 'x@example.test', 's1.sealed', ?1, ?1)`,
      )
        .bind(STAMP)
        .run(),
    ).rejects.toThrow(/CHECK/);

    // Still UNIQUE, for both providers.
    await expect(
      env.DB.prepare(
        `INSERT INTO users (id, apple_sub, email, wrapped_dek, created_utc, last_seen_utc)
         VALUES ('apple-dup', 'apple-sub', '', 's1.sealed', ?1, ?1)`,
      )
        .bind(STAMP)
        .run(),
    ).rejects.toThrow(/UNIQUE/);
    await expect(
      env.DB.prepare(
        `INSERT INTO users (id, google_sub, email, wrapped_dek, created_utc, last_seen_utc)
         VALUES ('google-dup', 'google-sub-old', '', 's1.sealed', ?1, ?1)`,
      )
        .bind(STAMP)
        .run(),
    ).rejects.toThrow(/UNIQUE/);
  });

  it('keeps the lock invariant', async () => {
    await expect(
      env.DB.prepare(
        `INSERT INTO users (id, google_sub, email, protection, wrapped_dek, created_utc, last_seen_utc)
         VALUES ('bad-lock', 'g-lock', 'x@example.test', 'passphrase', 's1.sealed', ?1, ?1)`,
      )
        .bind(STAMP)
        .run(),
    ).rejects.toThrow(/CHECK/);
  });
});

describe('0010 over an existing database', () => {
  it('makes every existing subscription Pro, and keeps any quota an operator had set', async () => {
    await windBackTo0008();
    await populate();
    const upTo0009 = inject('migrations').filter((migration) => migration.name < '0010');
    await applyD1Migrations(env.DB, upTo0009);
    await env.DB.prepare(
      `INSERT INTO users (id, google_sub, email, wrapped_dek, quota_bytes, created_utc, last_seen_utc)
       VALUES ('granted', 'g-granted', 'granted@example.test', 's1.sealed', 10737418240, ?1, ?1)`,
    )
      .bind(STAMP)
      .run();

    await applyD1Migrations(env.DB, inject('migrations'));

    const subscription = await env.DB.prepare('SELECT status, tier, plan, price_id FROM subscriptions WHERE user_id = ?1')
      .bind(USER)
      .first();
    expect(subscription).toEqual({ status: 'active', tier: 'pro', plan: null, price_id: null });

    const { results } = await env.DB.prepare('SELECT id, quota_override_bytes FROM users ORDER BY id').all();
    // The default 2 GiB was never a decision, so it now follows the tier; a raised figure was one.
    expect(results).toEqual([
      { id: USER, quota_override_bytes: null },
      { id: 'granted', quota_override_bytes: 10737418240 },
    ]);
  });
});
