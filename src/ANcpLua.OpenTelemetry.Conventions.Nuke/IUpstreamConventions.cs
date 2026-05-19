using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.Npm;
using Serilog;

namespace ANcpLua.OpenTelemetry.Conventions.Nuke;

/// <summary>
/// Nuke build component implemented by the upstream Weaver-based generator repository
/// (<c>ANcpLua/typespec-otel-semconv</c>, publishing
/// <c>@ancplua/typespec-otel-semconv@{semconv}-{n}</c> to GitHub Packages npm).
/// </summary>
/// <remarks>
/// <para>
/// This interface encodes the lockstep + reproducibility policy that the generator
/// pipeline must enforce. It is consumed by the upstream repo as a Nuke component:
/// </para>
/// <code>
/// class Build : NukeBuild, IUpstreamConventions
/// {
///     public static int Main() =&gt; Execute&lt;Build&gt;(x =&gt; ((IUpstreamConventions)x).GenerateOtelKeys);
/// }
/// </code>
/// <para>
/// Targets ship with working default-interface bodies that enforce the lockstep,
/// reproducibility, smoke-compile, pack, and publish policy this package contributes.
/// Consumer <c>Build</c> classes may override any individual target via the standard
/// Nuke component-target override pattern when project-local specifics (custom Weaver
/// templates, alternate fetch source) require it; otherwise the defaults are picked up
/// unchanged.
/// </para>
/// </remarks>
public interface IUpstreamConventions : INukeBuild
{
    /// <summary>
    /// Upstream OpenTelemetry semantic-conventions release tag to mirror, without the
    /// leading <c>v</c>. Default is the current pinned release.
    /// </summary>
    /// <remarks>
    /// This is the pinned upstream release (e.g. <c>open-telemetry/semantic-conventions@v1.41.0</c>),
    /// NOT the declarative version-selection integer at
    /// <c>.instrumentation/development.general.&lt;domain&gt;.semconv</c>.
    /// </remarks>
    [Parameter("Upstream OpenTelemetry semantic-conventions release tag to pin (e.g. '1.41.0').")]
    string SemconvVersion => TryGetValue(() => SemconvVersion) ?? "1.41.0";

    /// <summary>Pinned Weaver CLI version used to drive code generation.</summary>
    [Parameter("Pinned OpenTelemetry Weaver CLI version (must be set explicitly to keep generation reproducible).")]
    string WeaverVersion => TryGetValue(() => WeaverVersion)!;

    /// <summary>
    /// Path of the committed generated TypeSpec output that ships in the package's
    /// <c>lib/</c> directory. Defaults to <c>lib/otel-keys.tsp</c>.
    /// </summary>
    [Parameter("Path to the committed generated TypeSpec keys file (default: ./lib/otel-keys.tsp).")]
    AbsolutePath OtelKeysOutput => TryGetValue(() => OtelKeysOutput) ?? RootDirectory / "lib" / "otel-keys.tsp";

    /// <summary>SemVer range of <c>@typespec/compiler</c> the generated output must compile against.</summary>
    [Parameter("Accepted @typespec/compiler SemVer range for smoke compilation.")]
    string TypeSpecCompilerRange => TryGetValue(() => TypeSpecCompilerRange)!;

    /// <summary>
    /// Monotonic generator-revision counter appended to the published npm version as
    /// <c>{SemconvVersion}-{PackageVersionSuffix}</c>. Defaults to <c>1</c>.
    /// </summary>
    [Parameter("Generator-revision counter for the {semconv}-{n} npm version suffix.")]
    int PackageVersionSuffix => ParameterDefaults.GetOrDefault(this, () => PackageVersionSuffix, 1);

    /// <summary>Whether reproducibility / script-parity drift is a build failure (default <c>true</c>).</summary>
    [Parameter("Fail the build if reproducibility or script-parity verification detects drift.")]
    bool FailOnDrift => ParameterDefaults.GetOrDefault(this, () => FailOnDrift, true);

