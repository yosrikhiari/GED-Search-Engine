import json
import os
import tempfile
import requests

DASHBOARDS_URL = "http://localhost:5601/api/saved_objects/_import?overwrite=true"
PIPELINE_EVENTS_ID = "d129da60-301a-11f1-9d18-4fa9a9579a3a"
DOCUMENTS_ID = "ged-documents"


def make_obj(vis_type, obj_id, title, vis_state, search_source_dict, index_id):
    """
    Build the saved-object dict.
    vis_state is a plain Python dict — json.dumps(outer_obj) will serialize
    it as a nested JSON string automatically (no manual escaping needed).
    """
    return {
        "type": vis_type,
        "id": obj_id,
        "attributes": {
            "title": title,
            # KEY FIX: just json.dumps the vis_state dict into a string.
            # json.dumps(outer) then escapes it correctly — no double-encoding.
            "visState": json.dumps(vis_state),
            "uiStateJSON": "{}",
            "description": "",
            "version": 1,
            "kibanaSavedObjectMeta": {
                "searchSourceJSON": json.dumps({
                    **search_source_dict,
                    "indexRefName": "kibanaSavedObjectMeta.searchSourceJSON.index"
                })
            },
        },
        "references": [
            {
                "name": "kibanaSavedObjectMeta.searchSourceJSON.index",
                "type": "index-pattern",
                "id": index_id,
            }
        ],
    }


