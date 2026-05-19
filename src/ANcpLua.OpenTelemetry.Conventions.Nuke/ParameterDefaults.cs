using System;
using System.Linq.Expressions;
using Nuke.Common;

namespace ANcpLua.OpenTelemetry.Conventions.Nuke;

/// <summary>
/// Helpers for resolving <see cref="ParameterAttribute"/>-decorated default-interface
/// properties that have <see cref="ValueType"/> targets (e.g. <c>int</c>, <c>bool</c>).
/// </summary>
/// <remarks>
/// Nuke's <see cref="INukeBuild.TryGetValue{T}(Expression{Func{T}})"/> is constrained
/// to <c>where T : class</c>, so a default-interface getter like
/// <c>int Foo => TryGetValue(() => Foo)</c> does not compile for value types. The
/// idiomatic Nuke workaround is to box through <see cref="object"/>; this helper
/// hides the cast and provides a typed fallback default.
/// </remarks>
public static class ParameterDefaults
{
    /// <summary>
    /// Resolves a parameter value via <see cref="INukeBuild.TryGetValue{T}(Expression{Func{T}})"/>
    /// for a value-typed property, falling back to <paramref name="defaultValue"/>
    /// when no value was supplied on the command line, in environment variables, or
    /// in <c>.nuke/parameters.json</c>.
    /// </summary>
    /// <typeparam name="T">Value-type parameter target (e.g. <c>int</c>, <c>bool</c>).</typeparam>
    /// <param name="build">The current build instance (<c>this</c> from the interface getter).</param>
    /// <param name="expression">A lambda referencing the parameter property itself, e.g. <c>() =&gt; PackageVersionSuffix</c>.</param>
    /// <param name="defaultValue">Value to return when no override is supplied.</param>
    /// <returns>The resolved parameter value, or <paramref name="defaultValue"/>.</returns>
    public static T GetOrDefault<T>(INukeBuild build, Expression<Func<T>> expression, T defaultValue)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(expression);

        // Re-target the expression onto `object` so it satisfies TryGetValue<T> where T : class.
        Expression<Func<object?>> boxed = Expression.Lambda<Func<object?>>(
            Expression.Convert(expression.Body, typeof(object)));

        object? raw = build.TryGetValue(boxed);
        return raw is T value ? value : defaultValue;
    }
}
