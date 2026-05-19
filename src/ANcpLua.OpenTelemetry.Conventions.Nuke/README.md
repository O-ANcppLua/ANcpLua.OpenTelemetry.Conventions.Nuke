# ANcpLua.OpenTelemetry.Conventions.Nuke (source)

Implementation details for the shared Nuke component package. See the
[repository README](../../README.md) for consumer-facing documentation.

## Files

| File | Purpose |
| --- | --- |
| `IUpstreamConventions.cs` | Component for the Weaver-based generator repo. |
| `IDomainConventionsApi.cs` | Component for the downstream TypeSpec API repo. |
| `LockstepPolicy.cs` | Helpers (currently `ParseSemconvSuffixVersion`). |

## XML-doc conventions

- Every public member has a `<summary>` and, where relevant, `<remarks>`,
  `<exception>`, `<example>`.
- Target methods document **what** the target must do — bodies are stubs
  with `TODO` comments; consumers override the targets in their own
  `Build` class.
- `<remarks>` distinguishes the upstream **semconv release tag**
  (e.g. `1.41.0`) from the declarative version-selection integer at
  `.instrumentation/development.general.<domain>.semconv`.
