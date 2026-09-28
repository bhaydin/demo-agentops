using Swankers.Coach.Knowledge;
using Swankers.League;

namespace Swankers.Coach.Tests;

/// <summary>Runs against the real knowledge/ folder in the repo.</summary>
public class KnowledgeSearchTests
{
    private static KnowledgeSearch Load()
    {
        var root = RepoPaths.FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("repo root not found");
        return KnowledgeSearch.Load(Path.Combine(root, "knowledge"));
    }

    [Fact]
    public void Indexes_every_document_except_the_readme()
    {
        var knowledge = Load();

        Assert.True(knowledge.SectionCount >= 15, $"only {knowledge.SectionCount} sections");
        Assert.DoesNotContain(knowledge.Search("Foundry IQ grounding upload"), h => h.Source.Equals("README.md", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("when is the trade deadline", "trades-and-waivers.md")]
    [InlineData("how many starters in the starting lineup", "roster-and-lineup-rules.md")]
    [InlineData("what does Questionable mean on the injury report", "glossary.md")]
    [InlineData("Anchorage Falling roster draft notes", "draft-notes-anchorage-falling.md")]
    [InlineData("week 1 results standings", "weekly-digest.md")]
    public void Finds_the_right_document(string query, string expectedSource)
    {
        var hits = Load().Search(query);

        Assert.NotEmpty(hits);
        Assert.Equal(expectedSource, hits[0].Source);
    }

    [Fact]
    public void Empty_or_stopword_only_queries_return_nothing()
    {
        var knowledge = Load();

        Assert.Empty(knowledge.Search(""));
        Assert.Empty(knowledge.Search("what is the"));
    }

    [Fact]
    public void Knowledge_documents_ship_next_to_the_binaries()
    {
        // Codex Phase 3 review #4: a published artifact must not depend on the checkout.
        var shipped = Path.Combine(AppContext.BaseDirectory, "knowledge");

        Assert.True(Directory.Exists(shipped), $"missing {shipped}");
        Assert.True(Directory.EnumerateFiles(shipped, "*.md").Count() >= 6);
        Assert.DoesNotContain(Directory.EnumerateFiles(shipped), f => Path.GetFileName(f) == "README.md");
        Assert.True(KnowledgeSearch.Load(shipped).SectionCount >= 15);
    }

    [Fact]
    public void Exposes_the_search_tool_under_its_stable_name()
    {
        var tool = Load().AsTool();

        Assert.Equal("search_league_knowledge", tool.Name);
        Assert.Contains("league", tool.Description, StringComparison.OrdinalIgnoreCase);
    }
}
