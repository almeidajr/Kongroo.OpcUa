# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

An OPC UA **server**, scaffolded from the Kongroo `dotnet new` templates
(`kongroo-sln` + `kongroo-worker`). The owner's stated goal is learning the OPC UA server side.

The server project is still the template's placeholder: `SampleWorker` is a `PeriodicTimer` log
loop, and no `OPCFoundation.NetStandard.*` package is referenced yet. When the real server lands it
replaces `SampleWorker`; use the `building-opcua-servers` skill for it (2.0 stack: model XML +
`[NodeManager]` partial + `AddOpcUa().AddServer(...)` on the existing generic host — **not** the 1.x
`StandardServer`/`CustomNodeManager2` pattern that training data suggests).

## Commands

```bash
dotnet tool restore          # CSharpier — required once per clone
pnpm install                 # Prettier + commitlint — required once per clone

dotnet build -warnaserror
dotnet test --no-build

dotnet csharpier format .    # then: dotnet csharpier check .
pnpm exec prettier --write . # then: pnpm exec prettier --check .
```

Single test — the test project runs on Microsoft Testing Platform, not VSTest, so filters go after
`--` and use MTP syntax (`--filter-class`, `--filter-method`, `--filter-query`):

```bash
dotnet test --no-build -- --filter-method "*RandomInt*"
```

A filter that matches nothing exits **8** ("Zero tests ran"), not 0 — a typo'd filter looks like a
failure, not a pass.

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
- **`TimeProvider` is registered** in `Program.cs` and injected (`SampleWorker` takes it). Using
  `FakeTimeProvider` in a test needs `Microsoft.Extensions.TimeProvider.Testing` added to
  `Directory.Packages.props` first — it was trimmed out, nothing uses it yet.
- **The `building-opcua-servers` skill's worked examples use `ct`, `s` and `v` in lambdas.** Follow
  the skill's shape, not its parameter names.

Commits are Conventional Commits (`commitlint.config.cjs`). `.pre-commit-config.yaml` wires
csharpier, prettier and commitlint, but the hooks are **not installed** in this clone — run the
checks manually before committing, or `pre-commit install`.
