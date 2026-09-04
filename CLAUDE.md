# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

An OPC UA **server**, scaffolded from the Kongroo `dotnet new` templates
(`kongroo-sln` + `kongroo-worker`). The owner's stated goal is learning the OPC UA server side.

`SampleWorker` is gone. The server exposes a `Plant` object generated from
`src/Kongroo.OpcUa.Server/Model/Plant.xml` (namespace `http://kongroo.dev/UA/Plant/`): a read-only
`Temperature`, a read/write `Setpoint`, a `SetSetpoint` method, and a `SetpointChangedEventType`
event. `PlantNodeManager.cs` carries the `[NodeManager]` attribute that opts the class in to source
generation; `PlantNodeManager.Configure.cs` wires the handlers and the event stream; the pure
simulation logic lives in `PlantSimulation.cs`. `Program.cs` keeps the template's Serilog and
OpenTelemetry setup and adds `AddOpcUa().AddServer(...)` on the same generic host — this is the 2.0
stack (model XML + `[NodeManager]` partial), **not** the 1.x `StandardServer`/`CustomNodeManager2`
pattern that training data suggests. Use the `building-opcua-servers` skill when extending it. The
stack packages (`OPCFoundation.NetStandard.Opc.Ua.*`) are `2.0.0-preview.3` from NuGet;
`D:\gsc\UA-.NETStandard` is a read-only local reference checkout of the same repo's `master`, ahead
of what the preview package ships — see the traps below for where that gap bites.

## Commands

```bash
dotnet tool restore          # CSharpier — required once per clone
pnpm install                 # Prettier + commitlint — required once per clone

dotnet build -warnaserror
dotnet test --no-build

dotnet csharpier format .    # then: dotnet csharpier check .
pnpm exec prettier --write . # then: pnpm exec prettier --check .
```

Single test — the test projects run on Microsoft Testing Platform, not VSTest, so filters go after
`--` and use MTP syntax (`--filter-class`, `--filter-method`, `--filter-query`). Always scope the run
to the csproj that owns the test:

```bash
dotnet test --no-build test/Kongroo.OpcUa.UnitTests/Kongroo.OpcUa.UnitTests.csproj -- --filter-method "*TemperatureAt*"
```

A filter that matches nothing exits **8** ("Zero tests ran"), not 0 — a typo'd filter looks like a
failure, not a pass. That is also why the run above is scoped: solution-wide, `dotnet test --no-build
-- --filter-method "*TemperatureAt*"` runs both test projects, and the integration project (which has
no matching test) reports "Zero tests ran" and drags the whole run to exit 8 even though the unit
project passed.

Do not add `--nologo` to `dotnet test`: the SDK forwards it to each MTP module, which rejects the
unknown option and exits **5** while `dotnet test` only prints "Zero tests ran" — it reads exactly
like a broken test project.

## Build conventions that bite

Everything below is enforced; ignoring it means a red build or red CI, not a style nit.

- **`TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`** — every analyzer and IDE-style warning is
  a build error. Sonar (`SonarAnalyzer.CSharp`) runs on every project.
- **`GenerateDocumentationFile` must stay `true`** in `Directory.Build.props`. Removing it fails the
  build with `CSC : error EnableGenerateDocumentationFile` — `EnforceCodeStyleInBuild` needs it to
  run IDE0005. The error names no project of ours and reads like an SDK bug.
- **`UseArtifactsOutput`** — build output is `artifacts/bin/<Project>/<config>/`, there are no
  per-project `bin/`/`obj/` dirs. Paths in scripts and Dockerfiles must account for that.
- **Central Package Management** — versions live only in `Directory.Packages.props`; a `Version=`
  on a `PackageReference` is an error. Unlike the stock template, this file has been trimmed to the
  packages this repo actually references, so adding a package means adding its `PackageVersion`
  first.
- **CI order** (`ci.yml`): `csharpier check` and `prettier --check` run _before_ `dotnet build`.
  Unformatted code fails CI before it ever compiles.
- **`IsPackable` is `false` repo-wide**, which makes `release.yml`'s `dotnet pack` a deliberate
  no-op. Don't "fix" it.
- **`UnderObjectsFolder()` / `OrganizedBy()` do not exist in `2.0.0-preview.3`** — they are
  master-only. A fresh root node must be declared in `Model/Plant.xml`.
- **`PlantType` makes the generator emit a class named `PlantState`.** The model compiler emits
  `{TypeName}State`, so the simulation record is deliberately named `PlantSimulationState` to avoid
  the collision. Anyone adding a model type must check the generated name.
- **`SupportsEvents="true"` belongs on the `<opc:Object>` instance, not the ObjectType** — the
  generator gates the typed `Publish<TEvent>` accessor on the instance.
