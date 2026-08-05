DELETE FROM search_documents;

INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded)
SELECT 'note',id,local_date,daynote_nfc(title),daynote_nfc(body),daynote_fold(title),daynote_fold(body)
FROM notes;

INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded)
SELECT 'clipboard',id,local_date,'Clipboard',daynote_nfc(text_value),'CLIPBOARD',daynote_fold(text_value)
FROM clipboard_items
WHERE kind='text' AND text_value IS NOT NULL;

INSERT INTO search_fts(search_fts) VALUES('rebuild');
INSERT INTO search_fts(search_fts,rank) VALUES('integrity-check',1);
