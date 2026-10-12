---
name: "Configure npm CFS"
description: "Use when configuring npm, lockfiles, CI, containers, and Dependabot to resolve exclusively through Microsoft CFS."
agent: "agent"
---

# Reusable Prompt: Configure npm and Dependabot to Use Microsoft CFS

Implement this task in the current repository. Do not merely explain the changes.

## Goal

Route npm-compatible package resolution through Microsoft's CFS proxy:

`https://packagefeedproxy.microsoft.io/npm/`

Apply this consistently to local development, GitHub Actions, and Dependabot. Do not introduce credentials for this anonymously readable endpoint. Do not add fallback to npmjs.org.

## 1. Discover the repository

Inspect for `package.json`, npm/pnpm/Yarn lockfiles, `.npmrc`, workspaces, monorepo roots, `.github/dependabot.yml`, and workflows that install JavaScript dependencies. Identify intentional scoped/private registries. If no npm-compatible ecosystem exists, make no changes and report that result.

## 2. Configure the package manager

Create or update the applicable repository-level `.npmrc`:

```ini
registry=https://packagefeedproxy.microsoft.io/npm/
```

Preserve legitimate unrelated settings and intentional scoped private registries. Remove active default-registry overrides to `registry.npmjs.org`. Do not add `_authToken`, username, password, tenant ID, client ID, PAT, Azure login, or OIDC for the CFS proxy.

Account for the detected package manager. Do not switch npm, pnpm, or Yarn. Check whether its lockfile contains resolved URLs that bypass the configured registry. Regenerate lockfiles only if required, without changing package versions unnecessarily.

## 3. Configure Dependabot

Create or merge `.github/dependabot.yml`. Preserve existing schedules, groups, labels, reviewers, ignore rules, limits, and other ecosystems.

Declare:

```yaml
version: 2

registries:
  microsoft-cfs-npm:
    type: npm-registry
    url: https://packagefeedproxy.microsoft.io/npm/
    replaces-base: true
```

Every relevant npm update block must reference the registry:

```yaml
updates:
  - package-ecosystem: npm
    directory: /
    schedule:
      interval: weekly
    registries:
      - microsoft-cfs-npm
```

Adapt directories to the actual repository. Add one block per required package root. `replaces-base: true` is mandatory. Do not configure credentials.

## 4. Update GitHub Actions

Inspect every workflow that installs JavaScript dependencies. Keep the current package manager and normal locked install command, such as `npm ci`, `pnpm install --frozen-lockfile`, or the repository's Yarn equivalent. Ensure no workflow explicitly overrides the registry with npmjs.org. Do not add public-registry fallback.

## 5. Validate

Run appropriate checks, including:

```bash
npm config get registry
git grep -nE 'registry\.npmjs\.org|registry\.yarnpkg\.com|registry\.npmmirror\.com' || true
```

Run the repository's normal clean install and tests where available. Validate `.github/dependabot.yml` syntax and confirm every relevant npm block references `microsoft-cfs-npm`.

If Dependabot logs or execution are available, verify requests use the CFS endpoint. Do not claim runtime validation from static inspection alone.

## 6. Fail closed

If CFS is unavailable, dependency resolution must fail. Never retry against a public registry. Preserve required private scoped feeds, but document each exception.

## 7. Report

Return:

- ecosystems and package roots found;
- files changed;
- effective registry;
- clean-install and test results;
- remaining direct-public-registry references, classified by active config, lockfile, fixture, or documentation;
- Dependabot static and runtime validation status;
- assumptions, exceptions, and manual actions.

Never report a test as passed unless it was executed successfully.

## Acceptance criteria

- `.npmrc` defaults to the Microsoft CFS npm proxy.
- Dependabot defines `microsoft-cfs-npm` with `replaces-base: true`.
- Every applicable npm update block uses that registry.
- GitHub Actions does not bypass the proxy.
- No credentials or public-registry fallback were introduced.
- Existing dependency versions and unrelated behavior remain unchanged unless necessary.
