using Nuke.Common;
using Nuke.Common.IO;

namespace Nuke.OpenTelemetry.Conventions;

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
/// Targets are declaration-only stubs. Concrete bodies live in the consumer
/// repository and are wired up via the standard Nuke component-override pattern.
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
            // TODO: Invoke `npm ci` at DomainSpecRoot; respect the existing package-lock.json.
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
            // TODO: Read package-lock.json, locate OtelKeysPackage entry, assert resolved
            // TODO: version == OtelKeysVersion exactly (no semver range tolerance).
            // TODO: Read node_modules/{OtelKeysPackage}/lib/otel-keys.tsp and diff bytewise
            // TODO: against {DomainSpecRoot}/generated/otel-keys.gen.tsp. Fail on any drift.
        });

    /// <summary>
    /// Run <c>tsp compile {DomainSpecRoot}/index.tsp --no-emit --warn-as-error</c>
    /// against the full domain spec.
    /// </summary>
    Target CompileDomainSpec => _ => _
        .DependsOn(VerifyKeysLockstep)
        .Executes(() =>
        {
            // TODO: Invoke `tsp compile index.tsp --no-emit --warn-as-error` at DomainSpecRoot.
        });

    /// <summary>Run the C# emitter, writing under <c>{EmitOutputDir}/csharp</c>.</summary>
    Target EmitCSharp => _ => _
        .DependsOn(CompileDomainSpec)
        .Executes(() =>
        {
            // TODO: Invoke `tsp compile index.tsp --emit @typespec/http-client-csharp`
            // TODO: (or the configured C# emitter) with output-dir EmitOutputDir/csharp.
        });

    /// <summary>Run the DuckDB emitter, writing under <c>{EmitOutputDir}/duckdb</c>.</summary>
    Target EmitDuckDb => _ => _
        .DependsOn(CompileDomainSpec)
        .Executes(() =>
        {
            // TODO: Invoke the DuckDB schema emitter against the domain spec; output to
            // TODO: EmitOutputDir/duckdb. The emitter is provided by the downstream repo.
        });

    /// <summary>Run the TypeScript types emitter, writing under <c>{EmitOutputDir}/ts-types</c>.</summary>
    Target EmitTsTypes => _ => _
        .DependsOn(CompileDomainSpec)
        .Executes(() =>
        {
            // TODO: Invoke the TypeScript types emitter; output to EmitOutputDir/ts-types.
        });

    /// <summary>Run the lint emitter / conventions linter, writing under <c>{EmitOutputDir}/lint</c>.</summary>
    Target LintConventions => _ => _
        .DependsOn(CompileDomainSpec)
        .Executes(() =>
        {
            // TODO: Invoke the conventions linter; surface findings as build warnings/errors.
        });

    /// <summary>
    /// Aggregate target that runs every emitter in <see cref="Emitters"/>: by default
    /// <see cref="EmitCSharp"/>, <see cref="EmitDuckDb"/>, <see cref="EmitTsTypes"/>,
    /// and <see cref="LintConventions"/>.
    /// </summary>
    Target EmitAll => _ => _
        .DependsOn(EmitCSharp, EmitDuckDb, EmitTsTypes, LintConventions)
        .Executes(() =>
        {
            // TODO: This aggregate is a barrier target; individual emitters do the work.
        });

    /// <summary>
    /// Emit twice into separate temporary directories and diff them to guarantee
    /// emitters are deterministic. Fails when <see cref="EmitFailOnDrift"/> is <c>true</c>.
    /// </summary>
    Target VerifyEmitDeterministic => _ => _
        .DependsOn(VerifyKeysLockstep)
        .Executes(() =>
        {
            // TODO: Run each emitter twice into separate scratch dirs, diff bytewise;
            // TODO: respect EmitFailOnDrift.
        });

    /// <summary>Run <c>dotnet build</c> on the C# emitter output to validate the generated project.</summary>
    Target BuildCSharpEmit => _ => _
        .DependsOn(EmitCSharp)
        .Executes(() =>
        {
            // TODO: `dotnet build {EmitOutputDir}/csharp -c Release --nologo`.
        });

    /// <summary>
    /// Run <c>git diff --quiet generated/</c> to assert nobody hand-edited a
    /// generated file (CI guard).
    /// </summary>
    Target VerifyNoManualEditsToGenerated => _ => _
        .Executes(() =>
        {
            // TODO: Run `git diff --quiet -- {DomainSpecRoot}/generated/`; fail on non-zero.
        });

    /// <summary>Run <c>npm pack</c> for the downstream API package.</summary>
    Target PackApiPackage => _ => _
        .DependsOn(VerifyKeysLockstep, VerifyEmitDeterministic, VerifyNoManualEditsToGenerated, CompileDomainSpec)
        .Executes(() =>
        {
            // TODO: Invoke `npm pack` at DomainSpecRoot and surface the tarball path.
        });

    /// <summary>
    /// Run <c>npm publish --provenance</c> against the GitHub Packages npm registry
    /// for the <c>@o-ancpplua</c> scope.
    /// </summary>
    Target PublishApiPackage => _ => _
        .DependsOn(PackApiPackage)
        .Executes(() =>
        {
            // TODO: `npm publish --provenance --registry=https://npm.pkg.github.com`
            // TODO: using the @o-ancpplua scope; require a NODE_AUTH_TOKEN with write:packages.
        });
}