- **Server settings bind through the Options pattern, and `AddServer`'s callback cannot see DI.**
  `PlantServerOptions` (section `OpcUa`) is bound with `.ValidateDataAnnotations().ValidateOnStart()`,
  so a malformed port refuses to boot instead of silently falling back. Because
  `AddServer(Action<OpcUaServerOptions>)` gets no service provider, anything derived from
  configuration is applied in a second `AddOptions<OpcUaServerOptions>().Configure<IOptions<…>>(…)`
  after it — `Configure` actions run in registration order. `ValidateDataAnnotations` needs the
  `Microsoft.Extensions.Options.DataAnnotations` package; it is not in the `Hosting` graph.
- **`PkiRoot` is pinned to `%LOCALAPPDATA%/Kongroo/OpcUaServer/pki`**, not the stack's default
  `%TEMP%/OPC Foundation/{App}/pki`. `%TEMP%` is routinely cleared, and a server that loses its
  certificate store regenerates its identity on the next boot, forcing every client to re-trust it.
  It is set in `Program.cs`, deliberately not configurable.
- **`AddServer` boots the server inside a `BackgroundService`, so `host.StartAsync()` returns before
  any endpoint listener is bound.** Anything that connects immediately after `StartAsync` — an
  in-process test, a tool — will fail. Register an `IServerStartupTask` and await it; the stack
  invokes those right after the listeners open, which is a real readiness signal rather than a
  guessed delay or a retry loop. `test/Kongroo.OpcUa.IntegrationTests/PlantServerFixture.cs` is the
  working example.

## Layout

`Kongroo.OpcUa.slnx` (XML solution format) carries `/src/` and `/test/` folders. Adding a project is
always two steps — the template's own `add-to-solution` post-action does not fire against `.slnx`:

```bash
dotnet new kongroo-test -n <Name> -o test/<Name>
dotnet sln Kongroo.OpcUa.slnx add test/<Name>/<Name>.csproj
```

Adders available: `kongroo-api`, `kongroo-worker`, `kongroo-cli`, `kongroo-console`, `kongroo-lib`
(`--packable` to publish), `kongroo-test`, `kongroo-itest`. See `README.md` for the full list and
the nuget.org OIDC publishing setup.

## Formatting

`.editorconfig` is the authority for mechanics (201 lines). The parts that shape code: 120-column
limit, file-scoped namespaces, and a `[{test,**/test}/**.cs]` section that turns off IDE1006 and
CA1707 — that exists solely to make the underscored test-method naming convention legal.

CSharpier owns C# formatting and Prettier owns JSON/YAML/Markdown; don't hand-format either.

## Code style

The C# style rules live in `~/.claude/rules/csharp-style.md` (functional first, immutable by
default, pure logic in `private static` methods, small methods, modern C#, idiomatic async, no
abbreviations anywhere including lambda parameters, affirmative boolean predicates, and the
`<ClassUnderTest>Tests` / `<Method>_<With|When><Scenario>_Should<Result>` test naming). They apply
here; this section records only what is specific to this repo.

- **Part of it is compiler-enforced, not preference.** `.editorconfig` escalates CA2227, CA1002,
  CA1819 and CA1051 to warning, and warnings are errors — settable collection properties, exposed
  `List<T>` or arrays, and public fields do not compile.
- **Domain short forms that count as words here:** `NodeId`, `OpcUa`, on top of the usual `Id`,
  `Uri`, `Json`.
- **`TimeProvider` is registered as a singleton** in `Program.cs` and the stack's
  `OpcUaServerHostedService` resolves it from DI — so the registration is load-bearing, not dead.
  (The stack only `TryAddSingleton`s its own default inside `RegisterJwtIssuer`, which is gated on
  a JWKS URI and never runs here; the hosted service otherwise falls back to its
  `TimeProvider? = null` parameter default.) What is _not_ DI-activated is `PlantNodeManager`
  itself — only its generated factory is — so it holds a plain `TimeProvider.System` field instead
  (see the `ponytail:` comment in `PlantNodeManager.Configure.cs`). Using `FakeTimeProvider` in a
  test still needs `Microsoft.Extensions.TimeProvider.Testing` added to `Directory.Packages.props`
  first — it was trimmed out, nothing uses it yet.
- **The `building-opcua-servers` skill's worked examples use `ct`, `s`, `v` in lambdas and `m_` on
  fields.** Follow the skill's shape, not its identifiers: `.editorconfig` enforces `_camelCase`
  private fields (IDE1006 + warnings-as-errors), so `m_state` **does not compile** here.

Commits are Conventional Commits (`commitlint.config.cjs`). `.pre-commit-config.yaml` wires
csharpier, prettier and commitlint, but the hooks are **not installed** in this clone — run the
checks manually before committing, or `pre-commit install`.
