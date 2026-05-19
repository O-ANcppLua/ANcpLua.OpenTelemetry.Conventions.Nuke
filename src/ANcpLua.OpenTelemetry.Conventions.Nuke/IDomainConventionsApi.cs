using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.Npm;
using Serilog;

namespace ANcpLua.OpenTelemetry.Conventions.Nuke;

/// <summary>
/// Nuke build component implemented by the downstream API-surface repository
/// (<c>O-ANcppLua/ANcpLua.OtelConventions.Api</c>, publishing
/// <c>@o-ancpplua/otel-conventions-api</c> to GitHub Packages npm).
/// </summary>
/// <remarks>
/// <para>
/// This interface enforces the lockstep guarantee from the consumer side: the
/// downstream API package must consume an exact pinned version of
/// <c>@ancplua/typespec-otel-semconv</c> and must not allow manual edits to
/// generated files. Multiple emitters (C#, DuckDB, TypeScript types, lint)
/// run from the same generated input.
/// </para>
/// <code>
/// class Build : NukeBuild, IDomainConventionsApi
/// {
///     public static int Main() =&gt; Execute&lt;Build&gt;(x =&gt; ((IDomainConventionsApi)x).EmitAll);
/// }
/// </code>
/// <para>
/// Targets ship with working default-interface bodies that enforce the lockstep,
/// determinism, manual-edit, and pack-publish policy this package contributes.
/// Consumer <c>Build</c> classes may override any individual target via the standard
/// Nuke component-target override pattern when project-local specifics (custom emitter
/// package ids, alternate output layouts) require it; otherwise the defaults are
/// picked up unchanged.
/// </para>
/// </remarks>
public interface IDomainConventionsApi : INukeBuild
{
    /// <summary>npm package name of the upstream keys provider.</summary>
    [Parameter("npm package name of the upstream keys provider (default: '@ancplua/typespec-otel-semconv').")]
    string OtelKeysPackage => TryGetValue(() => OtelKeysPackage) ?? "@ancplua/typespec-otel-semconv";

    /// <summary>
    /// Exact lockstep version (<c>{semconv}-{n}</c>, e.g. <c>1.41.0-3</c>) of
    /// <see cref="OtelKeysPackage"/> the downstream package must pin in
    /// <c>package-lock.json</c>.
    /// </summary>
    [Parameter("Exact lockstep version ({semconv}-{n}) of the upstream keys package.")]
    string OtelKeysVersion => TryGetValue(() => OtelKeysVersion)!;

    /// <summary>
    /// Set of emitters to run during <c>EmitAll</c>. Defaults to
    /// <c>csharp</c>, <c>duckdb</c>, <c>ts-types</c>, <c>lint</c>.
    /// </summary>
    [Parameter("Emitter names to invoke (default: csharp, duckdb, ts-types, lint).")]
    string[] Emitters => TryGetValue(() => Emitters) ?? new[] { "csharp", "duckdb", "ts-types", "lint" };

    /// <summary>Root directory under which all emitters write their output.</summary>
    [Parameter("Root directory for all emitter outputs (default: ./emitters).")]
    AbsolutePath EmitOutputDir => TryGetValue(() => EmitOutputDir) ?? RootDirectory / "emitters";

    /// <summary>Whether emit-reproducibility drift is a build failure (default <c>true</c>).</summary>
    [Parameter("Fail the build if emit-reproducibility verification detects drift.")]
    bool EmitFailOnDrift => ParameterDefaults.GetOrDefault(this, () => EmitFailOnDrift, true);

    /// <summary>Root of the downstream TypeSpec domain spec (i.e. directory containing <c>index.tsp</c>).</summary>
    [Parameter("Root directory of the downstream TypeSpec domain spec (default: RootDirectory).")]
    AbsolutePath DomainSpecRoot => TryGetValue(() => DomainSpecRoot) ?? RootDirectory;

    /// <summary>Run <c>npm ci</c> to restore TypeSpec dependencies from the lockfile.</summary>
    Target RestoreTypeSpecDeps => _ => _
        .Executes(() =>
        {
            NpmTasks.NpmCi(s => s.SetProcessWorkingDirectory(DomainSpecRoot));
        });

