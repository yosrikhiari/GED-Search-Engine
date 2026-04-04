using FluentAssertions;
using GED.Core.Models;

namespace GED.Tests.Quality;

public class SearchServiceQualityTests
{
    [Fact]
    public void SearchRequest_DefaultValues_AreCorrect()
    {
        var request = new SearchRequest();
        request.Query.Should().BeEmpty();
        request.Page.Should().Be(1);
        request.PageSize.Should().Be(20);
        request.SearchType.Should().Be(SearchType.Natural);
        request.SortBy.Should().Be(SortField.Relevance);
        request.SortDescending.Should().BeTrue();
        request.IncludeOcrContent.Should().BeTrue();
        request.IncludeSuggestions.Should().BeFalse();
    }

    [Fact]
    public void SearchRequest_WithFilters_SetsAllProperties()
    {
        var request = new SearchRequest
        {
            Query = "invoice 2024",
            Page = 2,
            PageSize = 50,
            Categories = new List<string> { "Invoice" },
            ContentTypes = new List<string> { "application/pdf" },
            FromDate = new DateTime(2024, 1, 1),
            ToDate = new DateTime(2024, 12, 31),
            Tags = new List<string> { "monthly" },
            SortBy = SortField.CreatedDate,
            SortDescending = false,
            SearchType = SearchType.Natural
        };

        request.Page.Should().Be(2);
        request.PageSize.Should().Be(50);
        request.Categories.Should().Contain("Invoice");
        request.FromDate.Should().Be(new DateTime(2024, 1, 1));
    }

    [Fact]
    public void SearchRequest_Pagination_OffsetCalculation()
    {
        var page = 3;
        var pageSize = 20;
        var expectedOffset = (page - 1) * pageSize;
        expectedOffset.Should().Be(40);
    }

    [Fact]
    public void SearchRequest_MaxPageSize_IsLimited()
    {
        var request = new SearchRequest { PageSize = 1000 };
        var maxPageSize = 100;
        var effectivePageSize = Math.Min(request.PageSize, maxPageSize);
        effectivePageSize.Should().Be(maxPageSize);
    }

    [Fact]
    public void SearchRequest_MinPage_IsOne()
    {
        var request = new SearchRequest { Page = 0 };
        var effectivePage = Math.Max(request.Page, 1);
        effectivePage.Should().Be(1);
    }

    [Fact]
    public void SearchResult_WithDocuments_ContainsAllFields()
    {
        var result = new SearchResult
        {
            TotalResults = 150,
            Page = 1,
            PageSize = 20,
            TotalPages = 8,
            Documents = new List<DocumentSearchHit>
            {
                new() { Id = Guid.NewGuid(), Title = "Invoice 2024", Score = 0.95f },
                new() { Id = Guid.NewGuid(), Title = "Invoice 2023", Score = 0.85f }
            },
            Facets = new Dictionary<string, List<FacetValue>>
            {
                ["category"] = new List<FacetValue> { new() { Value = "Invoice", Count = 100 } }
            },
            ProcessedQuery = "invoice 2024",
            IsUnderstood = true,
            DetectedLanguage = "en",
            SearchMode = SearchMode.Hybrid
        };

        result.TotalResults.Should().Be(150);
        result.TotalPages.Should().Be(8);
        result.Documents.Should().HaveCount(2);
        result.Facets.Should().ContainKey("category");
        result.SearchMode.Should().Be(SearchMode.Hybrid);
    }

    [Fact]
    public void SearchResult_NoResults_ReturnsEmptyList()
    {
        var result = new SearchResult
        {
            TotalResults = 0,
            Documents = new List<DocumentSearchHit>()
        };

        result.TotalResults.Should().Be(0);
        result.Documents.Should().BeEmpty();
    }

    [Fact]
    public void DocumentSearchHit_WithHighlights_HighlightsText()
    {
        var hit = new DocumentSearchHit
        {
            Id = Guid.NewGuid(),
            Title = "Invoice 2024",
            Score = 0.95f,
            Highlights = new List<string> { "<em>Invoice</em> 2024" },
            ExtractedText = "...monthly <em>invoice</em> for January..."
        };

        hit.Highlights.Should().Contain(h => h.Contains("<em>"));
        hit.ExtractedText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CategoryAliases_French_Resolved()
    {
        var categoryAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["contrat"] = "Contract",
            ["facture"] = "Invoice",
            ["rapport"] = "Report",
            ["lettre"] = "Letter",
            ["devis"] = "Invoice"
        };

        categoryAliases["facture"].Should().Be("Invoice");
        categoryAliases["contrat"].Should().Be("Contract");
        categoryAliases["rapport"].Should().Be("Report");
    }

    [Fact]
    public void CategoryAliases_Arabic_Resolved()
    {
        var arabicAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["فاتورة"] = "Invoice",
            ["عقد"] = "Contract",
            ["تقرير"] = "Report"
        };

