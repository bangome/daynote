import { beforeEach, describe, expect, it } from 'vitest';
import {
  env,
  get,
  post,
  resetDatabase,
  signIn,
  signInAgain,
  toBase64Url,
  type Account,
} from './helpers';

/**
 * The server never sees plaintext, so these tests deliberately push nonsense-shaped ciphertext: what
 * matters is that the Worker orders, stores, and returns blobs without inspecting them.
 */

beforeEach(resetDatabase);

function envelope(marker = 'x'): string {
  const nonce = toBase64Url(crypto.getRandomValues(new Uint8Array(12)));
  const body = toBase64Url(new TextEncoder().encode(marker.padEnd(32, '.')));
  return `v1.${nonce}.${body}`;
}

function stamp(minute: number): string {
  return `2026-08-20T09:${String(minute).padStart(2, '0')}:00.0000000Z`;
}

function noteId(suffix: number): string {
  return `00000000-0000-4000-8000-${String(suffix).padStart(12, '0')}`;
}

async function signedIn(): Promise<{ account: Account; token: string }> {
  const account = await signIn();
  return { account, token: account.accessToken };
}

describe('push', () => {
  it('accepts a new note and advances the cursor', async () => {
    const { token } = await signedIn();

    const response = await post(
      '/v1/sync/push',
      { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(0) }] },
      { token },
    );

    expect(response.status).toBe(200);
    expect(response.body.accepted_notes).toEqual([noteId(1)]);
    expect(response.body.rejected_notes).toEqual([]);
    expect(response.body.cursor).toBeGreaterThan(0);
  });

  it('rejects a push older than what is stored', async () => {
    const { token } = await signedIn();
    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload: envelope('new'), updated_utc: stamp(10) }] }, { token });

    const stale = await post(
      '/v1/sync/push',
      { notes: [{ id: noteId(1), payload: envelope('old'), updated_utc: stamp(5) }] },
      { token },
    );

    // An offline device reconnecting must not clobber newer content.
    expect(stale.body.accepted_notes).toEqual([]);
    expect(stale.body.rejected_notes).toEqual([noteId(1)]);
  });

  it('rejects a push with the same timestamp rather than re-storing it', async () => {
    const { token } = await signedIn();
    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(10) }] }, { token });

    const repeat = await post(
      '/v1/sync/push',
      { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(10) }] },
      { token },
    );

    // Accepting it would append a change_log row and echo to every device for no reason.
    expect(repeat.body.rejected_notes).toEqual([noteId(1)]);
  });

  it('stores a tombstone that outranks an older edit', async () => {
    const { token } = await signedIn();
    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(5) }] }, { token });

    const deleted = await post(
      '/v1/sync/push',
      { tombstones: [{ id: noteId(1), deleted_utc: stamp(10) }] },
      { token },
    );

    expect(deleted.body.accepted_tombstones).toEqual([noteId(1)]);
    const pulled = await get('/v1/sync/pull?since=0', { token });
    expect(pulled.body.changes).toHaveLength(1);
    expect(pulled.body.changes[0].payload).toBeNull();
    expect(pulled.body.changes[0].deleted_utc).toBe(stamp(10));
  });

  it('rejects a tombstone older than the stored edit', async () => {
    const { token } = await signedIn();
    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(10) }] }, { token });

    const late = await post(
      '/v1/sync/push',
      { tombstones: [{ id: noteId(1), deleted_utc: stamp(5) }] },
      { token },
    );

    expect(late.body.rejected_tombstones).toEqual([noteId(1)]);
  });

  it('lets a newer edit resurrect a deleted note', async () => {
    const { token } = await signedIn();
    await post('/v1/sync/push', { tombstones: [{ id: noteId(1), deleted_utc: stamp(5) }] }, { token });

    const revived = await post(
      '/v1/sync/push',
      { notes: [{ id: noteId(1), payload: envelope('back'), updated_utc: stamp(10) }] },
      { token },
    );

    expect(revived.body.accepted_notes).toEqual([noteId(1)]);
    const pulled = await get('/v1/sync/pull?since=0', { token });
    expect(pulled.body.changes[0].payload).not.toBeNull();
    expect(pulled.body.changes[0].deleted_utc).toBeNull();
  });

  it('keeps accounts apart', async () => {
    const alice = await signedIn();
    const bob = await signedIn();

    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload: envelope('alice'), updated_utc: stamp(0) }] }, { token: alice.token });

    const bobsView = await get('/v1/sync/pull?since=0', { token: bob.token });
    expect(bobsView.body.changes).toEqual([]);
  });

  it('requires a token', async () => {
    const response = await post('/v1/sync/push', { notes: [] });
    expect(response.status).toBe(401);
  });

  it.each([
    ['a non-uuid id', { id: 'note-1' }],
    ['a payload that is not an envelope', { payload: 'just text' }],
    ['a payload with the wrong nonce length', { payload: 'v1.AAAA.AAAAAAAAAAAAAAAAAAAAAAAA' }],
    ['a non-canonical timestamp', { updated_utc: '2026-08-20T09:00:00Z' }],
    ['a .NET round-trip timestamp', { updated_utc: '2026-08-20T09:00:00.0000000+00:00' }],
  ])('rejects %s', async (_label, override) => {
    const { token } = await signedIn();

    const response = await post(
      '/v1/sync/push',
      { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(0), ...override }] },
      { token },
    );

    expect(response.status).toBe(400);
  });

  it('refuses a file tombstone rather than reporting a delete that never happened', async () => {
    const { token } = await signedIn();

    const response = await post(
      '/v1/sync/push',
      { tombstones: [{ entity: 'file', id: noteId(1), deleted_utc: stamp(0) }] },
      { token },
    );

    expect(response.status).toBe(400);
  });

  it('refuses an oversized batch', async () => {
    const { token } = await signedIn();
    const notes = Array.from({ length: 501 }, (_, index) => ({
      id: noteId(index + 1),
      payload: envelope(),
      updated_utc: stamp(0),
    }));

    expect((await post('/v1/sync/push', { notes }, { token })).status).toBe(400);
  });
});