    /// <summary>
    /// Assert that the resolved version of <see cref="OtelKeysPackage"/> in
    /// <c>package-lock.json</c> exactly equals <see cref="OtelKeysVersion"/>, AND
    /// that <c>generated/otel-keys.gen.tsp</c> matches the upstream package's
    /// shipped <c>lib/otel-keys.tsp</c> byte-for-byte.
    /// </summary>
    Target VerifyKeysLockstep => _ => _
        .DependsOn(RestoreTypeSpecDeps)
        .Requires(() => OtelKeysVersion)
        .Executes(() =>
        {
            // Validate the OtelKeysVersion format defensively; if it does not parse,
            // surface a clear lockstep-policy violation before any further checks.
            var parsedVersion = LockstepPolicy.ParseSemconvSuffixVersion(OtelKeysVersion);
            Log.Debug("VerifyKeysLockstep: OtelKeysVersion '{Version}' parsed as semconv={Semconv} n={N}.",
                OtelKeysVersion, parsedVersion.Semconv, parsedVersion.N);

            var lockfile = DomainSpecRoot / "package-lock.json";
            if (!File.Exists(lockfile))
                throw new InvalidOperationException(
                    $"VerifyKeysLockstep: {lockfile} not found. Run RestoreTypeSpecDeps first.");

            string? resolvedVersion = null;
            using (var doc = JsonDocument.Parse(File.ReadAllText(lockfile)))
            {
                if (doc.RootElement.TryGetProperty("packages", out var packages))
                {
                    var key = $"node_modules/{OtelKeysPackage}";
                    if (packages.TryGetProperty(key, out var entry) &&
                        entry.TryGetProperty("version", out var versionElement))
                    {
                        resolvedVersion = versionElement.GetString();
                    }
                }
            }

            if (resolvedVersion is null)
                throw new InvalidOperationException(
                    $"VerifyKeysLockstep: could not locate 'packages[\"node_modules/{OtelKeysPackage}\"].version' " +
                    $"in {lockfile}. Is the upstream package installed?");

            if (!string.Equals(resolvedVersion, OtelKeysVersion, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"VerifyKeysLockstep: version drift — package-lock pins {OtelKeysPackage}@{resolvedVersion}, " +
                    $"but OtelKeysVersion requires exactly {OtelKeysVersion}. " +
                    "Update package-lock or change OtelKeysVersion; this is an exact-equality check.");

            var shippedKeys = DomainSpecRoot / "node_modules" / OtelKeysPackage / "lib" / "otel-keys.tsp";
            var committedKeys = DomainSpecRoot / "generated" / "otel-keys.gen.tsp";

            if (!File.Exists(shippedKeys))
                throw new InvalidOperationException(
                    $"VerifyKeysLockstep: upstream keys file not found at {shippedKeys}.");
            if (!File.Exists(committedKeys))
                throw new InvalidOperationException(
                    $"VerifyKeysLockstep: committed keys file not found at {committedKeys}.");

            var diff = Helpers.BytewiseFileDiff(shippedKeys, committedKeys);
            if (diff is not null)
                throw new InvalidOperationException(
                    $"VerifyKeysLockstep: keys-file drift — {diff}. " +
                    $"Regenerate {committedKeys} from {shippedKeys} (do not hand-edit).");

            Log.Information("VerifyKeysLockstep: {Package}@{Version} pinned exactly, keys byte-identical.",
                OtelKeysPackage, OtelKeysVersion);
        });

    /// <summary>
    /// Run <c>tsp compile {DomainSpecRoot}/index.tsp --no-emit --warn-as-error</c>
    /// against the full domain spec.
    /// </summary>
    Target CompileDomainSpec => _ => _
        .DependsOn(VerifyKeysLockstep)
        .Executes(() =>
        {
            NpmTasks.Npm(
                "exec --no -- tsp compile index.tsp --no-emit --warn-as-error",
                workingDirectory: DomainSpecRoot);
        });

    /// <summary>Run the C# emitter, writing under <c>{EmitOutputDir}/csharp</c>.</summary>
    Target EmitCSharp => _ => _
        .DependsOn(CompileDomainSpec)
        .Executes(() => RunDomainEmitter(this, "csharp"));

    /// <summary>Run the DuckDB emitter, writing under <c>{EmitOutputDir}/duckdb</c>.</summary>
    Target EmitDuckDb => _ => _
        .DependsOn(CompileDomainSpec)
        .Executes(() => RunDomainEmitter(this, "duckdb"));

    /// <summary>Run the TypeScript types emitter, writing under <c>{EmitOutputDir}/ts-types</c>.</summary>
    Target EmitTsTypes => _ => _
        .DependsOn(CompileDomainSpec)
        .Executes(() => RunDomainEmitter(this, "ts-types"));

    /// <summary>Run the lint emitter / conventions linter, writing under <c>{EmitOutputDir}/lint</c>.</summary>
    Target LintConventions => _ => _
        .DependsOn(CompileDomainSpec)
        .Executes(() => RunDomainEmitter(this, "lint"));

    /// <summary>
    /// Aggregate target that runs every emitter in <see cref="Emitters"/>: by default
    /// <see cref="EmitCSharp"/>, <see cref="EmitDuckDb"/>, <see cref="EmitTsTypes"/>,
    /// and <see cref="LintConventions"/>.
    /// </summary>
    Target EmitAll => _ => _
        .DependsOn(EmitCSharp, EmitDuckDb, EmitTsTypes, LintConventions)
        .Executes(() =>
        {
            Log.Information("EmitAll: dependency graph complete.");
        });

    /// <summary>
    /// Emit twice into separate temporary directories and diff them to guarantee
    /// emitters are deterministic. Fails when <see cref="EmitFailOnDrift"/> is <c>true</c>.
    /// </summary>
    Target VerifyEmitDeterministic => _ => _
        .DependsOn(VerifyKeysLockstep)
        .Executes(() =>
        {
            foreach (var name in Emitters)
            {
                var pkg = Helpers.ResolveDomainEmitterPackage(name);
                var dirA = TemporaryDirectory / $"emit-{name}-A" / Guid.NewGuid().ToString("N")[..8];
                var dirB = TemporaryDirectory / $"emit-{name}-B" / Guid.NewGuid().ToString("N")[..8];
                dirA.CreateOrCleanDirectory();
                dirB.CreateOrCleanDirectory();

                var env = Helpers.DeterministicProcessEnv();
                NpmTasks.Npm(
                    $"exec --no -- tsp compile index.tsp --emit \"{pkg}\" --output-dir \"{dirA}\"",
                    workingDirectory: DomainSpecRoot,
                    environmentVariables: env);
                NpmTasks.Npm(
                    $"exec --no -- tsp compile index.tsp --emit \"{pkg}\" --output-dir \"{dirB}\"",
                    workingDirectory: DomainSpecRoot,
                    environmentVariables: env);

                var diff = Helpers.BytewiseDirectoryDiff(dirA, dirB);
                if (diff is null)
                {
                    Log.Information("VerifyEmitDeterministic: '{Emitter}' is deterministic.", name);
                    continue;
                }

                var msg = $"VerifyEmitDeterministic: emitter '{name}' is non-deterministic — {diff}";
                if (EmitFailOnDrift)
                    throw new InvalidOperationException(msg);
                Log.Warning(msg);
            }
        });

    /// <summary>Run <c>dotnet build</c> on the C# emitter output to validate the generated project.</summary>
    Target BuildCSharpEmit => _ => _
        .DependsOn(EmitCSharp)
        .Executes(() =>
        {
            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(EmitOutputDir / "csharp")
                .SetConfiguration("Release")
                .EnableNoLogo());
        });

