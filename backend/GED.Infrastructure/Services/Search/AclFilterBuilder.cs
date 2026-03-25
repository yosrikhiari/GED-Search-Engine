using OpenSearch.Client;
using GED.Core.Models;

namespace GED.Infrastructure.Services.Search;

/// <summary>
/// Builds ACL (Access Control List) filter queries for OpenSearch.
/// </summary>
public class AclFilterBuilder
{
    /// <summary>
    /// Builds ACL filter for document search.
    /// </summary>
    public List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>> BuildDocumentAclFilters(
        string? userId,
        string? userRole,
        List<string>? userAllowedCategories)
    {
        var filters = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>();

        // Hard filter: only indexed documents
        filters.Add(fq => fq.Term(t => t.Field("status").Value("Indexed")));

        // ACL filter for non-admin users
        bool isNonAdminWithValidId = userRole != "Admin" && !string.IsNullOrEmpty(userId);
        bool isNonAdminWithoutId = userRole != "Admin" && string.IsNullOrEmpty(userId);

        if (isNonAdminWithoutId)
        {
            // Return no results if user has no ID (authentication issue)
            filters.Add(fq => fq.Term(t => t.Field("id").Value(Guid.Empty.ToString())));
        }
        else if (isNonAdminWithValidId)
        {
            var cats = userAllowedCategories ?? new List<string>();

            var aclShould = new List<Func<QueryContainerDescriptor<DocumentIndexModel>, QueryContainer>>
            {
                // Document is open to all
                s => s.Term(t => t.Field("accessLevel").Value("open")),
                // User has explicit ACL grant
                s => s.Term(t => t.Field("allowedUserIds").Value(userId))
            };

            // Category-based access
            foreach (var cat in cats)
            {
                var c = cat;
                aclShould.Add(s => s.Term(t => t.Field("category.keyword").Value(c)));
            }

            filters.Add(fq => fq.Bool(ab => ab
                .Should(aclShould.ToArray())
                .MinimumShouldMatch(1)));
        }

        return filters;
    }

    /// <summary>
    /// Builds ACL filter for chunk search.
    /// </summary>
    public List<Func<QueryContainerDescriptor<ChunkIndexModel>, QueryContainer>> BuildChunkAclFilters(
        string? userId,
        string? userRole,
        List<string>? userAllowedCategories)
    {
        var filters = new List<Func<QueryContainerDescriptor<ChunkIndexModel>, QueryContainer>>();

        // ACL filter for non-admins
        if (userRole != "Admin" && userId != null)
        {
            var cats = userAllowedCategories ?? new List<string>();

            var aclShould = new List<Func<QueryContainerDescriptor<ChunkIndexModel>, QueryContainer>>
            {
                s => s.Term(t => t.Field("accessLevel").Value("open")),
                s => s.Term(t => t.Field("allowedUserIds").Value(userId))
            };

            foreach (var cat in cats)
            {
                var c = cat;
                aclShould.Add(s => s.Term(t => t.Field("category.keyword").Value(c)));
            }

            filters.Add(fq => fq.Bool(b => b
                .Should(aclShould.ToArray())
                .MinimumShouldMatch(1)));
        }
        else if (userRole != "Admin")
        {
            // No user ID — return no results
            filters.Add(fq => fq.Term(t => t.Field("accessLevel").Value("__impossible__")));
        }

        return filters;
    }
}
