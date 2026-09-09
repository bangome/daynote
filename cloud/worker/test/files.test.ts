import { beforeEach, describe, expect, it } from 'vitest';
import {
  env,
  expireEntitlement,
  get,
  getAsset,
  grantSubscription,
  post,
  putAsset,
  resetDatabase,
  signIn,
  toBase64Url,
  type Account,
} from './helpers';

/**
 * Attachment sync (docs/CLOUD_SYNC.md §5, Phase 7).
 *
 * Two properties are worth more than the rest and are tested hardest: **nothing is deleted by
 * lapsing**, and **a delete always propagates**. Everything else here — refcounts, quota, the
 * shared cursor — exists to keep those two true.
 */

beforeEach(resetDatabase);

function envelope(marker = 'x'): string {
  const nonce = toBase64Url(crypto.getRandomValues(new Uint8Array(12)));
  const body = toBase64Url(new TextEncoder().encode(marker.padEnd(32, '.')));
  return `v1.${nonce}.${body}`;
}

function stamp(minute: number): string {
  return `2026-09-10T09:${String(minute).padStart(2, '0')}:00.0000000Z`;
}

function fileId(suffix: number): string {
  return `00000000-0000-4000-8000-${String(suffix).padStart(12, '0')}`;
}

/** A blinded key is 64 hex digits; its value is opaque, so any stable one will do. */
function blinded(suffix: number): string {
  return String(suffix).padStart(64, 'a');
}

function fileEntry(id: number, key: number, options: { at?: number; bytes?: number } = {}) {
  return {
    id: fileId(id),
    payload: envelope(`file-${id}`),
    blinded_key: blinded(key),
    stored_bytes: options.bytes ?? 1024,
    updated_utc: stamp(options.at ?? 0),
  };
}

/** A signed-in account that is paying, which is the ordinary case for everything file-related. */
async function subscriber(): Promise<{ account: Account; token: string }> {
  const account = await signIn();
  await grantSubscription(account.userId);
  return { account, token: account.accessToken };
}

async function assetRow(userId: string, key: string) {
  return env.DB.prepare(
    'SELECT stored_bytes, ref_count, uploaded_utc FROM assets WHERE user_id = ?1 AND blinded_key = ?2',
  )
    .bind(userId, key)
    .first<{ stored_bytes: number; ref_count: number; uploaded_utc: string | null }>();
}

describe('file metadata push', () => {
  it('stores a file and makes it pullable', async () => {
    const { token } = await subscriber();

    const pushed = await post('/v1/files/push', { files: [fileEntry(1, 1)] }, { token });
    expect(pushed.status).toBe(200);
    expect(pushed.body.accepted_files).toEqual([fileId(1)]);
    expect(pushed.body.files_blocked).toBe(false);

    const pulled = await get('/v1/sync/pull?since=0', { token });
    expect(pulled.body.changes).toHaveLength(1);
    expect(pulled.body.changes[0]).toMatchObject({
      entity: 'file',
      id: fileId(1),
      blinded_key: blinded(1),
      stored_bytes: 1024,
    });
  });

  it('rejects a push older than what is stored', async () => {
    const { token } = await subscriber();
    await post('/v1/files/push', { files: [fileEntry(1, 1, { at: 10 })] }, { token });

    const stale = await post('/v1/files/push', { files: [fileEntry(1, 2, { at: 5 })] }, { token });

    expect(stale.body.accepted_files).toEqual([]);
    expect(stale.body.rejected_files).toEqual([fileId(1)]);
  });

  it('refuses junk rather than storing it', async () => {
    const { token } = await subscriber();

    const badKey = await post(
      '/v1/files/push',
      { files: [{ ...fileEntry(1, 1), blinded_key: 'not-hex' }] },
      { token },
    );
    expect(badKey.status).toBe(400);

    const badPayload = await post(
      '/v1/files/push',
      { files: [{ ...fileEntry(1, 1), payload: 'plaintext' }] },
      { token },
    );
    expect(badPayload.status).toBe(400);
  });
});

