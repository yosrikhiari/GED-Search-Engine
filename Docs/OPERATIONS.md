# GED Search Engine - Operations Guide

## Backup Strategy

This document describes backup procedures for the GED (Electronic Document Management) Search Engine.

### 1. SQL Server Database Backup

The database contains all document metadata, user accounts, and ACL information.

**Volume Path:** `sqlserver-data` (Docker volume)

**Backup Command:**
```bash
# Option 1: Use sqlcmd inside the container
docker exec ged-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '${DB_PASSWORD}' -Q \
  "BACKUP DATABASE ged_db TO DISK = '/var/opt/mssql/backup/ged_db_$(date +%Y%m%d_%H%M%S).bak'"

# Option 2: Copy from Docker volume
docker run --rm -v ged-search-engine_sqlserver-data:/data -v $(pwd):/backup alpine \
  tar czf /backup/sqlserver_backup_$(date +%Y%m%d).tar.gz -C /data .
```

**Recommended Frequency:** Daily

### 2. OpenSearch Index Backup

The index contains all searchable document chunks and metadata.

**Volume Path:** `opensearch-data` (Docker volume)

**Backup via OpenSearch Snapshot API:**
```bash
# Register a snapshot repository (run once)
curl -X PUT "http://localhost:9200/_snapshot/ged_backup" -H 'Content-Type: application/json' -d '
{
  "type": "fs",
  "settings": {
    "location": "/usr/share/opensearch/backup"
  }
}'

# Create snapshot
curl -X PUT "http://localhost:9200/_snapshot/ged_backup/ged_snapshot_$(date +%Y%m%d)" -H 'Content-Type: application/json'
```

**Recommended Frequency:** Weekly

### 3. Uploaded Files Backup

Uploaded documents are stored in the `ged-documents` volume.

**Volume Path:** `ged-documents` (Docker volume)

**Backup Command:**
```bash
# Option 1: Docker volume copy
docker run --rm -v ged-search-engine_ged-documents:/data -v $(pwd):/backup alpine \
  tar czf /backup/documents_backup_$(date +%Y%m%d).tar.gz -C /data .

# Option 2: rsync from container
docker exec ged-backend tar czf - -C /var/lib/ged/documents . > documents_$(date +%Y%m%d).tar.gz
```

**Recommended Frequency:** Daily (incremental) or Weekly (full)

### 4. Redis Cache Backup

Redis contains session data and distributed cache entries. This is ephemeral and can be rebuilt from the database.

**Volume Path:** `redis-data` (if persistent)

**Note:** Redis is configured with `--save ""` which disables persistence. Sessions are rebuilt from login.

### 5. Backup Verification

After any backup, verify:
1. Backup files are not empty
2. Compression completed without errors
3. Test restore in a non-production environment

### 6. Restore Procedures

**Database Restore:**
```bash
docker exec -i ged-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '${DB_PASSWORD}' \
  -Q "RESTORE DATABASE ged_db FROM DISK = '/var/opt/mssql/backup/ged_db_backup.bak'"
```

**OpenSearch Restore:**
```bash
curl -X POST "http://localhost:9200/_snapshot/ged_backup/ged_snapshot_YYYYMMDD/_restore"
```

### 7. Retention Policy

| Data Type | Retention | Storage |
|-----------|-----------|---------|
| Database backups | 30 days | Local + offsite |
| OpenSearch snapshots | 7 days | Local |
| Document archives | 90 days | Local + offsite |
| Application logs | 7 days | Local |

### 8. Automation (Recommended)

Create a cron job or scheduled task:

```bash
# /etc/cron.daily/ged-backup
#!/bin/bash
DATE=$(date +%Y%m%d)
BACKUP_DIR=/backups/ged

# Database backup
docker exec ged-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "${DB_PASSWORD}" \
  -Q "BACKUP DATABASE ged_db TO DISK = '/var/opt/mssql/backup/ged_db_${DATE}.bak'"
docker cp ged-sqlserver:/var/opt/mssql/backup/ged_db_${DATE}.bak ${BACKUP_DIR}/

# OpenSearch snapshot
curl -X PUT "http://localhost:9200/_snapshot/ged_backup/snapshot_${DATE}" 2>/dev/null

# Document files
docker exec ged-backend tar czf - -C /var/lib/ged/documents . > ${BACKUP_DIR}/documents_${DATE}.tar.gz

# Cleanup old backups (keep 30 days)
find ${BACKUP_DIR} -type f -mtime +30 -delete
```
