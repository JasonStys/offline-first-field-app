-- File: 001_initial.sql
-- Purpose: Create the transactional local cache, bounded outbox, conflicts, and sync cursor.
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS schema_history (
    version INTEGER PRIMARY KEY,
    applied_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS inspections (
    id TEXT PRIMARY KEY,
    template_id TEXT NOT NULL CHECK(length(template_id) BETWEEN 1 AND 64),
    title TEXT NOT NULL CHECK(length(title) BETWEEN 1 AND 120),
    notes TEXT NOT NULL CHECK(length(notes) <= 4000),
    status INTEGER NOT NULL CHECK(status BETWEEN 0 AND 2),
    version INTEGER NOT NULL CHECK(version >= 0),
    updated_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_inspections_title
    ON inspections(title);

CREATE TABLE IF NOT EXISTS outbox_commands (
    sequence INTEGER PRIMARY KEY AUTOINCREMENT,
    mutation_id TEXT NOT NULL UNIQUE,
    record_id TEXT NOT NULL,
    device_id TEXT NOT NULL CHECK(length(device_id) BETWEEN 1 AND 64),
    expected_version INTEGER NOT NULL CHECK(expected_version >= 0),
    record_json TEXT NOT NULL,
    state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 3),
    attempts INTEGER NOT NULL DEFAULT 0 CHECK(attempts >= 0),
    last_error TEXT NULL,
    FOREIGN KEY(record_id) REFERENCES inspections(id)
);

CREATE INDEX IF NOT EXISTS ix_outbox_state_sequence
    ON outbox_commands(state, sequence);

CREATE TABLE IF NOT EXISTS queue_guard (
    singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
    total_count INTEGER NOT NULL CHECK(total_count BETWEEN 0 AND 10000)
);

INSERT OR IGNORE INTO queue_guard(singleton_id, total_count)
VALUES (1, (SELECT count(*) FROM outbox_commands));

CREATE TRIGGER IF NOT EXISTS tr_outbox_reject_at_capacity
BEFORE INSERT ON outbox_commands
WHEN (SELECT total_count FROM queue_guard WHERE singleton_id = 1) >= 10000
BEGIN
    SELECT RAISE(ABORT, 'outbox capacity reached');
END;

CREATE TRIGGER IF NOT EXISTS tr_outbox_track_insert
AFTER INSERT ON outbox_commands
BEGIN
    UPDATE queue_guard SET total_count = total_count + 1 WHERE singleton_id = 1;
END;

CREATE TRIGGER IF NOT EXISTS tr_outbox_track_delete
AFTER DELETE ON outbox_commands
BEGIN
    UPDATE queue_guard SET total_count = total_count - 1 WHERE singleton_id = 1;
END;

CREATE TABLE IF NOT EXISTS conflicts (
    mutation_id TEXT PRIMARY KEY,
    local_json TEXT NOT NULL,
    remote_json TEXT NOT NULL,
    detected_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sync_checkpoint (
    singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
    cursor INTEGER NOT NULL CHECK(cursor >= 0)
);

INSERT OR IGNORE INTO sync_checkpoint(singleton_id, cursor) VALUES (1, 0);
INSERT OR IGNORE INTO schema_history(version, applied_at_utc)
VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
