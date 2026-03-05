-- Migration: Add Outbox Pattern table
-- Run this against your PostgreSQL database before deploying the new code.
--
-- Purpose: Enables reliable OCR job dispatch via the Outbox Pattern.
-- Documents and their OCR jobs are now written atomically. The OutboxRelayService
-- picks up and publishes jobs to RabbitMQ, guaranteeing at-least-once delivery.

-- ─── Create outbox_messages table ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS outbox_messages (
    id            UUID         NOT NULL DEFAULT gen_random_uuid(),
    type          VARCHAR(100) NOT NULL,           -- message type, e.g. 'OcrJob'
    payload       TEXT         NOT NULL,           -- JSON payload for RabbitMQ
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    processed_at  TIMESTAMPTZ  NULL,               -- NULL = not yet published
    error         TEXT         NULL,               -- last publish error (if any)
    retry_count   INTEGER      NOT NULL DEFAULT 0, -- publish attempts so far

    CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
);

-- ─── Index for relay worker query ─────────────────────────────────────────────
-- The relay polls: WHERE processed_at IS NULL AND retry_count < 5
-- ORDER BY created_at (FIFO)
CREATE INDEX IF NOT EXISTS ix_outbox_unprocessed
    ON outbox_messages (created_at)
    WHERE processed_at IS NULL AND retry_count < 5;

-- ─── Optional: cleanup old processed messages ─────────────────────────────────
-- Run this periodically (e.g. weekly) to keep the table small.
-- DELETE FROM outbox_messages WHERE processed_at < now() - INTERVAL '30 days';

COMMENT ON TABLE outbox_messages IS
    'Outbox Pattern: OCR and other async jobs written here atomically with the '
    'source document. OutboxRelayService publishes them to RabbitMQ.';