    /// <summary>
    /// Restore the pinned Weaver binary into <c>~/.nuke/temp/weaver/{WeaverVersion}</c>.
    /// </summary>
    Target RestoreWeaver => _ => _
        .Requires(() => WeaverVersion)
        .Executes(() =>
        {
            var (archAsset, isZip, binaryName) = Helpers.WeaverAssetFor();
            var cacheRoot = (AbsolutePath)Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                            / ".nuke" / "temp" / "weaver" / WeaverVersion;
            var weaverBinPath = cacheRoot / $"weaver-{archAsset}" / binaryName;
            if (File.Exists(weaverBinPath))
            {
                Log.Information("RestoreWeaver: cached at {Path}", weaverBinPath);
                return;
            }

            cacheRoot.CreateDirectory();
            var ext = isZip ? "zip" : "tar.xz";
            var assetUri = $"https://github.com/open-telemetry/weaver/releases/download/v{WeaverVersion}/weaver-{archAsset}.{ext}";
            var shaUri = $"{assetUri}.sha256";
            var archivePath = cacheRoot / $"weaver-{archAsset}.{ext}";
            var shaPath = cacheRoot / $"weaver-{archAsset}.{ext}.sha256";

            Log.Information("RestoreWeaver: downloading {Uri}", assetUri);
            HttpTasks.HttpDownloadFile(assetUri, archivePath);
            HttpTasks.HttpDownloadFile(shaUri, shaPath);

            // .sha256 format is "<hex-digest>  weaver-<arch>.<ext>"; take the first whitespace token.
            var shaText = File.ReadAllText(shaPath).AsSpan().Trim();
            var sepIdx = shaText.IndexOfAny(' ', '\t');
            var digestSpan = sepIdx >= 0 ? shaText[..sepIdx] : shaText;
            var expectedDigest = digestSpan.ToString().ToLowerInvariant();
            string actualDigest;
            using (var stream = File.OpenRead(archivePath))
            {
                actualDigest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }

            if (!string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"RestoreWeaver: SHA-256 mismatch for {assetUri}. Expected '{expectedDigest}', got '{actualDigest}'.");

            if (isZip)
            {
                ZipFile.ExtractToDirectory(archivePath, cacheRoot);
            }
            else
            {
                ProcessTasks.StartProcess("tar", $"-xf \"{archivePath}\" -C \"{cacheRoot}\"")
                    .AssertZeroExitCode();
            }

            File.Delete(archivePath);
            File.Delete(shaPath);

            if (!File.Exists(weaverBinPath))
                throw new InvalidOperationException(
                    $"RestoreWeaver: expected binary at {weaverBinPath} after extracting {assetUri}, but file was not found.");

            Log.Information("RestoreWeaver: extracted to {Path}", weaverBinPath);
        });

    /// <summary>
    /// Check out <c>open-telemetry/semantic-conventions@v{SemconvVersion}</c> and
    /// recursively materialise all <c>model/**/*.yaml</c> and <c>model/**/*.yml</c>
    /// files (so e.g. <c>model/graphql/spans.yml</c> is included).
    /// </summary>
    Target FetchSemconvModel => _ => _
        .Requires(() => SemconvVersion)
        .Executes(() =>
        {
            var clonePath = TemporaryDirectory / "semconv" / SemconvVersion;
            var modelRoot = clonePath / "model";

            if (!Directory.Exists(modelRoot))
            {
                if (Directory.Exists(clonePath))
                    clonePath.DeleteDirectory();
                clonePath.Parent!.CreateDirectory();
                Log.Information("FetchSemconvModel: cloning open-telemetry/semantic-conventions@v{Version} into {Path}",
                    SemconvVersion, clonePath);
                GitTasks.Git(
                    $"clone --depth 1 --branch v{SemconvVersion} https://github.com/open-telemetry/semantic-conventions \"{clonePath}\"");
            }
            else
            {
                Log.Information("FetchSemconvModel: reusing cached checkout at {Path}", clonePath);
            }

            if (!Directory.Exists(modelRoot))
                throw new InvalidOperationException(
                    $"FetchSemconvModel: expected '{modelRoot}' to exist after clone of v{SemconvVersion}.");

            var yamlFiles = Directory.EnumerateFiles(modelRoot, "*.yaml", SearchOption.AllDirectories);
            var ymlFiles = Directory.EnumerateFiles(modelRoot, "*.yml", SearchOption.AllDirectories);
            var totalCount = 0;
            foreach (var _ in yamlFiles) totalCount++;
            foreach (var _ in ymlFiles) totalCount++;

            Log.Information("FetchSemconvModel: {Count} model files (yaml + yml) under {Path}", totalCount, modelRoot);
        });

