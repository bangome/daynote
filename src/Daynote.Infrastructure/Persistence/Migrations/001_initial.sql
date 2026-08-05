CREATE TABLE schema_versions (
    version INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    applied_utc TEXT NOT NULL
);

CREATE TABLE notes (
    rowid INTEGER PRIMARY KEY,
    id TEXT NOT NULL UNIQUE,
    local_date TEXT NOT NULL,
    title TEXT NOT NULL,
    body TEXT NOT NULL,
    sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
    revision INTEGER NOT NULL CHECK (revision >= 0),
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE (local_date, sort_order)
);

CREATE TABLE image_assets (
    hash TEXT PRIMARY KEY,
    relative_path TEXT NOT NULL UNIQUE,
    width INTEGER NOT NULL CHECK (width > 0),
    height INTEGER NOT NULL CHECK (height > 0),
    png_byte_length INTEGER NOT NULL CHECK (png_byte_length > 0),
    created_utc TEXT NOT NULL
);

CREATE TABLE clipboard_items (
    rowid INTEGER PRIMARY KEY,
    id TEXT NOT NULL UNIQUE,
    local_date TEXT NOT NULL,
    captured_utc TEXT NOT NULL,
    sequence_number INTEGER NOT NULL CHECK (sequence_number >= 0),
    kind TEXT NOT NULL CHECK (kind IN ('text', 'image')),
    text_value TEXT,
    asset_hash TEXT,
    payload_hash TEXT NOT NULL,
    byte_length INTEGER NOT NULL CHECK (byte_length >= 0),
    FOREIGN KEY (asset_hash) REFERENCES image_assets(hash)
);

CREATE TABLE settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE search_documents (
    rowid INTEGER PRIMARY KEY,
    source_type TEXT NOT NULL,
    source_id TEXT NOT NULL,
    local_date TEXT NOT NULL,
    title TEXT NOT NULL,
    body TEXT NOT NULL,
    title_folded TEXT NOT NULL,
    body_folded TEXT NOT NULL,
    UNIQUE (source_type, source_id)
);

CREATE VIRTUAL TABLE search_fts USING fts5(
    title,
    body,
    title_folded,
    body_folded,
    content='search_documents',
    content_rowid='rowid',
    tokenize='trigram case_sensitive 0'
);

CREATE TRIGGER search_documents_ai AFTER INSERT ON search_documents BEGIN
    INSERT INTO search_fts(rowid, title, body, title_folded, body_folded)
    VALUES (new.rowid, new.title, new.body, new.title_folded, new.body_folded);
END;

CREATE TRIGGER search_documents_ad AFTER DELETE ON search_documents BEGIN
    INSERT INTO search_fts(search_fts, rowid, title, body, title_folded, body_folded)
    VALUES ('delete', old.rowid, old.title, old.body, old.title_folded, old.body_folded);
END;

CREATE TRIGGER search_documents_au AFTER UPDATE ON search_documents BEGIN
    INSERT INTO search_fts(search_fts, rowid, title, body, title_folded, body_folded)
    VALUES ('delete', old.rowid, old.title, old.body, old.title_folded, old.body_folded);
    INSERT INTO search_fts(rowid, title, body, title_folded, body_folded)
    VALUES (new.rowid, new.title, new.body, new.title_folded, new.body_folded);
END;
