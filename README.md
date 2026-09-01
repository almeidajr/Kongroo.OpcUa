# ![Kongroo](assets/icon-32.png) Kongroo.OpcUa

Scaffolded from the Kongroo solution template (`dotnet new kongroo-sln`). The repo starts empty —
conventions, hooks and CI are wired up, and you add projects with the adder templates.

## Getting started

```bash
dotnet tool restore       # CSharpier
pnpm install              # Prettier + commitlint
```

## Adding projects

Each adder creates a project on disk; it is not wired into the solution until you also run
`dotnet sln add` on its `.csproj`. The empty `.slnx` routes each project into the matching `/src/`
or `/test/` folder automatically, based on the path you give it.

```bash
dotnet new kongroo-api -n Kongroo.OpcUa.Api -o src/Kongroo.OpcUa.Api
dotnet sln add src/Kongroo.OpcUa.Api/Kongroo.OpcUa.Api.csproj

dotnet new kongroo-test -n Kongroo.OpcUa.UnitTests -o test/Kongroo.OpcUa.UnitTests
dotnet sln add test/Kongroo.OpcUa.UnitTests/Kongroo.OpcUa.UnitTests.csproj
```

The other adders follow the same two-command pattern:

```bash
dotnet new kongroo-worker  -n Kongroo.OpcUa.Worker -o src/Kongroo.OpcUa.Worker
dotnet new kongroo-cli     -n Kongroo.OpcUa.Cli    -o src/Kongroo.OpcUa.Cli
dotnet new kongroo-console -n Kongroo.OpcUa.Tool   -o src/Kongroo.OpcUa.Tool
dotnet new kongroo-lib     -n Kongroo.OpcUa.Domain -o src/Kongroo.OpcUa.Domain
dotnet new kongroo-itest   -n Kongroo.OpcUa.IntegrationTests -o test/Kongroo.OpcUa.IntegrationTests
# then: dotnet sln add <path passed to -o above>/<ProjectName>.csproj
```

## Package versions

`Directory.Packages.props` is the single source of every package version — Central Package
Management, one file, one version per package. Project files reference packages without a version.

It ships with every version the Kongroo adders can reference, not just the ones this repo uses, so
`dotnet new kongroo-worker` (or any other adder) works without editing it first. An entry nothing
references costs nothing to restore.

To change a version, edit it here; NuGet rejects a duplicate `PackageVersion`, so the file cannot
disagree with itself.

## Publishing packages

Projects do not publish by default (`IsPackable` is `false` repo-wide). Create a publishable library
with `dotnet new kongroo-lib --packable`, then tag:

```bash
git tag v1.0.0 && git push --tags
```

`.github/workflows/release.yml` publishes every packable project to nuget.org via
**trusted publishing (OIDC)** — no API key. One-time setup:

1. Create a trusted-publishing policy at <https://www.nuget.org/account/trustedpublishing>:
   Repository = this repo, Workflow File = `release.yml`, Environment = `release`.
2. Repo **Settings → Environments → `release`** → add secret `NUGET_USER` = your
   nuget.org **profile name** (not email).
