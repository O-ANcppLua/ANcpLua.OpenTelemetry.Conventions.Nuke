# ANcpLua.OpenTelemetry.Conventions.Nuke

Shared [Nuke](https://nuke.build) build-component policy for the ANcpLua
OpenTelemetry semantic-conventions toolchain. This package does not generate or
emit anything itself — it ships two `INukeBuild` component interfaces and one
helper class that two sibling repositories implement to keep their generation
and API surfaces in lockstep.

> Not an official OpenTelemetry artifact. The conventions themselves come
> from the upstream
> [`open-telemetry/semantic-conventions`](https://github.com/open-telemetry/semantic-conventions)
> repository; this package only defines the build policy used to mirror them.

## Architecture

```
                       open-telemetry/semantic-conventions@v{SemconvVersion}
                                          |
                                          v
                      +---------------------------------------+
                      | ANcpLua/typespec-otel-semconv         |
                      | implements IUpstreamConventions       |
                      | publishes @ancplua/typespec-otel-     |
                      |   semconv@{SemconvVersion}-{n}        |
                      +---------------------------------------+
                                          |
                                  npm (GitHub Packages)
                                          |
                                          v
                      +---------------------------------------+
                      | O-ANcppLua/ANcpLua.OtelConventions.Api|
                      | implements IDomainConventionsApi      |
                      | publishes @o-ancpplua/otel-           |
                      |   conventions-api                     |
                      +---------------------------------------+
```

Both repositories take a `PackageReference` on this package and `implement`
their respective interface. Interface targets are declared here so a change to
the lockstep policy (a new verification gate, a renamed target, an added
parameter) lands in **one** place.

## Interface to consumer mapping

| Interface | Consumer repository | npm package published |
| --- | --- | --- |
| `IUpstreamConventions` | [`ANcpLua/typespec-otel-semconv`](https://github.com/ANcpLua/typespec-otel-semconv) | `@ancplua/typespec-otel-semconv@{semconv}-{n}` |
| `IDomainConventionsApi` | [`O-ANcppLua/ANcpLua.OtelConventions.Api`](https://github.com/O-ANcppLua/ANcpLua.OtelConventions.Api) | `@o-ancpplua/otel-conventions-api` |

## Install

```xml
<ItemGroup>
  <PackageReference Include="ANcpLua.OpenTelemetry.Conventions.Nuke" Version="0.1.0" />
</ItemGroup>
```

## Use

### Upstream generator

```csharp
using Nuke.Common;
using ANcpLua.OpenTelemetry.Conventions.Nuke;

class Build : NukeBuild, IUpstreamConventions
{
    public static int Main() =>
        Execute<Build>(x => ((IUpstreamConventions)x).PublishTypeSpecLibrary);
}
```

Targets the consumer overrides (typical):

- `RestoreWeaver`, `FetchSemconvModel`, `GenerateOtelKeys`
- `VerifyOtelKeysReproducible`, `VerifyOtelKeysScriptParity`,
  `VerifyOtelKeysCompile`, `RunSmokeTests`, `VerifyClean`
- `PackTypeSpecLibrary`, `PublishTypeSpecLibrary`

### Downstream API

```csharp
using Nuke.Common;
using ANcpLua.OpenTelemetry.Conventions.Nuke;

class Build : NukeBuild, IDomainConventionsApi
{
    public static int Main() =>
        Execute<Build>(x => ((IDomainConventionsApi)x).PublishApiPackage);
}
```

Targets the consumer overrides (typical):

- `RestoreTypeSpecDeps`, `VerifyKeysLockstep`, `CompileDomainSpec`
- `EmitCSharp`, `EmitDuckDb`, `EmitTsTypes`, `LintConventions`, `EmitAll`
- `VerifyEmitDeterministic`, `BuildCSharpEmit`, `VerifyNoManualEditsToGenerated`
- `PackApiPackage`, `PublishApiPackage`

## Lockstep version scheme

The upstream npm package version is `{semconv-version}-{n}` where `semconv-version`
mirrors the pinned `open-telemetry/semantic-conventions` release tag (e.g.
`1.41.0`) and `n` is a monotonic generator-revision counter starting at `1`.

The downstream package **must** pin an exact `{semconv}-{n}` version in
`package-lock.json` and verify that the keys file shipped by the upstream
package matches its own `generated/otel-keys.gen.tsp` byte-for-byte. This is
what `IDomainConventionsApi.VerifyKeysLockstep` enforces.

`LockstepPolicy.ParseSemconvSuffixVersion("1.41.0-3")` returns `("1.41.0", 3)`
and is the canonical parser shared by both builds.

## Compatibility

- Targets `net10.0` (Nuke.Common 10.x ships only a net10.0 assembly).
- Built against `Nuke.Common` 10.1.0.
- SDK pinned via `global.json` to .NET 10.

## License

[Apache-2.0](./LICENSE). Copyright 2026 Alexander Nachtmann.