describe('what a lapsed subscription may still do', () => {
  it('refuses new attachments but says so distinctly, so the client keeps them queued', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);

    const response = await post(
      '/v1/files/push',
      { files: [fileEntry(1, 1)] },
      { token: account.accessToken },
    );

    // 200, not 402: a refused upsert must not fail a request whose tombstones have to land.
    expect(response.status).toBe(200);
    expect(response.body.files_blocked).toBe(true);
    expect(response.body.accepted_files).toEqual([]);
    // Not in `rejected_files` either — that would tell the client the item is settled and drop it.
    expect(response.body.rejected_files).toEqual([]);
  });

  it('still accepts a delete', async () => {
    const { account, token } = await subscriber();
    await post('/v1/files/push', { files: [fileEntry(1, 1)] }, { token });
    await expireEntitlement(account.userId);

    const response = await post(
      '/v1/files/push',
      { tombstones: [{ id: fileId(1), deleted_utc: stamp(30) }] },
      { token },
    );

    expect(response.body.accepted_tombstones).toEqual([fileId(1)]);
    // A delete that could not be sent would resurrect the attachment on every other device.
    const pulled = await get('/v1/sync/pull?since=0', { token });
    expect(pulled.body.changes[0].deleted_utc).toBe(stamp(30));
  });

  it('keeps every byte already uploaded', async () => {
    const { account, token } = await subscriber();
    await putAsset(blinded(1), new Uint8Array([1, 2, 3, 4]), token);
    await post('/v1/files/push', { files: [fileEntry(1, 1)] }, { token });

    await expireEntitlement(account.userId);

    // The row and the object are untouched; only access to them stops.
    expect(await assetRow(account.userId, blinded(1))).toMatchObject({ ref_count: 1 });
    expect(await env.ASSET_BUCKET!.get(`${account.userId}/${blinded(1)}`)).not.toBeNull();

    const denied = await getAsset(blinded(1), token);
    expect(denied.status).toBe(402);
  });
});

describe('asset bytes', () => {
  it('round-trips the sealed bytes unchanged', async () => {
    const { token } = await subscriber();
    const bytes = crypto.getRandomValues(new Uint8Array(2048));

    const stored = await putAsset(blinded(1), bytes, token);
    expect(stored.status).toBe(200);
    expect(stored.body.stored_bytes).toBe(2048);

    const fetched = await getAsset(blinded(1), token);
    expect(fetched.status).toBe(200);
    expect(fetched.bytes).toEqual(bytes);
  });

  it('needs a subscription in both directions', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);

    expect((await putAsset(blinded(1), new Uint8Array([1]), account.accessToken)).status).toBe(402);
    expect((await getAsset(blinded(1), account.accessToken)).status).toBe(402);
  });

  it('answers 404 when the metadata arrived before the bytes', async () => {
    const { token } = await subscriber();
    await post('/v1/files/push', { files: [fileEntry(1, 1)] }, { token });

    // Ordinary, and a retry rather than a corruption: the two calls are not atomic.
    expect((await getAsset(blinded(1), token)).status).toBe(404);
  });

  it('cannot be read across accounts, even knowing the key', async () => {
    const { token } = await subscriber();
    await putAsset(blinded(1), new Uint8Array([9, 9, 9]), token);

    const other = await signIn({ subject: 'other-sub', email: 'other@example.com' });
    await grantSubscription(other.userId);

    expect((await getAsset(blinded(1), other.accessToken)).status).toBe(404);
  });

  it('refuses an upload that would exceed the quota', async () => {
    const { account, token } = await subscriber();
    await env.DB.prepare('UPDATE users SET quota_bytes = 4096 WHERE id = ?1').bind(account.userId).run();

    expect((await putAsset(blinded(1), new Uint8Array(3000), token)).status).toBe(200);
    expect((await putAsset(blinded(2), new Uint8Array(3000), token)).status).toBe(413);
  });

  it('lets the same key be re-uploaded without counting twice', async () => {
    const { account, token } = await subscriber();
    await env.DB.prepare('UPDATE users SET quota_bytes = 4096 WHERE id = ?1').bind(account.userId).run();

    expect((await putAsset(blinded(1), new Uint8Array(3000), token)).status).toBe(200);
    // A retry after a dropped connection is the reason this must not fail.
    expect((await putAsset(blinded(1), new Uint8Array(3000), token)).status).toBe(200);
  });
});