def create_bar_vis(title, vis_id, index_id, query, time_field="timestamp"):
    vis_state = {
        "title": title,
        "type": "histogram",
        "params": {
            "type": "histogram",
            "grid": {"categoryLines": False},
            "categoryAxes": [{"id": "CategoryAxis-1", "type": "category", "position": "bottom", "show": True,
                               "style": {}, "scale": {"type": "linear"},
                               "labels": {"show": True, "filter": True, "truncate": 100}, "title": {}}],
            "valueAxes": [{"id": "ValueAxis-1", "name": "LeftAxis-1", "type": "value", "position": "left",
                            "show": True, "style": {}, "scale": {"type": "linear", "mode": "normal"},
                            "labels": {"show": True, "rotate": 0, "filter": False, "truncate": 100},
                            "title": {"text": "Count"}}],
            "seriesParams": [{"show": True, "type": "histogram", "mode": "stacked",
                               "data": {"label": "Count", "id": "1"}, "valueAxis": "ValueAxis-1",
                               "drawLinesBetweenPoints": True, "lineWidth": 2, "showCircles": True}],
            "addTooltip": True, "addLegend": True, "legendPosition": "right",
            "times": [], "addTimeMarker": False,
            "thresholdLine": {"show": False, "value": 10, "width": 1, "style": "full", "color": "#E7664C"},
            "labels": {},
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "count", "schema": "metric", "params": {}},
            {"id": "2", "enabled": True, "type": "date_histogram", "schema": "segment",
             "params": {"field": time_field, "useNormalizedOpenSearchInterval": True,
                        "scaleMetricValues": False, "interval": "auto", "drop_partials": False,
                        "min_doc_count": 1, "extended_bounds": {}}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": query}, "filter": []}, index_id)


def create_stacked_bar_vis(title, vis_id, index_id, query, time_field="timestamp", split_field="createdByUserId", split_size=5):
    vis_state = {
        "title": title,
        "type": "histogram",
        "params": {
            "type": "histogram",
            "grid": {"categoryLines": False},
            "categoryAxes": [{"id": "CategoryAxis-1", "type": "category", "position": "bottom", "show": True,
                               "style": {}, "scale": {"type": "linear"},
                               "labels": {"show": True, "filter": True, "truncate": 100}, "title": {}}],
            "valueAxes": [{"id": "ValueAxis-1", "name": "LeftAxis-1", "type": "value", "position": "left",
                            "show": True, "style": {}, "scale": {"type": "linear", "mode": "normal"},
                            "labels": {"show": True, "rotate": 0, "filter": False, "truncate": 100},
                            "title": {"text": "Count"}}],
            "seriesParams": [{"show": True, "type": "histogram", "mode": "stacked",
                               "data": {"label": "Count", "id": "1"}, "valueAxis": "ValueAxis-1",
                               "drawLinesBetweenPoints": True, "lineWidth": 2, "showCircles": True}],
            "addTooltip": True, "addLegend": True, "legendPosition": "right",
            "times": [], "addTimeMarker": False,
            "thresholdLine": {"show": False, "value": 10, "width": 1, "style": "full", "color": "#E7664C"},
            "labels": {},
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "count", "schema": "metric", "params": {}},
            {"id": "2", "enabled": True, "type": "date_histogram", "schema": "segment",
             "params": {"field": time_field, "useNormalizedOpenSearchInterval": True,
                        "scaleMetricValues": False, "interval": "auto", "drop_partials": False,
                        "min_doc_count": 1, "extended_bounds": {}}},
            {"id": "3", "enabled": True, "type": "terms", "schema": "group",
             "params": {"field": split_field, "orderBy": "1", "order": "desc", "size": split_size,
                        "otherBucket": True, "otherBucketLabel": "Other",
                        "missingBucket": True, "missingBucketLabel": "Unknown"}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": query}, "filter": []}, index_id)


def create_line_vis(vis_id, index_id):
    title = "Stage latency"
    vis_state = {
        "title": title,
        "type": "line",
        "params": {
            "type": "line",
            "grid": {"categoryLines": False},
            "categoryAxes": [{"id": "CategoryAxis-1", "type": "category", "position": "bottom", "show": True,
                               "style": {}, "scale": {"type": "linear"},
                               "labels": {"show": True, "filter": True, "truncate": 100}, "title": {}}],
            "valueAxes": [{"id": "ValueAxis-1", "name": "LeftAxis-1", "type": "value", "position": "left",
                            "show": True, "style": {}, "scale": {"type": "linear", "mode": "normal"},
                            "labels": {"show": True, "rotate": 0, "filter": False, "truncate": 100},
                            "title": {"text": "Avg duration (ms)"}}],
            "seriesParams": [{"show": True, "type": "line", "mode": "normal",
                               "data": {"label": "Average durationMs", "id": "1"}, "valueAxis": "ValueAxis-1",
                               "drawLinesBetweenPoints": True, "lineWidth": 2, "interpolate": "linear",
                               "showCircles": True}],
            "addTooltip": True, "addLegend": True, "legendPosition": "right",
            "times": [], "addTimeMarker": False,
            "thresholdLine": {"show": False, "value": 10, "width": 1, "style": "full", "color": "#E7664C"},
            "labels": {},
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "avg", "schema": "metric", "params": {"field": "durationMs"}},
            {"id": "2", "enabled": True, "type": "date_histogram", "schema": "segment",
             "params": {"field": "timestamp", "useNormalizedOpenSearchInterval": True,
                        "scaleMetricValues": False, "interval": "auto", "drop_partials": False,
                        "min_doc_count": 1, "extended_bounds": {}}},
             {"id": "3", "enabled": True, "type": "terms", "schema": "group",
              "params": {"field": "pipelineStage.keyword", "orderBy": "1", "order": "desc", "size": 10,
                         "otherBucket": False, "otherBucketLabel": "Other",
                         "missingBucket": False, "missingBucketLabel": "Missing"}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": "*"}, "filter": []}, index_id)


def create_pie_vis(title, vis_id, index_id, query, split_field):
    vis_state = {
        "title": title,
        "type": "pie",
        "params": {
            "type": "pie",
            "addTooltip": True, "addLegend": True, "legendPosition": "right", "isDonut": True,
            "labels": {"show": False, "values": True, "last_level": True, "truncate": 100},
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "count", "schema": "metric", "params": {}},
            {"id": "2", "enabled": True, "type": "terms", "schema": "segment",
             "params": {"field": split_field, "orderBy": "1", "order": "desc", "size": 10,
                        "otherBucket": False, "otherBucketLabel": "Other",
                        "missingBucket": False, "missingBucketLabel": "Missing"}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": query}, "filter": []}, index_id)


def create_ocr_confidence_vis(vis_id, index_id):
    title = "OCR processed documents"
    vis_state = {
        "title": title,
        "type": "metric",
        "params": {
            "addTooltip": True, "addLegend": False, "type": "metric",
            "metric": {
                "percentageMode": False, "useRanges": False,
                "colorSchema": "Green to Red", "metricColorMode": "None",
                "colorsRange": [{"from": 0, "to": 10000}],
                "labels": {"show": True}, "invertColors": False,
                "style": {"bgFill": "#000", "bgColor": False, "labelColor": False, "subText": "OCR processed", "fontSize": 60},
            },
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "count", "schema": "metric", "params": {}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": "isOcrProcessed:true"}, "filter": []}, index_id)


def create_retry_vis(vis_id, index_id):
    title = "Retry count by queue"
    vis_state = {
        "title": title,
        "type": "histogram",
        "params": {
            "type": "histogram",
            "grid": {"categoryLines": False},
            "categoryAxes": [{"id": "CategoryAxis-1", "type": "category", "position": "bottom", "show": True,
                               "style": {}, "scale": {"type": "linear"},
                               "labels": {"show": True, "filter": True, "truncate": 100}, "title": {"text": "Queue"}}],
            "valueAxes": [{"id": "ValueAxis-1", "name": "LeftAxis-1", "type": "value", "position": "left",
                            "show": True, "style": {}, "scale": {"type": "linear", "mode": "normal"},
                            "labels": {"show": True, "rotate": 0, "filter": False, "truncate": 100},
                            "title": {"text": "Total retries"}}],
            "seriesParams": [{"show": True, "type": "histogram", "mode": "normal",
                               "data": {"label": "Total retries", "id": "1"}, "valueAxis": "ValueAxis-1",
                               "drawLinesBetweenPoints": True, "lineWidth": 2, "showCircles": True}],
            "addTooltip": True, "addLegend": False,
            "times": [], "addTimeMarker": False,
            "thresholdLine": {"show": False, "value": 10, "width": 1, "style": "full", "color": "#E7664C"},
            "labels": {},
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "sum", "schema": "metric", "params": {"field": "retryCount"}},
            {"id": "2", "enabled": True, "type": "terms", "schema": "segment",
             "params": {"field": "queueName.keyword", "orderBy": "1", "order": "desc", "size": 10,
                        "otherBucket": True, "otherBucketLabel": "Other",
                        "missingBucket": True, "missingBucketLabel": "N/A"}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": "*"}, "filter": []}, index_id)


def create_doc_status_vis(vis_id, index_id):
    title = "Document status distribution"
    vis_state = {
        "title": title,
        "type": "pie",
        "params": {
            "type": "pie",
            "addTooltip": True, "addLegend": True, "legendPosition": "right", "isDonut": True,
            "labels": {"show": True, "values": True, "last_level": True, "truncate": 100},
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "count", "schema": "metric", "params": {}},
             {"id": "2", "enabled": True, "type": "terms", "schema": "segment",
             "params": {"field": "status", "orderBy": "1", "order": "desc", "size": 10,
                        "otherBucket": False, "otherBucketLabel": "Other",
                        "missingBucket": False, "missingBucketLabel": "Missing"}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": "*"}, "filter": []}, index_id)


def create_metric_vis(vis_id, index_id):
    title = "Completed pipeline events"
    vis_state = {
        "title": title,
        "type": "metric",
        "params": {
            "addTooltip": True, "addLegend": False, "type": "metric",
            "metric": {
                "percentageMode": False, "useRanges": False,
                "colorSchema": "Green to Red", "metricColorMode": "None",
                "colorsRange": [{"from": 0, "to": 10000}],
                "labels": {"show": True}, "invertColors": False,
                "style": {"bgFill": "#000", "bgColor": False, "labelColor": False, "subText": "Completed", "fontSize": 60},
            },
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "count", "schema": "metric", "params": {}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": "status.keyword:completed"}, "filter": []}, index_id)


def create_table_vis(vis_id, index_id):
    title = "Failed events"
    vis_state = {
        "title": title,
        "type": "table",
        "params": {
            "perPage": 10, "showPartialRows": False, "showMetricsAtAllLevels": False,
            "sort": {"columnIndex": None, "direction": None},
            "showTotal": False, "totalFunc": "sum",
            "dimensions": {
                "metrics": [{"accessor": 0, "format": {"id": "number"}, "params": {}, "aggType": "count"}],
                "buckets": [],
            },
        },
        "aggs": [
            {"id": "1", "enabled": True, "type": "count", "schema": "metric", "params": {}},
            {"id": "2", "enabled": True, "type": "terms", "schema": "bucket",
             "params": {"field": "pipelineStage.keyword", "orderBy": "1", "order": "desc", "size": 10,
                        "otherBucket": False, "otherBucketLabel": "Other",
                        "missingBucket": False, "missingBucketLabel": "Missing"}},
            {"id": "3", "enabled": True, "type": "terms", "schema": "bucket",
             "params": {"field": "documentId.keyword", "orderBy": "1", "order": "desc", "size": 10,
                        "otherBucket": False, "otherBucketLabel": "Other",
                        "missingBucket": False, "missingBucketLabel": "Missing"}},
        ],
    }
    return make_obj("visualization", vis_id, title, vis_state,
                    {"query": {"language": "kuery", "query": "status.keyword:failed"}, "filter": []}, index_id)


def create_dashboard():
    panels = [
        {"panelIndex": "0", "gridData": {"x": 0,  "y": 0,  "w": 36, "h": 15, "i": "0"}, "version": "7.10.0", "type": "visualization", "id": "upload-volume"},
        {"panelIndex": "1", "gridData": {"x": 36, "y": 0,  "w": 12, "h": 15, "i": "1"}, "version": "7.10.0", "type": "visualization", "id": "pipeline-success-rate"},
        {"panelIndex": "2", "gridData": {"x": 0,  "y": 15, "w": 48, "h": 15, "i": "2"}, "version": "7.10.0", "type": "visualization", "id": "stage-latency"},
        {"panelIndex": "3", "gridData": {"x": 0,  "y": 30, "w": 16, "h": 15, "i": "3"}, "version": "7.10.0", "type": "visualization", "id": "documents-by-category"},
        {"panelIndex": "4", "gridData": {"x": 16, "y": 30, "w": 16, "h": 15, "i": "4"}, "version": "7.10.0", "type": "visualization", "id": "duplicate-detection"},
        {"panelIndex": "5", "gridData": {"x": 32, "y": 30, "w": 16, "h": 15, "i": "5"}, "version": "7.10.0", "type": "visualization", "id": "ocr-vs-native"},
        {"panelIndex": "10", "gridData": {"x": 0,  "y": 45, "w": 16, "h": 15, "i": "10"}, "version": "7.10.0", "type": "visualization", "id": "document-status"},
        {"panelIndex": "6", "gridData": {"x": 16, "y": 45, "w": 16, "h": 15, "i": "6"}, "version": "7.10.0", "type": "visualization", "id": "stage-error-breakdown"},
        {"panelIndex": "7", "gridData": {"x": 32, "y": 45, "w": 16, "h": 15, "i": "7"}, "version": "7.10.0", "type": "visualization", "id": "processing-time-dist"},
        {"panelIndex": "8", "gridData": {"x": 0,  "y": 60, "w": 24, "h": 15, "i": "8"}, "version": "7.10.0", "type": "visualization", "id": "failed-events"},
        {"panelIndex": "11", "gridData": {"x": 24, "y": 60, "w": 24, "h": 15, "i": "11"}, "version": "7.10.0", "type": "visualization", "id": "ocr-confidence"},
        {"panelIndex": "12", "gridData": {"x": 0,  "y": 75, "w": 24, "h": 15, "i": "12"}, "version": "7.10.0", "type": "visualization", "id": "retry-by-queue"},
        {"panelIndex": "9", "gridData": {"x": 24, "y": 75, "w": 24, "h": 15, "i": "9"}, "version": "7.10.0", "type": "visualization", "id": "user-upload-activity"},
    ]
    return {
        "type": "dashboard",
        "id": "ged-pipeline-monitor",
        "attributes": {
            "title": "GED Pipeline Monitor",
            "hits": 0,
            "description": "Pipeline monitoring dashboard for GED document processing",
            "panelsJSON": json.dumps(panels),
            "optionsJSON": json.dumps({"useMargins": True, "syncColors": False, "hidePanelTitles": False}),
            "timeRestore": False,
            "kibanaSavedObjectMeta": {
                "searchSourceJSON": json.dumps({"query": {"language": "kuery", "query": ""}, "filter": []})
            },
        },
        "references": [
            {"name": "panel_0", "type": "visualization", "id": "upload-volume"},
            {"name": "panel_1", "type": "visualization", "id": "pipeline-success-rate"},
            {"name": "panel_2", "type": "visualization", "id": "stage-latency"},
            {"name": "panel_3", "type": "visualization", "id": "documents-by-category"},
            {"name": "panel_4", "type": "visualization", "id": "duplicate-detection"},
            {"name": "panel_5", "type": "visualization", "id": "ocr-vs-native"},
            {"name": "panel_6", "type": "visualization", "id": "stage-error-breakdown"},
            {"name": "panel_7", "type": "visualization", "id": "processing-time-dist"},
            {"name": "panel_8", "type": "visualization", "id": "failed-events"},
            {"name": "panel_9", "type": "visualization", "id": "user-upload-activity"},
            {"name": "panel_10", "type": "visualization", "id": "document-status"},
            {"name": "panel_11", "type": "visualization", "id": "ocr-confidence"},
            {"name": "panel_12", "type": "visualization", "id": "retry-by-queue"},
        ],
    }


def resolve_index_pattern_id(title):
    """Find the actual UUID of an index-pattern by its title."""
    url = f"http://localhost:5601/api/saved_objects/_find?type=index-pattern&search={title}&search_fields=title"
    resp = requests.get(url)
    resp.raise_for_status()
    data = resp.json()
    if data.get("total", 0) == 0:
        raise RuntimeError(f"Index-pattern '{title}' not found in OpenSearch Dashboards")
    obj = data["saved_objects"][0]
    print(f"  Resolved index-pattern '{title}' -> {obj['id']}")
    return obj["id"]


def main():
    # Resolve actual UUIDs dynamically — they change after every docker compose down -v
    print("Resolving index-pattern IDs...")
    pipeline_events_id = resolve_index_pattern_id("ged-pipeline-events")
    documents_id = resolve_index_pattern_id("ged-documents")

    objects = [
        # Bar charts — ged-pipeline-events uses "timestamp"; ged-documents uses "createdAt"
        create_bar_vis("Upload volume over time",      "upload-volume",        pipeline_events_id, "pipelineStage.keyword:upload AND status.keyword:completed", time_field="timestamp"),
        create_bar_vis("Processing time distribution", "processing-time-dist", pipeline_events_id, "pipelineStage.keyword:ocr_worker",                          time_field="timestamp"),
        create_stacked_bar_vis("User upload activity", "user-upload-activity", documents_id,       "*",                                                 time_field="createdAt", split_field="createdByUserId", split_size=5),
        # Line
        create_line_vis("stage-latency", pipeline_events_id),
        # Pies
        create_pie_vis("Documents by category",    "documents-by-category", documents_id,       "*",                         "category.keyword"),
        create_pie_vis("Duplicate detection rate", "duplicate-detection",   pipeline_events_id, "pipelineStage.keyword:file_storage", "duplicateDetected"),
        create_pie_vis("OCR vs native text",       "ocr-vs-native",         pipeline_events_id, "pipelineStage.keyword:ocr_worker",   "extractionMethod.keyword"),
        create_pie_vis("Stage error breakdown",    "stage-error-breakdown", pipeline_events_id, "status.keyword:failed",      "pipelineStage.keyword"),
        create_doc_status_vis("document-status",   documents_id),
        # Histograms
        create_ocr_confidence_vis("ocr-confidence", documents_id),
        create_retry_vis("retry-by-queue",         pipeline_events_id),
        # Metric + table
        create_metric_vis("pipeline-success-rate", pipeline_events_id),
        create_table_vis("failed-events",          pipeline_events_id),
        # Dashboard
        create_dashboard(),
    ]

    ndjson_content = "\n".join(json.dumps(o, ensure_ascii=False) for o in objects)

    with tempfile.NamedTemporaryFile(mode='w', suffix='.ndjson', delete=False, encoding='utf-8') as f:
        f.write(ndjson_content)
        temp_path = f.name

    try:
        with open(temp_path, 'rb') as f:
            response = requests.post(
                DASHBOARDS_URL,
                headers={"osd-xsrf": "true"},
                files={"file": ("dashboard.ndjson", f, "application/ndjson")},
            )
        print("HTTP status:", response.status_code)
        result = response.json()
        print("Import result:", json.dumps(result, indent=2))

        if result.get("success"):
            print("\nDashboard created successfully!")
            print("Open: http://localhost:5601/app/dashboards#/view/ged-pipeline-monitor")

            # Export index-patterns + dashboard objects for auto-restore
            # Fetch the actual index-pattern objects from Dashboards
            ip_objects = []
            for title, resolved_id in [("ged-pipeline-events", pipeline_events_id), ("ged-documents", documents_id)]:
                resp = requests.get(f"http://localhost:5601/api/saved_objects/index-pattern/{resolved_id}")
                if resp.ok:
                    ip_obj = resp.json()
                    ip_objects.append({
                        "type": "index-pattern",
                        "id": ip_obj["id"],
                        "attributes": ip_obj["attributes"],
                        "references": ip_obj.get("references", []),
                        "migrationVersion": ip_obj.get("migrationVersion", {}),
                    })

            # Build complete NDJSON: index-patterns first, then visualizations + dashboard
            all_objects = ip_objects + objects
            ndjson_full = "\n".join(json.dumps(o, ensure_ascii=False) for o in all_objects)

            ndjson_path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                       "opensearch_dashboards", "saved_objects.ndjson")
            with open(ndjson_path, "w", encoding="utf-8") as out:
                out.write(ndjson_full)
            print(f"Saved NDJSON to {ndjson_path} ({len(all_objects)} objects)")
        else:
            print("\nImport finished with errors:")
            for err in result.get("errors", []):
                print(" -", err)
    finally:
        os.unlink(temp_path)


if __name__ == "__main__":
    main()