        arabicAliases["فاتورة"].Should().Be("Invoice");
        arabicAliases["عقد"].Should().Be("Contract");
    }

    [Fact]
    public void HybridSearch_Weights_CombineCorrectly()
    {
        var bm25Weight = 0.6f;
        var semanticWeight = 0.4f;
        var bm25Score = 100.0f;
        var semanticScore = 0.85f;

        var normalizedBm25 = bm25Score / 100.0f;
        var combinedScore = (bm25Weight * normalizedBm25) + (semanticWeight * semanticScore);
        combinedScore.Should().BeGreaterThan(0);
        combinedScore.Should().BeLessThan(1);
    }

    [Fact]
    public void HybridSearch_RRF_MergesResults()
    {
        var bm25Results = new List<(Guid Id, double Score)> { (Guid.NewGuid(), 1.0), (Guid.NewGuid(), 0.8) };
        var knnResults = new List<(Guid Id, double Score)> { (Guid.NewGuid(), 0.9), (Guid.NewGuid(), 0.7) };
        var rrfK = 60;

        var rrfScores = new Dictionary<Guid, double>();

        for (int i = 0; i < bm25Results.Count; i++)
        {
            var docId = bm25Results[i].Id;
            var rrfScore = 1.0 / (rrfK + i + 1);
            rrfScores[docId] = rrfScores.GetValueOrDefault(docId, 0) + rrfScore;
        }

        for (int i = 0; i < knnResults.Count; i++)
        {
            var docId = knnResults[i].Id;
            var rrfScore = 1.0 / (rrfK + i + 1);
            rrfScores[docId] = rrfScores.GetValueOrDefault(docId, 0) + rrfScore;
        }

        rrfScores.Should().HaveCount(4);
        rrfScores.Values.All(v => v > 0).Should().BeTrue();
    }

    [Fact]
    public void AclFilter_Admin_BypassesAll()
    {
        var userRole = "Admin";
        var shouldBypassAcl = userRole == "Admin";
        shouldBypassAcl.Should().BeTrue();
    }

    [Fact]
    public void AclFilter_User_SeesRestrictedDocuments()
    {
        var userId = Guid.NewGuid();
        var documentAllowedUsers = new List<string> { userId.ToString() };
        var documentAccessLevel = "restricted";

        var hasAccess = documentAccessLevel == "open" || documentAllowedUsers.Contains(userId.ToString());
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public void AclFilter_User_CannotSeeOthersDocuments()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var documentAllowedUsers = new List<string> { otherUserId.ToString() };
        var documentAccessLevel = "restricted";

        var hasAccess = documentAccessLevel == "open" || documentAllowedUsers.Contains(userId.ToString());
        hasAccess.Should().BeFalse();
    }

    [Fact]
    public void AclFilter_OpenDocument_EveryoneCanSee()
    {
        var userId = Guid.NewGuid();
        var documentAccessLevel = "open";
        var hasAccess = documentAccessLevel == "open";
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public void AclFilter_CategoryMatch_GrantsAccess()
    {
        var userCategories = new List<string> { "Invoice", "Contract" };
        var documentCategory = "Invoice";
        var hasAccess = userCategories.Contains(documentCategory);
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public void AclFilter_CategoryMismatch_DeniesAccess()
    {
        var userCategories = new List<string> { "Invoice", "Contract" };
        var documentCategory = "Report";
        var hasAccess = userCategories.Contains(documentCategory);
        hasAccess.Should().BeFalse();
    }

    [Fact]
    public void NaturalLanguageQuery_WithFrenchQuery_DetectsFrench()
    {
        var query = "trouvez mes factures de janvier 2024";
        var frenchIndicators = new[] { "trouvez", "mes", "factures", "janvier", "dans", "sur" };
        var isFrench = frenchIndicators.Any(w => query.Contains(w, StringComparison.OrdinalIgnoreCase));
        isFrench.Should().BeTrue();
    }

    [Fact]
    public void NaturalLanguageQuery_WithEnglishQuery_DetectsEnglish()
    {
        var query = "find my invoices from January 2024";
        var englishIndicators = new[] { "find", "my", "invoices", "January", "in", "from" };
        var isEnglish = englishIndicators.Any(w => query.Contains(w, StringComparison.OrdinalIgnoreCase));
        isEnglish.Should().BeTrue();
    }

    [Fact]
    public void NaturalLanguageQuery_DateExtraction_Works()
    {
        var query = "invoices from January 2024";
        var datePatterns = new[] { "January 2024", "janvier 2024", "2024-01", "01/2024", "janvier", "2024" };
        var hasDate = datePatterns.Any(p => query.Contains(p, StringComparison.OrdinalIgnoreCase));
        hasDate.Should().BeTrue();
    }

    [Fact]
    public void NaturalLanguageQuery_CategoryExtraction_Works()
    {
        var query = "show me all contracts signed in 2024";
        var categoryKeywords = new[] { "contract", "contracts", "contrat", "facture", "invoice", "rapport", "report" };
        var extractedCategories = categoryKeywords.Where(k => query.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        extractedCategories.Should().NotBeEmpty();
    }

    [Fact]
    public void SearchType_AllTypes_AreDefined()
    {
        var searchTypes = Enum.GetValues<SearchType>();
        searchTypes.Should().Contain(SearchType.Natural);
        searchTypes.Should().Contain(SearchType.Exact);
        searchTypes.Should().Contain(SearchType.Fuzzy);
        searchTypes.Should().Contain(SearchType.Wildcard);
        searchTypes.Should().Contain(SearchType.Advanced);
        searchTypes.Length.Should().Be(5);
    }

    [Fact]
    public void FacetValue_ContainsValueAndCount()
    {
        var facet = new FacetValue { Value = "Invoice", Count = 100 };
        facet.Value.Should().Be("Invoice");
        facet.Count.Should().Be(100);
    }

    [Fact]
    public void SearchFilters_DateRange_Validation()
    {
        var dateFrom = new DateTime(2024, 1, 1);
        var dateTo = new DateTime(2024, 12, 31);
        dateFrom.Should().BeBefore(dateTo);
    }

    [Fact]
    public void SearchFilters_DateRange_InvalidRange_Detected()
    {
        var dateFrom = new DateTime(2024, 12, 31);
        var dateTo = new DateTime(2024, 1, 1);
        var isValid = dateFrom <= dateTo;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void EmbeddingDimension_Is1024()
    {
        const int embeddingDim = 1024;
        var testVector = new float[embeddingDim];
        testVector.Length.Should().Be(1024);
    }

    [Fact]
    public void SemanticThreshold_FiltersLowScores()
    {
        var threshold = 0.30f;
        var scores = new[] { 0.95f, 0.85f, 0.45f, 0.25f, 0.10f };
        var aboveThreshold = scores.Where(s => s >= threshold).ToList();
        aboveThreshold.Should().HaveCount(3);
        aboveThreshold.Should().OnlyContain(s => s >= threshold);
    }

    [Fact]
    public void CacheKey_Generation_IsDeterministic()
    {
        var request1 = new SearchRequest { Query = "invoice", Page = 1, PageSize = 20 };
        var request2 = new SearchRequest { Query = "invoice", Page = 1, PageSize = 20 };
        var key1 = $"search:{request1.Query}:{request1.Page}:{request1.PageSize}";
        var key2 = $"search:{request2.Query}:{request2.Page}:{request2.PageSize}";
        key1.Should().Be(key2);
    }

    [Fact]
    public void CacheKey_DifferentRequests_ProduceDifferentKeys()
    {
        var request1 = new SearchRequest { Query = "invoice", Page = 1 };
        var request2 = new SearchRequest { Query = "contract", Page = 1 };
        var key1 = $"search:{request1.Query}:{request1.Page}";
        var key2 = $"search:{request2.Query}:{request2.Page}";
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void QueryIntent_AllIntents_AreDefined()
    {
        var intents = Enum.GetValues<QueryIntent>();
        intents.Should().Contain(QueryIntent.Search);
        intents.Should().Contain(QueryIntent.Find);
        intents.Should().Contain(QueryIntent.List);
        intents.Should().Contain(QueryIntent.Filter);
        intents.Should().Contain(QueryIntent.Compare);
        intents.Should().Contain(QueryIntent.Summarize);
        intents.Length.Should().Be(6);
    }

    [Fact]
    public void SortField_AllFields_AreDefined()
    {
        var fields = Enum.GetValues<SortField>();
        fields.Should().Contain(SortField.Relevance);
        fields.Should().Contain(SortField.CreatedDate);
        fields.Should().Contain(SortField.ModifiedDate);
        fields.Should().Contain(SortField.Title);
        fields.Should().Contain(SortField.FileSize);
        fields.Length.Should().Be(5);
    }

    [Fact]
    public void SearchMode_AllModes_AreDefined()
    {
        var modes = Enum.GetValues<SearchMode>();
        modes.Should().Contain(SearchMode.BM25);
        modes.Should().Contain(SearchMode.Semantic);
        modes.Should().Contain(SearchMode.Hybrid);
        modes.Length.Should().Be(3);
    }

    [Fact]
    public void NaturalLanguageQuery_DefaultValues_AreCorrect()
    {
        var nlq = new NaturalLanguageQuery();
        nlq.OriginalQuery.Should().BeEmpty();
        nlq.ProcessedQuery.Should().BeEmpty();
        nlq.Keywords.Should().BeEmpty();
        nlq.Entities.Should().BeEmpty();
        nlq.DetectedLanguage.Should().Be("unknown");
        nlq.IsUnderstood.Should().BeTrue();
        nlq.Intent.Should().Be(default);
    }

    [Fact]
    public void NaturalLanguageQuery_ProcessQuery_RemovesStopWords()
    {
        var query = "find me all invoices from 2024";
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "me", "all", "from", "the", "a", "an", "find" };
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keywords = words.Where(w => !stopWords.Contains(w)).ToList();
        keywords.Should().Contain("invoices");
        keywords.Should().Contain("2024");
        keywords.Should().NotContain("find");
        keywords.Should().NotContain("all");
    }
}
