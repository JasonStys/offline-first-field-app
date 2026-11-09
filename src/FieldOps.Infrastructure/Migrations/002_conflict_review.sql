-- File: 002_conflict_review.sql
-- Purpose: Add optional original mutation baselines without guessing evidence for existing drafts.
CREATE TABLE IF NOT EXISTS mutation_baselines (
    mutation_id TEXT PRIMARY KEY,
    baseline_json TEXT NULL,
    FOREIGN KEY(mutation_id) REFERENCES outbox_commands(mutation_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_outbox_record_state ON outbox_commands(record_id, state);

INSERT OR IGNORE INTO schema_history(version, applied_at_utc)
VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
