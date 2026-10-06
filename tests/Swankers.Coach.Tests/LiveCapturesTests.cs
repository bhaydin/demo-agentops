using Swankers.League;

namespace Swankers.Coach.Tests;

/// <summary>Raw Work IQ captures stay off the checkout by default, and the root can be moved.</summary>
public class LiveCapturesTests
{
    [Fact]
    public void By_default_captures_go_to_local_application_data_and_never_under_the_checkout()
    {
        var checkout = RepoPaths.FindRepoRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("repo root not found");
        var root = LiveCaptures.Root(configured: null);

        Assert.Equal(LiveCaptures.DefaultRoot, root);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), root);
        Assert.EndsWith(Path.Combine("SwankersCoach", "workplace", "live"), root);
        Assert.False(Path.GetFullPath(root).StartsWith(Path.GetFullPath(checkout), StringComparison.OrdinalIgnoreCase), $"{root} is inside the checkout");
    }

    [Fact]
    public void The_root_can_be_overridden_for_troubleshooting()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "swankers-captures", Path.GetRandomFileName());
        try
        {
            Assert.Equal(elsewhere, LiveCaptures.Root(elsewhere));

            var written = LiveCaptures.Write("probe", "structure only", LiveCaptures.Root(elsewhere));

            Assert.Equal(elsewhere, Path.GetDirectoryName(written));
            Assert.Equal("structure only", File.ReadAllText(written));
        }
        finally
        {
            if (Directory.Exists(elsewhere))
            {
                Directory.Delete(elsewhere, recursive: true);
            }
        }
    }
}
