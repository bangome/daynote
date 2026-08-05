ALTER TABLE notes ADD COLUMN is_favorite INTEGER NOT NULL DEFAULT 0 CHECK (is_favorite IN (0, 1));

CREATE TABLE note_tags (
    note_id TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    tag TEXT NOT NULL,
    sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
    UNIQUE (note_id, tag),
    UNIQUE (note_id, sort_order)
);

CREATE TABLE file_assets (
    hash TEXT PRIMARY KEY,
    relative_path TEXT NOT NULL UNIQUE,
    byte_length INTEGER NOT NULL CHECK (byte_length >= 0),
    created_utc TEXT NOT NULL
);

CREATE TABLE day_files (
    rowid INTEGER PRIMARY KEY,
    id TEXT NOT NULL UNIQUE,
    local_date TEXT NOT NULL,
    display_name TEXT NOT NULL,
    byte_length INTEGER NOT NULL CHECK (byte_length >= 0),
    asset_hash TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (asset_hash) REFERENCES file_assets(hash)
);

DELETE FROM search_documents;

INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded)
SELECT 'note',n.id,n.local_date,daynote_nfc(n.title),daynote_nfc(n.body),daynote_fold(n.title),
    daynote_fold(n.body || COALESCE((SELECT char(10) || group_concat(t.tag, ' ') FROM note_tags t WHERE t.note_id=n.id), ''))
FROM notes n;

INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded)
SELECT 'clipboard',id,local_date,'Clipboard',daynote_nfc(text_value),'CLIPBOARD',daynote_fold(text_value)
FROM clipboard_items
WHERE kind='text' AND text_value IS NOT NULL;

INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded)
SELECT 'file',id,local_date,daynote_nfc(display_name),'',daynote_fold(display_name),''
FROM day_files;

INSERT INTO search_fts(search_fts) VALUES('rebuild');
INSERT INTO search_fts(search_fts,rank) VALUES('integrity-check',1);
