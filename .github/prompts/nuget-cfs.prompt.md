---
name: "Configure NuGet CFS"
description: "Use when configuring NuGet, CI, containers, and Dependabot to resolve exclusively through Microsoft CFS."
agent: "agent"
---

# Reusable Prompt: Configure NuGet and Dependabot to Use Microsoft CFS

Implement this task in the current repository. Do not merely explain the changes.

## Goal

Route NuGet package resolution through Microsoft's CFS proxy:

`https://packagefeedproxy.microsoft.io/nuget/v3/index.json`

Apply this consistently to local development, GitHub Actions, and Dependabot. Do not add fallback to nuget.org. First test whether the endpoint is reachable from the current/public runner context; do not assume anonymous access if it is not observed.

## 1. Discover the repository

Inspect for solutions and projects (`*.sln`, `*.slnx`, `*.csproj`, `*.fsproj`, `*.vbproj`), `packages.config`, `packages.lock.json`, `Directory.Packages.props`, all case variants of `NuGet.config`, `.github/dependabot.yml`, and workflows that restore .NET packages. Identify intentional private feeds and package-source mapping. If no NuGet ecosystem exists, make no changes and report that result.

## 2. Configure NuGet

Create or update the repository-level `NuGet.config` nearest the solution root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="Microsoft-CFS"
         value="https://packagefeedproxy.microsoft.io/nuget/v3/index.json"
         protocolVersion="3" />
  </packageSources>
</configuration>
```

`<clear />` is mandatory to prevent inherited nuget.org sources. Preserve intentional private feeds after `<clear />`. Preserve and adapt package-source mapping rather than weakening it. Do not commit credentials. Remove active direct sources for `api.nuget.org` and `nuget.org/api/v2` unless a documented approved exception exists.

## 3. Configure Dependabot

Create or merge `.github/dependabot.yml`. Preserve existing schedules, groups, labels, reviewers, ignore rules, limits, and other ecosystems.

Declare:

```yaml
version: 2

registries:
  microsoft-cfs-nuget:
    type: nuget-feed
    url: https://packagefeedproxy.microsoft.io/nuget/v3/index.json
```

Every relevant NuGet update block must reference it:

```yaml
updates:
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: weekly
    registries:
      - microsoft-cfs-nuget
```

Adapt directories to actual project roots. Do not add `replaces-base`; that option is for npm, not `nuget-feed`. Depend on repository `NuGet.config` plus `<clear />` to prevent source fallback.

## 4. Update GitHub Actions

Inspect every workflow that restores NuGet packages. Preserve existing build logic, but ensure restores use repository configuration. Where appropriate:

```bash
dotnet restore --configfile NuGet.config
```

Remove explicit nuget.org sources and fallback restore steps. Do not add authentication unless endpoint testing returns an authentication requirement. If authentication is required, stop and report the evidence instead of inventing credentials or switching to a different feed.

## 5. Validate

Run appropriate checks:

```bash
dotnet nuget list source
git grep -nE 'api\.nuget\.org|nuget\.org/api/v2|www\.nuget\.org' || true
dotnet restore --configfile NuGet.config
```

Run existing tests where available. Validate XML and Dependabot YAML. Confirm every relevant NuGet block references `microsoft-cfs-nuget`.

If Dependabot logs or execution are available, verify requests use the CFS endpoint and do not query nuget.org. Do not claim runtime validation from static inspection alone.

## 6. Fail closed

If CFS is unavailable or rejects the runner, restore must fail. Never retry against nuget.org. Preserve required private feeds, but document each exception.

## 7. Report

Return:

- project roots and private feeds found;
- files changed;
- effective NuGet sources;
- proxy reachability and restore/test results;
- remaining nuget.org references, classified by active config, lockfile, fixture, or documentation;
- Dependabot static and runtime validation status;
- assumptions, exceptions, and manual actions.

Never report a test as passed unless it was executed successfully.

## Acceptance criteria

- Repository `NuGet.config` contains `<clear />` and the CFS source.
- Required private feeds and package-source mappings remain valid.
- Dependabot defines and uses `microsoft-cfs-nuget` for every applicable root.
- GitHub Actions does not bypass CFS.
- No credentials or public-registry fallback were introduced without demonstrated need.
- Existing dependency versions and unrelated behavior remain unchanged unless necessary.