    /// <summary>
    /// Invoke Weaver against the materialised model and write the generated TypeSpec
    /// to <see cref="OtelKeysOutput"/>.
    /// </summary>
    Target GenerateOtelKeys => _ => _
        .DependsOn(RestoreWeaver, FetchSemconvModel)
        .Requires(() => WeaverVersion, () => SemconvVersion)
        .Executes(() =>
        {
            var (archAsset, _, binaryName) = Helpers.WeaverAssetFor();
            var weaverBin = (AbsolutePath)Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                            / ".nuke" / "temp" / "weaver" / WeaverVersion / $"weaver-{archAsset}" / binaryName;
            if (!File.Exists(weaverBin))
                throw new InvalidOperationException(
                    $"GenerateOtelKeys: weaver binary not found at {weaverBin}. RestoreWeaver should have produced it.");

            var clonePath = TemporaryDirectory / "semconv" / SemconvVersion;
            var modelRoot = clonePath / "model";
            var templatesRoot = RootDirectory / "templates" / "registry";
            var templateTarget = "typespec";
            var stagingDir = TemporaryDirectory / "weaver" / Guid.NewGuid().ToString("N")[..8];
            stagingDir.CreateOrCleanDirectory();

            // Derive SOURCE_DATE_EPOCH from the pinned upstream tag's committer date so
            // Weaver's deterministic-output guarantee is honoured even if it ever grows
            // a time-sensitive code path.
            var epoch = GitTasks.Git(
                    $"log -1 --format=%ct refs/tags/v{SemconvVersion}",
                    workingDirectory: clonePath,
                    logOutput: false)
                .FirstOrDefault().Text?.Trim();
            var env = Helpers.DeterministicProcessEnv(epoch);

            ProcessTasks.StartProcess(
                    weaverBin,
                    $"registry generate --registry \"{modelRoot}\" --templates \"{templatesRoot}\" {templateTarget} \"{stagingDir}\"",
                    environmentVariables: env)
                .AssertZeroExitCode();

            var stagedFile = stagingDir / OtelKeysOutput.Name;
            if (!File.Exists(stagedFile))
                throw new InvalidOperationException(
                    $"GenerateOtelKeys: weaver did not produce expected file '{OtelKeysOutput.Name}' in {stagingDir}. " +
                    $"Check that templates/registry/{templateTarget}/weaver.yaml emits this filename.");

            OtelKeysOutput.Parent!.CreateDirectory();
            File.Copy(stagedFile, OtelKeysOutput, overwrite: true);
            Log.Information("GenerateOtelKeys: wrote {Path}", OtelKeysOutput);
        });

    /// <summary>
    /// Regenerate the keys into a scratch directory and diff against the committed
    /// <see cref="OtelKeysOutput"/>. Fails when <see cref="FailOnDrift"/> is <c>true</c>
    /// and a difference is detected.
    /// </summary>
    Target VerifyOtelKeysReproducible => _ => _
        .DependsOn(GenerateOtelKeys)
        .Executes(() =>
        {
            var (archAsset, _, binaryName) = Helpers.WeaverAssetFor();
            var weaverBin = (AbsolutePath)Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                            / ".nuke" / "temp" / "weaver" / WeaverVersion / $"weaver-{archAsset}" / binaryName;
            var clonePath = TemporaryDirectory / "semconv" / SemconvVersion;
            var modelRoot = clonePath / "model";
            var templatesRoot = RootDirectory / "templates" / "registry";
            var scratchDir = TemporaryDirectory / "repro" / Guid.NewGuid().ToString("N")[..8];
            scratchDir.CreateOrCleanDirectory();

            var epoch = GitTasks.Git(
                    $"log -1 --format=%ct refs/tags/v{SemconvVersion}",
                    workingDirectory: clonePath,
                    logOutput: false)
                .FirstOrDefault().Text?.Trim();
            var env = Helpers.DeterministicProcessEnv(epoch);

            ProcessTasks.StartProcess(
                    weaverBin,
                    $"registry generate --registry \"{modelRoot}\" --templates \"{templatesRoot}\" typespec \"{scratchDir}\"",
                    environmentVariables: env)
                .AssertZeroExitCode();

            var reproFile = scratchDir / OtelKeysOutput.Name;
            var diff = Helpers.BytewiseFileDiff(OtelKeysOutput, reproFile);
            if (diff is null)
            {
                Log.Information("VerifyOtelKeysReproducible: byte-identical regeneration of {Path}", OtelKeysOutput);
                return;
            }

            var msg = $"VerifyOtelKeysReproducible: regeneration drift detected — {diff}";
            if (FailOnDrift)
                throw new InvalidOperationException(msg);
            Log.Warning(msg);
        });

