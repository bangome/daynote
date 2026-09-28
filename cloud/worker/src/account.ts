import { revoke as revokeApple } from './apple';
import { APPLE_REFRESH_PURPOSE, authenticate } from './auth';
import { cancelForDeletion } from './billing';
import { openText } from './dek';
import { noContent } from './http';
import { ACCOUNT_DELETE_LIMITS, enforce } from './ratelimit';
import type { Env } from './env';

/**
 * Account deletion: `DELETE /v1/account` (docs/CLOUD_SYNC.md §4.12).
 *
 * Both stores require it — App Store guideline 5.1.1(v) and Google Play's account-deletion policy —
 * and it is the right thing regardless: a person who asks for their data to be gone should not
 * have to write to anyone. So it is immediate and permanent. There is no grace period, no soft
 * delete, and no copy kept "in case": this service is a sync relay, and every note it held is still
 * on the devices that pushed it.
 *
 * The order is what makes it safe to fail half-way and safe to retry:
 *
 * 1. **Billing first**, and it can refuse. An account that is gone but still renewing is the one
 *    outcome worse than not deleting, so a subscription this server cannot cancel stops everything
 *    before a byte is removed.
 * 2. **Apple's token revoked**, best effort. Apple requires it; a failure there is logged and does
 *    not keep the data.
 * 3. **R2 objects**, which no transaction covers. Deleting them before the rows means a failure
 *    after this step leaves an account whose attachments are gone but whose rows remain — and a
 *    retry, still authenticated because the user row survived, finishes the job. The other order
 *    could strand objects nobody can ever address again.
 * 4. **Every row, in one D1 batch**, which is a transaction: the account is either all there or
 *    all gone. Tables are deleted explicitly rather than by the ON DELETE CASCADE on `users`, so a
 *    table added later without a cascade cannot quietly survive a deletion.
 * 5. **R2 again**, for anything another device uploaded while steps 3 and 4 ran. If this pass
 *    fails the account is already gone and the objects are unreachable, but they are still ours
 *    to hold, so the error surfaces as a 500 and is logged rather than swallowed.
 *
 * After step 4 the user row is gone, so the caller's access token fails `authenticate` and its
 * refresh tokens no longer exist. A retry after success is therefore a 401, which the client should
 * read as "already deleted".
 *
 * `billing_events` keeps its rows — they are the record of payments Paddle made — but with the
 * account id cleared, so nothing in them leads back to anything the account stored.
 */

/** R2 lists and deletes at most 1000 keys per call. */
const R2_PAGE = 1000;

export async function remove(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  await enforce(env, ACCOUNT_DELETE_LIMITS(user.id), now);

  await cancelForDeletion(env, user.id, now);
  await revokeAppleToken(env, user.id, now);
  await deleteObjects(env, user.id);

  await env.DB.batch([
    env.DB.prepare('DELETE FROM change_log WHERE user_id = ?1').bind(user.id),
    env.DB.prepare('DELETE FROM notes WHERE user_id = ?1').bind(user.id),
    env.DB.prepare('DELETE FROM files WHERE user_id = ?1').bind(user.id),
    env.DB.prepare('DELETE FROM assets WHERE user_id = ?1').bind(user.id),
    env.DB.prepare('DELETE FROM refresh_tokens WHERE user_id = ?1').bind(user.id),
    env.DB.prepare('DELETE FROM subscriptions WHERE user_id = ?1').bind(user.id),
    // Buckets are "<action>:<scope>:<value>:<window>" (ratelimit.ts); per-account ones use the id.
    env.DB.prepare('DELETE FROM rate_limits WHERE bucket LIKE ?1').bind(`%:user:${user.id}:%`),
    env.DB.prepare('UPDATE billing_events SET user_id = NULL WHERE user_id = ?1').bind(user.id),
    env.DB.prepare('DELETE FROM users WHERE id = ?1').bind(user.id),
  ]);

  // Once more, now that the account is gone. Another signed-in device can upload between the first
  // pass and the batch, and that object would otherwise outlive the account with nothing pointing
  // at it. After the batch `authenticate` fails, so nothing new can land under the prefix, and this
  // pass leaves it empty. The first pass stays: it is what makes a retry after a failure part-way
  // through find the account still there to retry against.
  await deleteObjects(env, user.id);

  return noContent();
}

async function revokeAppleToken(env: Env, userId: string, now: Date): Promise<void> {
  const row = await env.DB.prepare('SELECT apple_refresh_token FROM users WHERE id = ?1')
    .bind(userId)
    .first<{ apple_refresh_token: string | null }>();
  if (row?.apple_refresh_token == null) {
    return;
  }

  try {
    await revokeApple(env, await openText(env, APPLE_REFRESH_PURPOSE, row.apple_refresh_token), now);
  } catch (error) {
    // An unopenable token means a rotated DEK_WRAP_KEY or a damaged row. Neither is a reason to
    // keep the account; say so in the log and carry on.
    console.error('apple revoke skipped', error instanceof Error ? error.message : String(error));
  }
}

/**
 * Deletes every object under the account's prefix — not just the ones `assets` knows about, so an
 * upload that landed without its row (assets.ts writes the row after the object) goes too.
 */
async function deleteObjects(env: Env, userId: string): Promise<void> {
  if (env.ASSET_BUCKET === undefined) {
    // Nothing can have been stored: the upload route refuses when the bucket is not bound.
    return;
  }

  // Objects are named "<user id>/<blinded key>" (assets.ts, files.ts).
  const prefix = `${userId}/`;
  for (;;) {
    const listed = await env.ASSET_BUCKET.list({ prefix, limit: R2_PAGE });
    if (listed.objects.length === 0) {
      return;
    }
    await env.ASSET_BUCKET.delete(listed.objects.map((object) => object.key));
    if (!listed.truncated) {
      return;
    }
  }
}
