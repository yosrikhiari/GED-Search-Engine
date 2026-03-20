-- ============================================================================
-- GED Elise — SQL Server schema
-- ============================================================================

-- Drop existing tables
IF OBJECT_ID('dbo.document_acls', 'U') IS NOT NULL
    DROP TABLE dbo.document_acls;

IF OBJECT_ID('dbo.user_group_assignments', 'U') IS NOT NULL
    DROP TABLE dbo.user_group_assignments;

IF OBJECT_ID('dbo.document_group_members', 'U') IS NOT NULL
    DROP TABLE dbo.document_group_members;

GO

-- ─── documents ───────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.documents', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.documents (
        id                  UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        title               NVARCHAR(500)     NOT NULL,
        description         NVARCHAR(2000),
        file_name           NVARCHAR(500)     NOT NULL,
        file_path           NVARCHAR(1000)    NOT NULL,
        content_type        NVARCHAR(200)     NOT NULL,
        file_size           BIGINT            NOT NULL DEFAULT 0,
        file_hash           NVARCHAR(200),
        created_at          DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        document_date       DATETIME2,
        modified_at         DATETIME2,
        created_by          NVARCHAR(200),
        modified_by         NVARCHAR(200),
        -- stored as string enum (e.g. 'Active', 'Deleted', 'Processing')
        status              NVARCHAR(50)      NOT NULL DEFAULT 'Active',
        is_ocr_processed    BIT               NOT NULL DEFAULT 0,
        ocr_text            NVARCHAR(MAX),
        extracted_text      NVARCHAR(MAX),
        tags                NVARCHAR(MAX),
        category            NVARCHAR(200),
        metadata            NVARCHAR(MAX),
        version             INT               NOT NULL DEFAULT 1,
        parent_document_id  UNIQUEIDENTIFIER
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_documents_created_at'    AND object_id = OBJECT_ID('dbo.documents'))
    CREATE INDEX ix_documents_created_at    ON dbo.documents (created_at);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_documents_document_date' AND object_id = OBJECT_ID('dbo.documents'))
    CREATE INDEX ix_documents_document_date ON dbo.documents (document_date);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_documents_category'      AND object_id = OBJECT_ID('dbo.documents'))
    CREATE INDEX ix_documents_category      ON dbo.documents (category);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_documents_status'        AND object_id = OBJECT_ID('dbo.documents'))
    CREATE INDEX ix_documents_status        ON dbo.documents (status);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_documents_content_type'  AND object_id = OBJECT_ID('dbo.documents'))
    CREATE INDEX ix_documents_content_type  ON dbo.documents (content_type);


-- ─── document_acls ───────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.document_acls', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.document_acls (
        id           UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        document_id  UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.documents (id) ON DELETE CASCADE,
        user_id      UNIQUEIDENTIFIER  NOT NULL,
        permission   INT               NOT NULL DEFAULT 0,
        granted_at   DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        granted_by   UNIQUEIDENTIFIER  NOT NULL,
        expires_at   DATETIME2
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_acl_doc_user' AND object_id = OBJECT_ID('dbo.document_acls'))
    CREATE INDEX ix_acl_doc_user ON dbo.document_acls (document_id, user_id);

-- ─── users ───────────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.users', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.users (
        id                  UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        username            NVARCHAR(100)     NOT NULL,
        password_hash       NVARCHAR(500)     NOT NULL,
        full_name           NVARCHAR(200),
        email               NVARCHAR(200),
        role                NVARCHAR(50)      NOT NULL DEFAULT 'User',
        is_active           BIT               NOT NULL DEFAULT 1,
        created_at          DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        last_login_at       DATETIME2,
        allowed_categories  NVARCHAR(MAX)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_users_username' AND object_id = OBJECT_ID('dbo.users'))
    CREATE UNIQUE INDEX ix_users_username ON dbo.users (username);