describe('reference counting', () => {
  it('reclaims the object once the last file pointing at it is deleted', async () => {
    const { account, token } = await subscriber();
    await putAsset(blinded(1), new Uint8Array([1, 2, 3]), token);
    await post('/v1/files/push', { files: [fileEntry(1, 1), fileEntry(2, 1)] }, { token });

    expect(await assetRow(account.userId, blinded(1))).toMatchObject({ ref_count: 2 });

    await post('/v1/files/push', { tombstones: [{ id: fileId(1), deleted_utc: stamp(30) }] }, { token });
    // One file still points at it, so the bytes stay.
    expect(await assetRow(account.userId, blinded(1))).toMatchObject({ ref_count: 1 });
    expect(await env.ASSET_BUCKET!.get(`${account.userId}/${blinded(1)}`)).not.toBeNull();

    await post('/v1/files/push', { tombstones: [{ id: fileId(2), deleted_utc: stamp(31) }] }, { token });
    expect(await assetRow(account.userId, blinded(1))).toBeNull();
    expect(await env.ASSET_BUCKET!.get(`${account.userId}/${blinded(1)}`)).toBeNull();
  });

  it('releases the old asset when a file is repointed at a new one', async () => {
    const { account, token } = await subscriber();
    await putAsset(blinded(1), new Uint8Array([1]), token);
    await putAsset(blinded(2), new Uint8Array([2]), token);
    await post('/v1/files/push', { files: [fileEntry(1, 1)] }, { token });

    await post('/v1/files/push', { files: [fileEntry(1, 2, { at: 20 })] }, { token });

    expect(await assetRow(account.userId, blinded(1))).toBeNull();
    expect(await assetRow(account.userId, blinded(2))).toMatchObject({ ref_count: 1 });
  });

  it('does not double-count a file pushed twice at the same asset', async () => {
    const { account, token } = await subscriber();
    await post('/v1/files/push', { files: [fileEntry(1, 1)] }, { token });
    await post('/v1/files/push', { files: [fileEntry(1, 1, { at: 20 })] }, { token });

    expect(await assetRow(account.userId, blinded(1))).toMatchObject({ ref_count: 1 });
  });
});

describe('the shared cursor', () => {
  it('pages notes and files together in sequence order', async () => {
    const { token } = await subscriber();

    await post('/v1/sync/push', { notes: [{ id: fileId(90), payload: envelope('n'), updated_utc: stamp(0) }] }, { token });
    await post('/v1/files/push', { files: [fileEntry(1, 1)] }, { token });
    await post('/v1/sync/push', { notes: [{ id: fileId(91), payload: envelope('n'), updated_utc: stamp(1) }] }, { token });

    const pulled = await get('/v1/sync/pull?since=0', { token });

    // Interleaved, and in the order they were written: one cursor over one log. A separate file
    // pull would let one of these two step the other's rows past the cursor unseen.
    expect(pulled.body.changes.map((c: { entity: string }) => c.entity)).toEqual(['note', 'file', 'note']);
    expect(pulled.body.cursor).toBe(pulled.body.changes[2].seq);
  });

  it('resumes from a cursor that fell in the middle of the two kinds', async () => {
    const { token } = await subscriber();
    await post('/v1/files/push', { files: [fileEntry(1, 1)] }, { token });

    const first = await get('/v1/sync/pull?since=0&limit=1', { token });
    await post('/v1/sync/push', { notes: [{ id: fileId(90), payload: envelope('n'), updated_utc: stamp(0) }] }, { token });

    const second = await get(`/v1/sync/pull?since=${first.body.cursor}`, { token });
    expect(second.body.changes).toHaveLength(1);
    expect(second.body.changes[0].entity).toBe('note');
  });

  it('leaves a note pull unaffected when the account has no files', async () => {
    const { token } = await subscriber();
    await post('/v1/sync/push', { notes: [{ id: fileId(90), payload: envelope('n'), updated_utc: stamp(0) }] }, { token });

    const pulled = await get('/v1/sync/pull?since=0', { token });
    expect(pulled.body.changes).toHaveLength(1);
    expect(pulled.body.changes[0].entity).toBe('note');
    expect(pulled.body.changes[0].blinded_key).toBeUndefined();
  });
});

describe('routing', () => {
  it('does not answer an asset path that is not a blinded key', async () => {
    const { token } = await subscriber();
    expect((await get('/v1/assets/short', { token })).status).toBe(404);
    expect((await get('/v1/assets/', { token })).status).toBe(404);
  });

  it('needs authentication', async () => {
    expect((await post('/v1/files/push', { files: [] }, {})).status).toBe(401);
  });
});
