namespace GED.Core.Models;

public static class OcrConstants
{
    public static class Stages
    {
        public const string Pending = "pending";
        public const string Processing = "processing";
        public const string TextExtracted = "text_extracted";
        public const string LlmCleaning = "llm_cleaning";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public static class MetadataKeys
    {
        public const string OcrStage = "ocr_stage";
        public const string OcrError = "ocr_error";
        public const string OcrConfidence = "ocr_confidence";
        public const string OcrRetryRequested = "ocr_retry_requested";
        public const string OcrRetryRequestedBy = "ocr_retry_requested_by";
        public const string ExtractedDate = "extracted_date";
    }
}
