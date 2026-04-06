#!/bin/bash
# Import OpenSearch Dashboards saved objects on container startup
# This script is designed to run as an init container in Docker Compose

set -e

DASHBOARDS_URL="${DASHBOARDS_URL:-http://opensearch-dashboards:5601}"
EXPORT_FILE="${EXPORT_FILE:-/init/saved_objects.ndjson}"
MAX_RETRIES="${MAX_RETRIES:-30}"
RETRY_INTERVAL="${RETRY_INTERVAL:-5}"

echo "=========================================="
echo "OpenSearch Dashboards Import Script"
echo "=========================================="
echo "URL: $DASHBOARDS_URL"
echo "Export file: $EXPORT_FILE"

# Check if export file exists
if [ ! -f "$EXPORT_FILE" ]; then
    echo "⚠️  No saved objects file found at $EXPORT_FILE"
    echo "   To enable auto-restore:"
    echo "   1. Configure your dashboards in OpenSearch Dashboards"
    echo "   2. Run: ./scripts/export_saved_objects.sh"
    echo "   3. Commit opensearch_dashboards/saved_objects.ndjson to version control"
    exit 0
fi

# Check if export file is empty
if [ ! -s "$EXPORT_FILE" ]; then
    echo "⚠️  Saved objects file is empty, skipping import"
    exit 0
fi

# Validate file format - should contain valid JSON objects
FILE_SIZE=$(wc -c < "$EXPORT_FILE")
LINE_COUNT=$(wc -l < "$EXPORT_FILE")
FIRST_CHAR=$(head -c 1 "$EXPORT_FILE")

# Check if it's a valid JSON object (starts with {)
if [ "$FIRST_CHAR" != "{" ]; then
    echo "⚠️  File does not appear to be valid saved objects format"
    echo "   Re-export using: ./scripts/export_saved_objects.sh"
    echo "   Skipping import..."
    exit 0
fi

# Check for empty or placeholder content
if [ "$FILE_SIZE" -lt 100 ]; then
    echo "⚠️  File appears to be placeholder or empty"
    echo "   Create dashboards first, then re-export using: ./scripts/export_saved_objects.sh"
    echo "   Skipping import..."
    exit 0
fi

# Count objects (lines starting with {)
OBJECT_COUNT=$(grep -c "^{" "$EXPORT_FILE" 2>/dev/null || echo "0")
if [ "$OBJECT_COUNT" -eq 0 ]; then
    echo "⚠️  No valid JSON objects found in file"
    echo "   Skipping import..."
    exit 0
fi

echo ""
echo "⏳ Waiting for OpenSearch Dashboards to be ready..."

# Wait for Dashboards to be available
RETRIES=0
until curl -s --fail "$DASHBOARDS_URL/api/status" > /dev/null 2>&1; do
    RETRIES=$((RETRIES + 1))
    if [ $RETRIES -ge $MAX_RETRIES ]; then
        echo "❌ Timeout waiting for OpenSearch Dashboards"
        exit 1
    fi
    echo "   Waiting... ($RETRIES/$MAX_RETRIES)"
    sleep $RETRY_INTERVAL
done

echo "✅ OpenSearch Dashboards is responding"

# Wait for OpenSearch to be ready (Dashboards may be up but ES may not be)
echo "⏳ Waiting for OpenSearch cluster to be ready..."
sleep 5

echo ""
echo "📥 Importing saved objects..."

# Filter out the summary line (last line with exportedCount)
TEMP_NDJSON="/tmp/saved_objects_import.ndjson"
grep -v '"exportedCount":' "$EXPORT_FILE" > "$TEMP_NDJSON" || cat "$EXPORT_FILE" > "$TEMP_NDJSON"

# Import saved objects with overwrite - pass filename explicitly to preserve extension
IMPORT_RESPONSE=$(curl -s -X POST "$DASHBOARDS_URL/api/saved_objects/_import?overwrite=true" \
  -H "osd-xsrf: true" \
  -F "file=@$TEMP_NDJSON;filename=saved_objects.ndjson;type=application/ndjson")

rm -f "$TEMP_NDJSON"

# Check response
if echo "$IMPORT_RESPONSE" | grep -q '"success":true'; then
    SUCCESS_COUNT=$(echo "$IMPORT_RESPONSE" | grep -o '"successCount":[0-9]*' | grep -o '[0-9]*' || echo "0")
    FAIL_COUNT=$(echo "$IMPORT_RESPONSE" | grep -o '"failedCount":[0-9]*' | grep -o '[0-9]*' || echo "0")
    
    echo "✅ Import completed successfully!"
    echo "   Success: $SUCCESS_COUNT objects"
    if [ "$FAIL_COUNT" != "0" ]; then
        echo "   Failed: $FAIL_COUNT objects (may already exist or have conflicts)"
    fi
else
    # Check if it's a partial success
    if echo "$IMPORT_RESPONSE" | grep -q '"success":false'; then
        echo "⚠️  Import completed with some failures"
        echo "   Response: $IMPORT_RESPONSE"
    else
        echo "❌ Import failed"
        echo "   Response: $IMPORT_RESPONSE"
        exit 1
    fi
fi

echo ""
echo "🎉 OpenSearch Dashboards configuration restored!"
echo "=========================================="
