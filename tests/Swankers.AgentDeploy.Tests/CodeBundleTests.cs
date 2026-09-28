using System.IO.Compression;
using System.Security.Cryptography;
using Swankers.AgentDeploy;

namespace Swankers.AgentDeploy.Tests;

/// <summary>
/// The hosted runtime unpacks the bundle on Linux: entry names must use '/', the publish
/// output must sit at the zip root, and the tool's own marker file must not ship.
/// </summary>
public sealed class CodeBundleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "swankers-agentdeploy-tests", Guid.NewGuid().ToString("N"));

    public CodeBundleTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "prompts"));
        Directory.CreateDirectory(Path.Combine(_dir, "knowledge"));
        File.WriteAllText(Path.Combine(_dir, "Swankers.Coach.dll"), "not really a dll");
        File.WriteAllText(Path.Combine(_dir, "prompts", "coach-v1.md"), "# v1");
        File.WriteAllText(Path.Combine(_dir, "knowledge", "glossary.md"), "# glossary");
        File.WriteAllText(Path.Combine(_dir, CoachPublisher.OwnershipMarker), "marker");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Entries_are_flat_with_forward_slashes_and_exclude_the_marker()
    {
        var (zip, sha256) = CodeBundle.Create(_dir);

        using var archive = new ZipArchive(zip.ToStream(), ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Equal(["Swankers.Coach.dll", "knowledge/glossary.md", "prompts/coach-v1.md"], names);
        Assert.DoesNotContain(names, n => n.Contains('\\'));
        Assert.DoesNotContain(CoachPublisher.OwnershipMarker, names);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(zip.ToArray())), sha256);
    }
}