describe('pull', () => {
  it('returns nothing for a fresh account', async () => {
    const { token } = await signedIn();

    const response = await get('/v1/sync/pull?since=0', { token });

    expect(response.body.changes).toEqual([]);
    expect(response.body.cursor).toBe(0);
    expect(response.body.has_more).toBe(false);
  });

  it('returns what another device pushed, with the payload untouched', async () => {
    const account = await signIn({ device: 'Desktop' });
    const deskToken = account.accessToken;
    const laptopToken = (await signInAgain(account, 'Laptop')).body.access_token;
    const payload = envelope('from the desktop');

    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload, updated_utc: stamp(0) }] }, { token: deskToken });

    const response = await get('/v1/sync/pull?since=0', { token: laptopToken });
    expect(response.body.changes).toHaveLength(1);
    expect(response.body.changes[0].payload).toBe(payload);
    expect(response.body.changes[0].updated_utc).toBe(stamp(0));
  });

  it('only returns changes after the cursor', async () => {
    const { token } = await signedIn();
    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(0) }] }, { token });
    const first = await get('/v1/sync/pull?since=0', { token });

    await post('/v1/sync/push', { notes: [{ id: noteId(2), payload: envelope(), updated_utc: stamp(1) }] }, { token });

    const second = await get(`/v1/sync/pull?since=${first.body.cursor}`, { token });
    expect(second.body.changes.map((change: { id: string }) => change.id)).toEqual([noteId(2)]);
  });

  it('collapses repeated edits of one note into a single change', async () => {
    const { token } = await signedIn();
    for (let minute = 0; minute < 5; minute += 1) {
      await post(
        '/v1/sync/push',
        { notes: [{ id: noteId(1), payload: envelope(`v${minute}`), updated_utc: stamp(minute) }] },
        { token },
      );
    }

    const response = await get('/v1/sync/pull?since=0', { token });

    // Five writes, one row: a note edited all afternoon costs one entry in the page.
    expect(response.body.changes).toHaveLength(1);
    expect(response.body.changes[0].updated_utc).toBe(stamp(4));
  });

  it('pages, and the cursor from a full page picks up exactly where it left off', async () => {
    const { token } = await signedIn();
    for (let index = 1; index <= 5; index += 1) {
      await post(
        '/v1/sync/push',
        { notes: [{ id: noteId(index), payload: envelope(), updated_utc: stamp(index) }] },
        { token },
      );
    }

    const first = await get('/v1/sync/pull?since=0&limit=2', { token });
    expect(first.body.changes).toHaveLength(2);
    expect(first.body.has_more).toBe(true);

    const second = await get(`/v1/sync/pull?since=${first.body.cursor}&limit=2`, { token });
    const third = await get(`/v1/sync/pull?since=${second.body.cursor}&limit=2`, { token });

    const seen = [...first.body.changes, ...second.body.changes, ...third.body.changes].map(
      (change: { id: string }) => change.id,
    );
    expect(seen).toEqual([noteId(1), noteId(2), noteId(3), noteId(4), noteId(5)]);
    expect(new Set(seen).size).toBe(5);
  });

  it('holds the cursor still on an empty page', async () => {
    const { token } = await signedIn();
    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(0) }] }, { token });
    const drained = await get('/v1/sync/pull?since=0', { token });

    const empty = await get(`/v1/sync/pull?since=${drained.body.cursor}`, { token });

    // Jumping to the global maximum here would skip whatever a concurrent push is mid-write.
    expect(empty.body.changes).toEqual([]);
    expect(empty.body.cursor).toBe(drained.body.cursor);
  });

  it('rejects a negative cursor', async () => {
    const { token } = await signedIn();
    expect((await get('/v1/sync/pull?since=-1', { token })).status).toBe(400);
  });

  it('requires a token', async () => {
    expect((await get('/v1/sync/pull?since=0')).status).toBe(401);
  });
});

describe('what the server can see', () => {
  it('stores only the blob, the clock, and the id', async () => {
    const { token } = await signedIn();
    await post('/v1/sync/push', { notes: [{ id: noteId(1), payload: envelope(), updated_utc: stamp(0) }] }, { token });

    const row = await env.DB.prepare('SELECT * FROM notes LIMIT 1').first<Record<string, unknown>>();

    // A regression here means someone added a column that could hold readable content. The date, the
    // title, the tags, and the favourite flag all live inside the encrypted payload on purpose.
    expect(Object.keys(row!)).toEqual(['user_id', 'id', 'payload', 'updated_utc', 'deleted_utc']);
  });

  it('never logs or returns anything derived from the payload', async () => {
    const { token } = await signedIn();
    const payload = envelope('secret');

    const pushed = await post(
      '/v1/sync/push',
      { notes: [{ id: noteId(1), payload, updated_utc: stamp(0) }] },
      { token },
    );

    // The push response carries ids and counts, never content.
    expect(JSON.stringify(pushed.body)).not.toContain(payload);
  });
});