    /// <summary>
    /// Run <c>git diff --quiet generated/</c> to assert nobody hand-edited a
    /// generated file (CI guard).
    /// </summary>
    Target VerifyNoManualEditsToGenerated => _ => _
        .Executes(() =>
        {
            var generated = DomainSpecRoot / "generated";
            if (!Directory.Exists(generated))
            {
                Log.Information("VerifyNoManualEditsToGenerated: {Path} not present, skipping.", generated);
                return;
            }

            var exitCode = ProcessTasks.StartProcess(
                    "git",
                    $"diff --quiet -- \"{generated}\"",
                    workingDirectory: DomainSpecRoot,
                    logInvocation: false,
                    logOutput: false)
                .AssertWaitForExit()
                .ExitCode;

            if (exitCode == 0)
            {
                Log.Information("VerifyNoManualEditsToGenerated: {Path} is clean.", generated);
                return;
            }

            // Re-run without --quiet so the offending diff appears in the build log.
            ProcessTasks.StartProcess(
                    "git",
                    $"diff -- \"{generated}\"",
                    workingDirectory: DomainSpecRoot)
                .AssertWaitForExit();
            throw new InvalidOperationException(
                $"VerifyNoManualEditsToGenerated: {generated} contains uncommitted changes; " +
                "regenerate via EmitAll instead of hand-editing.");
        });

    /// <summary>Run <c>npm pack</c> for the downstream API package.</summary>
    Target PackApiPackage => _ => _
        .DependsOn(VerifyKeysLockstep, VerifyEmitDeterministic, VerifyNoManualEditsToGenerated, CompileDomainSpec)
        .Executes(() =>
        {
            NpmTasks.Npm("pack", workingDirectory: DomainSpecRoot);
        });

    /// <summary>
    /// Run <c>npm publish --provenance</c> against the GitHub Packages npm registry
    /// for the <c>@o-ancpplua</c> scope.
    /// </summary>
    Target PublishApiPackage => _ => _
        .DependsOn(PackApiPackage)
        .Executes(() =>
        {
            var token = Environment.GetEnvironmentVariable("NODE_AUTH_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "PublishApiPackage: NODE_AUTH_TOKEN env var is required " +
                    "(GitHub Packages npm token with write:packages scope).");

            NpmTasks.Npm(
                "publish --access public --provenance --registry=https://npm.pkg.github.com",
                workingDirectory: DomainSpecRoot);
        });

    private static void RunDomainEmitter(IDomainConventionsApi build, string name)
    {
        var pkg = Helpers.ResolveDomainEmitterPackage(name);
        var outDir = build.EmitOutputDir / name;
        outDir.CreateOrCleanDirectory();
        NpmTasks.Npm(
            $"exec --no -- tsp compile index.tsp --emit \"{pkg}\" --output-dir \"{outDir}\"",
            workingDirectory: build.DomainSpecRoot,
            environmentVariables: Helpers.DeterministicProcessEnv());
    }
}