    /// <summary>
    /// Invoke both <c>scripts/generate.mjs</c> and its PowerShell sibling (if present)
    /// and assert the outputs are byte-identical, guarding against script drift.
    /// </summary>
    Target VerifyOtelKeysScriptParity => _ => _
        .Executes(() =>
        {
            var mjsScript = RootDirectory / "scripts" / "generate.mjs";
            if (!File.Exists(mjsScript))
            {
                Log.Information("VerifyOtelKeysScriptParity: {Path} not present, skipping parity.", mjsScript);
                return;
            }

            var ps1Script = RootDirectory / "scripts" / "generate.ps1";
            if (!File.Exists(ps1Script))
            {
                Log.Information("VerifyOtelKeysScriptParity: PowerShell sibling not present, skipping parity.");
                return;
            }

            var dirMjs = TemporaryDirectory / "parity-mjs" / Guid.NewGuid().ToString("N")[..8];
            var dirPs1 = TemporaryDirectory / "parity-ps1" / Guid.NewGuid().ToString("N")[..8];
            dirMjs.CreateOrCleanDirectory();
            dirPs1.CreateOrCleanDirectory();

            ProcessTasks.StartProcess(
                    "node",
                    $"\"{mjsScript}\" \"{dirMjs}\"",
                    workingDirectory: RootDirectory)
                .AssertZeroExitCode();
            ProcessTasks.StartProcess(
                    "pwsh",
                    $"-File \"{ps1Script}\" \"{dirPs1}\"",
                    workingDirectory: RootDirectory)
                .AssertZeroExitCode();

            var diff = Helpers.BytewiseDirectoryDiff(dirMjs, dirPs1);
            if (diff is null)
            {
                Log.Information("VerifyOtelKeysScriptParity: .mjs and .ps1 outputs are byte-identical.");
                return;
            }

            var msg = $"VerifyOtelKeysScriptParity: script drift — {diff}";
            if (FailOnDrift)
                throw new InvalidOperationException(msg);
            Log.Warning(msg);
        });

    /// <summary>
    /// Run <c>tsp compile test/smoke.tsp --no-emit --warn-as-error</c> against the
    /// generated output to confirm the keys file is syntactically and semantically valid.
    /// </summary>
    Target VerifyOtelKeysCompile => _ => _
        .DependsOn(GenerateOtelKeys)
        .Executes(() =>
        {
            NpmTasks.Npm(
                "exec --no -- tsp compile test/smoke.tsp --no-emit --warn-as-error",
                workingDirectory: RootDirectory);
        });

    /// <summary>Run <c>npm run test</c> for the generator package.</summary>
    Target RunSmokeTests => _ => _
        .DependsOn(GenerateOtelKeys)
        .Executes(() =>
        {
            NpmTasks.NpmRun(s => s
                .SetCommand("test")
                .SetProcessWorkingDirectory(RootDirectory));
        });

    /// <summary>
    /// Wrap the existing <c>verify-clean</c> npm script, which regenerates and runs
    /// <c>git diff --exit-code -- lib/ src/generated/</c> to ensure no manual edits.
    /// </summary>
    Target VerifyClean => _ => _
        .Executes(() =>
        {
            NpmTasks.NpmRun(s => s
                .SetCommand("verify-clean")
                .SetProcessWorkingDirectory(RootDirectory));
        });

    /// <summary>Run <c>npm pack</c> to produce the GitHub Packages tarball.</summary>
    Target PackTypeSpecLibrary => _ => _
        .DependsOn(VerifyOtelKeysReproducible, VerifyOtelKeysCompile, RunSmokeTests, VerifyClean)
        .Requires(() => SemconvVersion)
        .Executes(() =>
        {
            var packageJson = RootDirectory / "package.json";
            if (!File.Exists(packageJson))
                throw new InvalidOperationException(
                    $"PackTypeSpecLibrary: {packageJson} not found.");

            var newVersion = $"{SemconvVersion}-{PackageVersionSuffix}";
            Helpers.RewritePackageJsonVersion(packageJson, newVersion);
            Log.Information("PackTypeSpecLibrary: package.json version → {Version}", newVersion);

            NpmTasks.Npm("pack", workingDirectory: RootDirectory);
        });

    /// <summary>
    /// Run <c>npm publish --provenance</c> against the GitHub Packages npm registry
    /// for the <c>@ancplua</c> scope.
    /// </summary>
    Target PublishTypeSpecLibrary => _ => _
        .DependsOn(PackTypeSpecLibrary)
        .Requires(() => WeaverVersion)
        .Executes(() =>
        {
            var token = Environment.GetEnvironmentVariable("NODE_AUTH_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "PublishTypeSpecLibrary: NODE_AUTH_TOKEN env var is required " +
                    "(GitHub Packages npm token with write:packages scope).");

            NpmTasks.Npm(
                "publish --access public --provenance --registry=https://npm.pkg.github.com",
                workingDirectory: RootDirectory);
        });
}
