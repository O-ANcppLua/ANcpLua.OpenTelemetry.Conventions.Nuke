using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nuke.Common.IO;

namespace OtelConventions.Nuke;

/// <summary>
/// Internal helpers shared by <see cref="IUpstreamConventions"/> and
/// <see cref="IDomainConventionsApi"/> default-interface target bodies.
/// </summary>
internal static class Helpers
{
    internal static IReadOnlyDictionary<string, string> DeterministicProcessEnv(string? sourceDateEpoch = null)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                env[key] = value;
        }
        env["TZ"] = "UTC";
        env["LC_ALL"] = "C";
        if (!string.IsNullOrWhiteSpace(sourceDateEpoch))
            env["SOURCE_DATE_EPOCH"] = sourceDateEpoch;
        return env;
    }

    internal static string? BytewiseFileDiff(AbsolutePath a, AbsolutePath b)
    {
        if (!File.Exists(a)) return $"left file missing: {a}";
        if (!File.Exists(b)) return $"right file missing: {b}";
        var sizeA = new FileInfo(a).Length;
        var sizeB = new FileInfo(b).Length;
        if (sizeA != sizeB) return $"size mismatch: {a} ({sizeA} bytes) vs {b} ({sizeB} bytes)";
        using var streamA = File.OpenRead(a);
        using var streamB = File.OpenRead(b);
        var hashA = SHA256.HashData(streamA);
        var hashB = SHA256.HashData(streamB);
        return hashA.AsSpan().SequenceEqual(hashB) ? null : $"content differs: {a} vs {b}";
    }

    internal static string? BytewiseDirectoryDiff(AbsolutePath a, AbsolutePath b)
    {
        var filesA = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(a, p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var filesB = Directory.EnumerateFiles(b, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(b, p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        if (!filesA.SequenceEqual(filesB, StringComparer.Ordinal))
        {
            var onlyA = filesA.Except(filesB, StringComparer.Ordinal).FirstOrDefault();
            if (onlyA is not null) return $"only in {a}: {onlyA}";
            var onlyB = filesB.Except(filesA, StringComparer.Ordinal).FirstOrDefault();
            return $"only in {b}: {onlyB}";
        }
        foreach (var rel in filesA)
        {
            var diff = BytewiseFileDiff(a / rel, b / rel);
            if (diff is not null) return diff;
        }
        return null;
    }

    internal static void RewritePackageJsonVersion(AbsolutePath packageJson, string newVersion)
    {
        var originalText = File.ReadAllText(packageJson);
        var hasTrailingNewline = originalText.Length > 0 && originalText[^1] == '\n';

        using var doc = JsonDocument.Parse(originalText);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("version"))
                    writer.WriteString("version", newVersion);
                else
                    prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        var newText = Encoding.UTF8.GetString(ms.ToArray());
        if (hasTrailingNewline && !(newText.Length > 0 && newText[^1] == '\n'))
            newText += '\n';
        File.WriteAllText(packageJson, newText);
    }

    internal static (string Asset, bool IsZip, string BinaryName) WeaverAssetFor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => ("aarch64-apple-darwin", false, "weaver"),
                Architecture.X64 => ("x86_64-apple-darwin", false, "weaver"),
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported macOS architecture: {RuntimeInformation.OSArchitecture}")
            };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => ("x86_64-unknown-linux-gnu", false, "weaver"),
                Architecture.Arm64 => ("aarch64-unknown-linux-gnu", false, "weaver"),
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported Linux architecture: {RuntimeInformation.OSArchitecture}")
            };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => ("x86_64-pc-windows-msvc", true, "weaver.exe"),
                _ => throw new PlatformNotSupportedException(
                    $"Unsupported Windows architecture: {RuntimeInformation.OSArchitecture}")
            };
        throw new PlatformNotSupportedException("Unsupported OS for Weaver download.");
    }

    internal static string ResolveDomainEmitterPackage(string name) => name switch
    {
        "csharp" => "@typespec/http-client-csharp",
        "duckdb" => "@ancplua/typespec-emit-duckdb",
        "ts-types" => "@ancplua/typespec-emit-ts-types",
        "lint" => "@ancplua/typespec-otelconventions-lint",
        _ => throw new ArgumentException(
            $"Unknown emitter '{name}'. Override the matching Emit* target in your Build class to specify the emitter package id.",
            nameof(name))
    };
}
