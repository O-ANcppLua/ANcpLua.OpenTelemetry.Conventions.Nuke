using System;
using System.Globalization;

namespace OtelConventions.Nuke;

/// <summary>
/// Shared lockstep-policy helpers used by both the upstream generator
/// (<c>@ancplua/typespec-otel-semconv</c>) and the downstream API surface
/// (<c>@o-ancpplua/otel-conventions-api</c>) builds.
/// </summary>
/// <remarks>
/// The upstream generator publishes its npm package using the version scheme
/// <c>{semconv-version}-{n}</c>, for example <c>1.41.0-3</c>, where:
/// <list type="bullet">
///   <item><description><c>semconv-version</c> is the pinned upstream OpenTelemetry
///   semantic-conventions release tag (e.g. <c>1.41.0</c>, mirroring
///   <c>open-telemetry/semantic-conventions@v1.41.0</c>).</description></item>
///   <item><description><c>n</c> is the monotonic generator-revision counter:
///   the first release for a given semconv version is <c>-1</c>, the next is
///   <c>-2</c>, and so on. It is bumped whenever generator output, Weaver
///   settings, or the generation pipeline changes <em>without</em> bumping the
///   semconv version.</description></item>
/// </list>
/// The downstream package must pin an exact <c>{semconv}-{n}</c> version of the
/// upstream package in its <c>package-lock.json</c> to guarantee byte-identical
/// generated output across both repositories.
/// </remarks>
public static class LockstepPolicy
{
    /// <summary>
    /// Parses a lockstep version string of the form <c>"{semconv}-{n}"</c>
    /// (for example <c>"1.41.0-3"</c>) into its two components.
    /// </summary>
    /// <param name="version">
    /// The version string to parse. Must contain exactly one <c>-</c> separator
    /// between the semconv portion and the generator-revision portion.
    /// </param>
    /// <returns>
    /// A tuple of:
    /// <list type="bullet">
    ///   <item><description><c>semconv</c>: the upstream OpenTelemetry semantic-conventions
    ///   release tag, e.g. <c>"1.41.0"</c>.</description></item>
    ///   <item><description><c>n</c>: the monotonic generator-revision counter, &gt;= 1.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="version"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="version"/> is empty, missing the <c>-</c>
    /// separator, has an empty semconv segment, has a non-integer <c>n</c>
    /// segment, or contains a <c>n</c> value that is not strictly positive.
    /// </exception>
    /// <example>
    /// <code>
    /// var (semconv, n) = LockstepPolicy.ParseSemconvSuffixVersion("1.41.0-3");
    /// // semconv == "1.41.0"
    /// // n       == 3
    /// </code>
    /// </example>
    public static (string Semconv, int N) ParseSemconvSuffixVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Length == 0)
        {
            throw new FormatException(
                "Lockstep version string must not be empty; expected '{semconv}-{n}'.");
        }

        int separator = version.LastIndexOf('-');
        if (separator <= 0 || separator >= version.Length - 1)
        {
            throw new FormatException(
                $"Lockstep version '{version}' is malformed: expected '{{semconv}}-{{n}}' " +
                "with a non-empty semconv segment and a non-empty integer suffix.");
        }

        string semconv = version[..separator];
        string suffix = version[(separator + 1)..];

        if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
        {
            throw new FormatException(
                $"Lockstep version '{version}' has non-integer revision suffix '{suffix}'.");
        }

        if (n < 1)
        {
            throw new FormatException(
                $"Lockstep version '{version}' has revision suffix '{n}'; must be >= 1.");
        }

        return (semconv, n);
    }
}
