using OpenSearch.Client;
using GED.Core.Models;
using GED.Infrastructure.Constants;
using CoreSearchRequest = GED.Core.Models.SearchRequest;

namespace GED.Infrastructure.Services.Search;

/// <summary>
/// Builds OpenSearch queries for document and chunk search.
/// </summary>
public class SearchQueryBuilder
{
    /// <summary>
    /// Builds a BM25 precision query for document search.
    /// </summary>
    public QueryContainer BuildPrecisionQuery(
        QueryContainerDescriptor<DocumentIndexModel> q,
        string query,
        CoreSearchRequest request,
        NaturalLanguageQuery? nlQuery)
    {
        var shouldQueries = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();
        
        // Multi-match across multiple fields with different weights
        shouldQueries.Add(m => m.MultiMatch(mm => mm
            .Query(query)
            .Fields(f => f
                .Field(ff => ff.Title, 3.0)
                .Field(ff => ff.Description, 2.0)
                .Field(ff => ff.ExtractedText, 1.5)
                .Field(ff => ff.OcrText, 1.5)
                .Field(ff => ff.Tags, 1.0)
                .Field(ff => ff.Category, 1.0)
                .Field(ff => ff.FileName, 1.0))
            .Type(TextQueryType.BestFields)
            .Operator(Operator.Or)));

        // Category alias expansion
        var expandedQueries = ExpandCategoryAliases(query);
        if (expandedQueries.Count > 0)
        {
            shouldQueries.Add(m => m.MultiMatch(mm => mm
                .Query(string.Join(" ", expandedQueries))
                .Fields(f => f
                    .Field(ff => ff.Category, 3.0)
                    .Field(ff => ff.Description, 1.0))
                .Type(TextQueryType.BestFields)));
        }

        return q.Bool(b => b.Should(shouldQueries.ToArray()).MinimumShouldMatch(1));
    }

    /// <summary>
    /// Expands category aliases (FR/AR → English).
    /// </summary>
    private static List<string> ExpandCategoryAliases(string query)
        => CategoryAliases.ExpandFromQuery(query).ToList();
}
