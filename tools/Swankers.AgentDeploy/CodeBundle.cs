using System.IO.Compression;
using System.Security.Cryptography;

namespace Swankers.AgentDeploy;

/// <summary>
/// Builds the code zip the way the service expects: publish output flat at the root, entry
/// names with forward slashes. The SDK's folder upload writes Windows separators into the
/// entry names ("prompts\coach-v1.md"), which unpack on Linux as single root files, so the
/// hosted Coach could not find its prompts or knowledge.
/// </summary>
public static class CodeBundle
{
    public static (BinaryData Zip, string Sha256Hex) Create(string directory)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var entryName = Path.GetRelativePath(directory, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            }
        }

        var bytes = buffer.ToArray();
        return (BinaryData.FromBytes(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