-- ─── document_metadata ───────────────────────────────────────────────────────
IF OBJECT_ID('dbo.document_metadata', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.document_metadata (
        id           UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        document_id  UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.documents (id) ON DELETE CASCADE,
        [key]        NVARCHAR(200)     NOT NULL,
        value        NVARCHAR(MAX),
        type         NVARCHAR(50)      NOT NULL DEFAULT 'Text',
        created_at   DATETIME2         NOT NULL DEFAULT GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_document_metadata_document_id' AND object_id = OBJECT_ID('dbo.document_metadata'))
    CREATE INDEX ix_document_metadata_document_id ON dbo.document_metadata (document_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_document_metadata_doc_key' AND object_id = OBJECT_ID('dbo.document_metadata'))
    CREATE INDEX ix_document_metadata_doc_key ON dbo.document_metadata (document_id, [key]);


-- ─── outbox_messages ─────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.outbox_messages', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.outbox_messages (
        id            UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        type          NVARCHAR(100)     NOT NULL,
        payload       NVARCHAR(MAX)     NOT NULL,
        created_at    DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        processed_at  DATETIME2,
        error         NVARCHAR(MAX),
        retry_count   INT               NOT NULL DEFAULT 0
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_outbox_unprocessed' AND object_id = OBJECT_ID('dbo.outbox_messages'))
    CREATE INDEX ix_outbox_unprocessed ON dbo.outbox_messages (processed_at);


-- ─── document_groups ─────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.document_groups', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.document_groups (
        id           UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        name         NVARCHAR(200)     NOT NULL,
        description  NVARCHAR(1000),
        color        NVARCHAR(20),
        icon         NVARCHAR(10),
        category     NVARCHAR(100),
        created_by   UNIQUEIDENTIFIER  NOT NULL,
        created_at   DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        updated_at   DATETIME2,
        is_active    BIT               NOT NULL DEFAULT 1
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_document_groups_active' AND object_id = OBJECT_ID('dbo.document_groups'))
    CREATE INDEX ix_document_groups_active ON dbo.document_groups (is_active);


-- ─── document_group_members ───────────────────────────────────────────────────
IF OBJECT_ID('dbo.document_group_members', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.document_group_members (
        id                  UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        group_id            UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.document_groups (id) ON DELETE CASCADE,
        document_id         UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.documents (id)       ON DELETE NO ACTION,
        default_permission  INT               NOT NULL DEFAULT 0,
        added_at            DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        added_by            UNIQUEIDENTIFIER  NOT NULL
    );
END

-- SQL Server limitation: cannot have multiple cascading paths to the same table
-- document_id uses NO ACTION and is handled by application logic
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_group_members_group_doc' AND object_id = OBJECT_ID('dbo.document_group_members'))
    CREATE UNIQUE INDEX ix_group_members_group_doc ON dbo.document_group_members (group_id, document_id);


-- ─── user_group_assignments ───────────────────────────────────────────────────
IF OBJECT_ID('dbo.user_group_assignments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.user_group_assignments (
        id           UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        group_id     UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.document_groups (id) ON DELETE CASCADE,
        user_id      UNIQUEIDENTIFIER  NOT NULL,
        permission   INT               NOT NULL DEFAULT 0,
        assigned_at  DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        assigned_by  UNIQUEIDENTIFIER  NOT NULL,
        expires_at   DATETIME2
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_user_group_assignments_group_user' AND object_id = OBJECT_ID('dbo.user_group_assignments'))
    CREATE UNIQUE INDEX ix_user_group_assignments_group_user ON dbo.user_group_assignments (group_id, user_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_user_group_assignments_user' AND object_id = OBJECT_ID('dbo.user_group_assignments'))
    CREATE INDEX ix_user_group_assignments_user ON dbo.user_group_assignments (user_id);


-- ─── document_versions ──────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.document_versions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.document_versions (
        id             UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        document_id    UNIQUEIDENTIFIER  NOT NULL,
        version_number INT               NOT NULL DEFAULT 1,
        title          NVARCHAR(500),
        description    NVARCHAR(2000),
        file_name      NVARCHAR(500),
        file_size      BIGINT,
        content_type   NVARCHAR(200),
        category       NVARCHAR(200),
        tags           NVARCHAR(MAX),
        metadata       NVARCHAR(MAX),
        changed_by     NVARCHAR(500),
        change_reason  NVARCHAR(1000),
        created_at     DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        file_path      NVARCHAR(1000)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_versions_document_id' AND object_id = OBJECT_ID('dbo.document_versions'))
    CREATE INDEX ix_versions_document_id ON dbo.document_versions (document_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_versions_doc_version' AND object_id = OBJECT_ID('dbo.document_versions'))
    CREATE UNIQUE INDEX ix_versions_doc_version ON dbo.document_versions (document_id, version_number);


-- ─── webhooks ──────────────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.webhooks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.webhooks (
        id              UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        name            NVARCHAR(200)     NOT NULL,
        url             NVARCHAR(2000)    NOT NULL,
        secret          NVARCHAR(500),
        events          NVARCHAR(MAX)     NOT NULL,
        is_active       BIT               NOT NULL DEFAULT 1,
        timeout_seconds INT               NOT NULL DEFAULT 30,
        max_retries     INT               NOT NULL DEFAULT 3,
        created_at      DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        last_triggered_at DATETIME2,
        success_count   INT               NOT NULL DEFAULT 0,
        failure_count   INT               NOT NULL DEFAULT 0
    );
END


-- ─── EF Migrations history ────────────────────────────────────────────────────
-- EF Core automatically creates this table on first migration run.
-- Pre-creating it here prevents migration history insert failures on fresh databases.
IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.__EFMigrationsHistory (
        MigrationId    NVARCHAR(150)  NOT NULL PRIMARY KEY,
        ProductVersion NVARCHAR(32)   NOT NULL
    );
END

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = '20260313114636_InitialCreate')
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260313114636_InitialCreate', '8.0.25');