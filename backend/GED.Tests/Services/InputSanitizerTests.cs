using FluentAssertions;
using GED.Infrastructure.Services;

namespace GED.Tests.Services;

[Trait("Category", "Unit")]
public class InputSanitizerTests
{
    [Fact]
    public void Sanitize_WithNullInput_ReturnsEmptyString()
    {
        InputSanitizer.Sanitize(null).Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_WithEmptyInput_ReturnsEmptyString()
    {
        InputSanitizer.Sanitize("").Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_WithWhitespaceInput_ReturnsEmptyString()
    {
        InputSanitizer.Sanitize("   ").Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_WithNormalText_ReturnsSameText()
    {
        var result = InputSanitizer.Sanitize("Hello World");
        result.Should().Be("Hello World");
    }

    [Fact]
    public void Sanitize_WithScriptTag_RemovesScript()
    {
        var result = InputSanitizer.Sanitize("<script>alert('xss')</script>Hello");
        result.Should().NotContain("<script>");
        result.Should().NotContain("alert");
    }

    [Fact]
    public void Sanitize_WithHtmlTags_RemovesHtml()
    {
        var result = InputSanitizer.Sanitize("<b>Hello</b> <div>World</div>");
        result.Should().NotContain("<b>");
        result.Should().NotContain("<div>");
        result.Should().Contain("Hello");
        result.Should().Contain("World");
    }

    [Fact]
    public void Sanitize_WithEventHandler_RemovesEventHandler()
    {
        var result = InputSanitizer.Sanitize("<img onerror=\"alert(1)\" src=x>");
        result.Should().NotContain("onerror");
    }

    [Fact]
    public void SanitizeSearchQuery_WithNull_ReturnsEmpty()
    {
        InputSanitizer.SanitizeSearchQuery(null).Should().BeEmpty();
    }

    [Fact]
    public void SanitizeSearchQuery_WithSpecialChars_EscapesThem()
    {
        var result = InputSanitizer.SanitizeSearchQuery("test\"quote");
        result.Should().Contain("\\\"");
    }

    [Fact]
    public void SanitizeSearchQuery_WithNewlines_ReplacesWithSpace()
    {
        var result = InputSanitizer.SanitizeSearchQuery("test\nquery\rwith\ttabs");
        result.Should().NotContain("\n");
        result.Should().NotContain("\r");
        result.Should().NotContain("\t");
    }

    [Fact]
    public void SanitizeSearchQuery_WithLongInput_TruncatesTo1000()
    {
        var longInput = new string('a', 2000);
        var result = InputSanitizer.SanitizeSearchQuery(longInput);
        result.Length.Should().BeLessOrEqualTo(1000);
    }

    [Fact]
    public void SanitizeFileName_WithNull_ReturnsUnnamedFile()
    {
        InputSanitizer.SanitizeFileName(null).Should().Be("unnamed_file");
    }

    [Fact]
    public void SanitizeFileName_WithEmpty_ReturnsUnnamedFile()
    {
        InputSanitizer.SanitizeFileName("").Should().Be("unnamed_file");
    }

    [Fact]
    public void SanitizeFileName_WithPathTraversal_RemovesPath()
    {
        var result = InputSanitizer.SanitizeFileName("/etc/passwd");
        result.Should().NotContain("..");
        result.Should().NotStartWith("/");
    }

    [Fact]
    public void SanitizeFileName_WithInvalidChars_ReplacesThem()
    {
        var result = InputSanitizer.SanitizeFileName("test:file?.pdf");
        result.Should().NotContain(":");
        result.Should().NotContain("?");
    }

    [Fact]
    public void SanitizeFileName_WithLongName_Truncates()
    {
        var longName = new string('a', 300) + ".pdf";
        var result = InputSanitizer.SanitizeFileName(longName);
        result.Length.Should().BeLessOrEqualTo(204);
    }

    [Fact]
    public void SanitizeCategory_WithNull_ReturnsEmpty()
    {
        InputSanitizer.SanitizeCategory(null).Should().BeEmpty();
    }

    [Fact]
    public void SanitizeCategory_WithSpecialChars_RemovesThem()
    {
        var result = InputSanitizer.SanitizeCategory("Invoice!@#$%");
        result.Should().NotContain("!");
        result.Should().NotContain("@");
        result.Should().Contain("Invoice");
    }

    [Fact]
    public void SanitizeCategory_WithValidCategory_ReturnsSame()
    {
        var result = InputSanitizer.SanitizeCategory("Invoice-Contract_Report");
        result.Should().Be("Invoice-Contract_Report");
    }

    [Fact]
    public void SanitizeTags_WithNull_ReturnsEmptyList()
    {
        InputSanitizer.SanitizeTags(null).Should().BeEmpty();
    }

    [Fact]
    public void SanitizeTags_WithValidTags_ReturnsSanitized()
    {
        var tags = new List<string> { "  invoice  ", "contract", "report " };
        var result = InputSanitizer.SanitizeTags(tags);
        result.Should().Contain("invoice");
        result.Should().Contain("contract");
        result.Should().Contain("report");
    }

    [Fact]
    public void SanitizeTags_WithHtmlTags_RemovesHtml()
    {
        var tags = new List<string> { "<script>alert(1)</script>" };
        var result = InputSanitizer.SanitizeTags(tags);
        result.Should().BeEmpty();
    }

    [Fact]
    public void SanitizeTags_WithLongTag_ExcludesIt()
    {
        var tags = new List<string> { new string('a', 100) };
        var result = InputSanitizer.SanitizeTags(tags);
        result.Should().BeEmpty();
    }

    [Fact]
    public void SanitizeTags_WithDuplicates_RemovesDuplicates()
    {
        var tags = new List<string> { "invoice", "Invoice", "INVOICE" };
        var result = InputSanitizer.SanitizeTags(tags);
        result.Should().HaveCount(1);
    }

    [Fact]
    public void SanitizeTags_WithManyTags_LimitsTo20()
    {
        var tags = Enumerable.Range(1, 30).Select(i => $"tag{i}").ToList();
        var result = InputSanitizer.SanitizeTags(tags);
        result.Should().HaveCount(20);
    }
}