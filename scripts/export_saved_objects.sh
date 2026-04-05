#!/bin/bash
# Export OpenSearch Dashboards saved objects to NDJSON format
# Run this script when you have configured dashboards and want to save them for auto-restore

set -e

DASHBOARDS_URL="${DASHBOARDS_URL:-http://localhost:5601}"
OUTPUT_FILE="${OUTPUT_FILE:-./opensearch_dashboards/saved_objects.ndjson}"

echo "Exporting saved objects from OpenSearch Dashboards at $DASHBOARDS_URL..."

# Ensure output directory exists
mkdir -p "$(dirname "$OUTPUT_FILE")"

# Export all saved objects (index patterns, dashboards, visualizations, searches)
TEMP_FILE=$(mktemp)

curl -s -X POST "$DASHBOARDS_URL/api/saved_objects/_export" \
  -H "osd-xsrf: true" \
  -H "Content-Type: application/json" \
  -d '{"type": ["index-pattern", "dashboard", "visualization", "search"], "includeReferencesDeep": true}' \
  -o "$TEMP_FILE"

if [ -s "$TEMP_FILE" ]; then
  # OpenSearch Dashboards export returns newline-delimited JSON (NDJSON)
  # Split the single-line response into proper lines at }{ boundaries
  # Then filter out the summary line (has "exportedCount")
  
  # Split the line at }{ boundaries and add newlines
  sed 's/}{\s*/}\n{/g' "$TEMP_FILE" | \
    grep -v '"exportedCount"' | \
    grep -v '^$' > "$OUTPUT_FILE"
  
  # Count objects (lines starting with {)
  OBJECT_COUNT=$(grep -c "^{" "$OUTPUT_FILE" 2>/dev/null || echo "0")
  
  if [ "$OBJECT_COUNT" -gt 0 ]; then
    echo "✅ Exported $OBJECT_COUNT saved objects to $OUTPUT_FILE"
    echo "   Commit this file to version control to enable auto-restore on startup."
  else
    echo "⚠️  No saved objects found in response"
    echo "   Response size: $(wc -c < "$TEMP_FILE") bytes"
    echo "   You may need to create dashboards first in OpenSearch Dashboards UI."
    rm -f "$OUTPUT_FILE"
  fi
  
  rm -f "$TEMP_FILE"
else
  echo "❌ Export failed or returned empty response"
  rm -f "$TEMP_FILE"
  exit 1
fi
