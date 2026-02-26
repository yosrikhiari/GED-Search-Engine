CREATE TABLE IF NOT EXISTS documents (
    id                  UUID PRIMARY KEY,
    title               VARCHAR(500) NOT NULL,
    description         VARCHAR(2000),
    file_name           TEXT NOT NULL,
    file_path           TEXT NOT NULL,
    content_type        TEXT NOT NULL,
    file_size           BIGINT NOT NULL,
    file_hash           TEXT,
    created_at          TIMESTAMP NOT NULL,
    document_date       TIMESTAMP,
    modified_at         TIMESTAMP,
    created_by          TEXT,
    modified_by         TEXT,
    status              TEXT NOT NULL DEFAULT 'Active',
    is_ocr_processed    BOOLEAN NOT NULL DEFAULT false,
    ocr_text            TEXT,
    extracted_text      TEXT,
    tags                TEXT[],
    category            TEXT,
    metadata            TEXT,
    version             INT NOT NULL DEFAULT 1,
    parent_document_id  UUID
);
CREATE TABLE IF NOT EXISTS document_metadata (
    id          UUID PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    key         VARCHAR(200) NOT NULL,
    value       TEXT,
    type        TEXT NOT NULL DEFAULT 'String',
    created_at  TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_documents_created_at    ON documents(created_at);
CREATE INDEX IF NOT EXISTS ix_documents_document_date ON documents(document_date);
CREATE INDEX IF NOT EXISTS ix_documents_category      ON documents(category);
CREATE INDEX IF NOT EXISTS ix_documents_status        ON documents(status);
CREATE INDEX IF NOT EXISTS ix_document_metadata_document_id ON document_metadata(document_id);
