-- =============================================================================
-- GED Database — Full Schema Bootstrap
-- Usage: docker exec -i ged-postgres psql -U ged_user -d ged_db < init_schema.sql
-- =============================================================================

-- ─── documents ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS documents (
    id                  UUID PRIMARY KEY,
    title               VARCHAR(500)  NOT NULL,
    description         VARCHAR(2000),
    file_name           TEXT          NOT NULL,
    file_path           TEXT          NOT NULL,
    content_type        TEXT          NOT NULL,
    file_size           BIGINT        NOT NULL,
    file_hash           TEXT,
    created_at          TIMESTAMP     NOT NULL,
    document_date       TIMESTAMP,
    modified_at         TIMESTAMP,
    created_by          TEXT,
    modified_by         TEXT,
    status              TEXT          NOT NULL DEFAULT 'Active',
    is_ocr_processed    BOOLEAN       NOT NULL DEFAULT false,
    ocr_text            TEXT,
    extracted_text      TEXT,
    tags                TEXT[],
    category            TEXT,
    metadata            JSONB,
    version             INT           NOT NULL DEFAULT 1,
    parent_document_id  UUID
);

CREATE INDEX IF NOT EXISTS ix_documents_created_at    ON documents (created_at);
CREATE INDEX IF NOT EXISTS ix_documents_document_date ON documents (document_date);
CREATE INDEX IF NOT EXISTS ix_documents_category      ON documents (category);
CREATE INDEX IF NOT EXISTS ix_documents_status        ON documents (status);

-- ─── document_metadata ────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS document_metadata (
    id          UUID PRIMARY KEY,
    document_id UUID         NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
    key         VARCHAR(200) NOT NULL,
    value       TEXT,
    type        TEXT         NOT NULL DEFAULT 'String',
    created_at  TIMESTAMP    NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_document_metadata_document_id ON document_metadata (document_id);

-- ─── document_acls ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS document_acls (
    id          UUID      NOT NULL,
    document_id UUID      NOT NULL,
    user_id     UUID      NOT NULL,
    permission  INTEGER   NOT NULL,
    granted_at  TIMESTAMP NOT NULL,
    granted_by  UUID      NOT NULL,
    expires_at  TIMESTAMP,
    CONSTRAINT "PK_document_acls" PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_acl_doc_user ON document_acls (document_id, user_id);

-- ─── outbox_messages ──────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS outbox_messages (
    id           UUID         NOT NULL DEFAULT gen_random_uuid(),
    type         VARCHAR(100) NOT NULL,
    payload      TEXT         NOT NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    processed_at TIMESTAMPTZ  NULL,
    error        TEXT         NULL,
    retry_count  INTEGER      NOT NULL DEFAULT 0,
    CONSTRAINT pk_outbox_messages PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_outbox_unprocessed
    ON outbox_messages (created_at)
    WHERE processed_at IS NULL AND retry_count < 5;

COMMENT ON TABLE outbox_messages IS
    'Outbox Pattern: OCR and other async jobs written here atomically with the '
    'source document. OutboxRelayService publishes them to RabbitMQ.';

-- ─── EF Migrations history ────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL,
    "ProductVersion" VARCHAR(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20240101000000_InitialCreate',              '8.0.0'),
    ('20260307092924_AddDocumentAclExpiresAt',    '8.0.0')
ON CONFLICT DO NOTHING;