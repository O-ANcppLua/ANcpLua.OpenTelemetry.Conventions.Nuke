using Nuke.Common;
using Nuke.Common.IO;

namespace Nuke.OpenTelemetry.Conventions;

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
/// Targets here are intentionally declaration-only stubs. Concrete bodies live in the
/// consumer repository so the generator can evolve without forcing a re-release of this
/// shared component package. Override the targets in the consuming <c>Build</c> class
/// using the standard Nuke component-target override pattern.
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
        .Executes(() =>
        {
            // TODO: Resolve the platform-specific Weaver release asset for WeaverVersion,
            // TODO: download it to ~/.nuke/temp/weaver/{WeaverVersion}, verify checksum,
            // TODO: extract, and expose the resolved binary path to downstream targets.
        });

    /// <summary>
    /// Check out <c>open-telemetry/semantic-conventions@v{SemconvVersion}</c> and
    /// recursively materialise all <c>model/**/*.yaml</c> and <c>model/**/*.yml</c>
    /// files (so e.g. <c>model/graphql/spans.yml</c> is included).
    /// </summary>
    Target FetchSemconvModel => _ => _
        .Executes(() =>
        {
            // TODO: git clone --depth 1 --branch v{SemconvVersion} open-telemetry/semantic-conventions
            // TODO: into a deterministic scratch directory; recursively enumerate
            // TODO: model/**/*.{yaml,yml} (NOT just .yaml) and stage them for Weaver input.
        });

    /// <summary>
    /// Invoke Weaver against the materialised model and write the generated TypeSpec
    /// to <see cref="OtelKeysOutput"/>.
    /// </summary>
    Target GenerateOtelKeys => _ => _
        .DependsOn(RestoreWeaver, FetchSemconvModel)
        .Executes(() =>
        {
            // TODO: Run the pinned Weaver binary with the project's templates/registry,
            // TODO: target SemconvVersion, and write the result to OtelKeysOutput.
            // TODO: Generated output must be byte-deterministic across runs.
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
            // TODO: Re-run generation into a temp directory, diff bytewise against
            // TODO: OtelKeysOutput; respect FailOnDrift.
        });

    /// <summary>
    /// Invoke both <c>scripts/generate.mjs</c> and its PowerShell sibling (if present)
    /// and assert the outputs are byte-identical, guarding against script drift.
    /// </summary>
    Target VerifyOtelKeysScriptParity => _ => _
        .Executes(() =>
        {
            // TODO: Run scripts/generate.mjs into dir A, run scripts/generate.ps1 (if it exists)
            // TODO: into dir B, compare A and B bytewise; fail if they differ.
        });

    /// <summary>
    /// Run <c>tsp compile test/smoke.tsp --no-emit --warn-as-error</c> against the
    /// generated output to confirm the keys file is syntactically and semantically valid.
    /// </summary>
    Target VerifyOtelKeysCompile => _ => _
        .DependsOn(GenerateOtelKeys)
        .Executes(() =>
        {
            // TODO: Invoke `tsp compile test/smoke.tsp --no-emit --warn-as-error`;
            // TODO: pin the @typespec/compiler version inside TypeSpecCompilerRange.
        });

    /// <summary>Run <c>npm run test</c> for the generator package.</summary>
    Target RunSmokeTests => _ => _
        .DependsOn(GenerateOtelKeys)
        .Executes(() =>
        {
            // TODO: Invoke `npm run test` (vitest) for the upstream generator package.
        });

    /// <summary>
    /// Wrap the existing <c>verify-clean</c> npm script, which regenerates and runs
    /// <c>git diff --exit-code -- lib/ src/generated/</c> to ensure no manual edits.
    /// </summary>
    Target VerifyClean => _ => _
        .Executes(() =>
        {
            // TODO: Invoke `npm run verify-clean`; surface its exit code as the target result.
        });

    /// <summary>Run <c>npm pack</c> to produce the GitHub Packages tarball.</summary>
    Target PackTypeSpecLibrary => _ => _
        .DependsOn(VerifyOtelKeysReproducible, VerifyOtelKeysCompile, RunSmokeTests, VerifyClean)
        .Executes(() =>
        {
            // TODO: Bump package.json version to {SemconvVersion}-{PackageVersionSuffix},
            // TODO: then `npm pack` and surface the tarball path.
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
            // TODO: `npm publish --provenance --registry=https://npm.pkg.github.com`
            // TODO: using the @ancplua scope; require a NODE_AUTH_TOKEN with write:packages.
        });
}
