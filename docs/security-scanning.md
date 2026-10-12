# Security scanning

AIProxyPlus uses four complementary scan layers. Some layers combine tools with different scopes, and each row
below names what runs, when, and whether it can block. Only the deterministic scans are authoritative merge
controls; the agentic scan is advisory.

| Layer | Tool | Runs | Blocks | Configuration |
|---|---|---|---|---|
| Static analysis | CodeQL (C#, JavaScript/TypeScript, `security-extended`) | Pull requests, pushes to `main`, weekly | Merge (required checks) | [codeql.yml](../.github/workflows/codeql.yml) |
| Dependency scan | Dependency review, Dependabot | Pull requests (moderate or higher), weekly updates | Merge (required check) | [dependency-review.yml](../.github/workflows/dependency-review.yml), [dependabot.yml](../.github/dependabot.yml) |
| Secret scan | GitHub secret scanning and push protection (server), gitleaks (local) | Every commit and push locally; every push on GitHub | Commit and push | [.gitleaks.toml](../.gitleaks.toml), [.githooks/](../.githooks) |
| Agentic scan | Copilot code review using the repository security instructions | Every pull request and every new push | Nothing (advisory) | [security-review.instructions.md](../.github/instructions/security-review.instructions.md) |

The `main` ruleset requires the two CodeQL checks and dependency review. It also requests Copilot code review for
every pull request. Branch deletion and force pushes remain blocked.

## Branch and pull-request workflow

Nothing is committed or pushed directly to `main`. Work on a branch and merge through a pull request:

```text
git switch -c <topic>
git push -u origin <topic>
gh pr create
```

- The `main` ruleset has no bypass actors, so the repository owner is held to the same rules: a pull request, the two
  CodeQL checks, and dependency review. Force pushes and branch deletion are blocked.
- The local hooks also refuse a commit on `main` and any push to `refs/heads/main`, so the mistake is caught before
  it reaches the server. The ruleset remains the authoritative control because hooks can be skipped with `--no-verify`.
- Human approval stays optional (a sole maintainer cannot approve their own pull request); the required checks and
  the advisory Copilot review are the gate.

## Static analysis (CodeQL)

CodeQL scans C# and JavaScript/TypeScript with the `security-extended` suite. Do not reduce its languages, queries,
triggers, or permissions. Triage alerts in GitHub code scanning; never suppress one on the strength of an AI finding.

## Dependency scan

Dependency review fails a pull request that introduces a vulnerable dependency of moderate severity or higher.
Dependabot proposes NuGet and npm updates weekly through the Microsoft CFS proxies, and its pull requests must pass
the compile-and-test workflow before trusted automation can enable auto-merge.

## Secret scan

GitHub secret scanning and push protection are enabled for the repository and act as the server-side backstop. The
local gate stops a secret before it leaves the machine, where it would otherwise already be exposed.

Set up each clone once:

```text
winget install Gitleaks.Gitleaks
git config core.hooksPath .githooks
```

- `pre-commit` scans staged changes and blocks the commit.
- `pre-push` scans every commit about to be pushed and blocks the push. It fails closed: a missing gitleaks, an
  unreadable ref range, or a force-push over a remote tip that is not fetched locally blocks the push rather than
  skipping the scan. For a new branch it scans only commits the destination remote does not already have.

Both hooks require gitleaks on `PATH` and run two configs: [.gitleaks.toml](../.gitleaks.toml) for everything, and
[.gitleaks.lockfiles.toml](../.gitleaks.lockfiles.toml) for `package-lock.json`. The second exists because the
built-in gitleaks config globally skips lockfiles and an extending config cannot undo that, yet a registry
credential or token can sit in a lockfile `resolved` URL. Run a full-history scan at any time with:

```text
gitleaks git --config .gitleaks.toml --redact --no-banner .
gitleaks git --config .gitleaks.lockfiles.toml --redact --no-banner .
```

If a secret is found, remove it, **rotate the credential**, and rewrite the unpushed commit. Rotation is mandatory
because a leaked value must be assumed compromised even if it never reached the remote. Add a fingerprint to
[.gitleaksignore](../.gitleaksignore) only for a confirmed false positive, with a comment explaining why; never add
a broad path or rule allowlist to silence a real finding. `--no-verify` bypasses the local gate, so GitHub push
protection remains the independent server-side control.

## Agentic scan

Copilot code review runs automatically on each pull request and again on every push to it. GitHub loads
`.github/instructions/*.instructions.md` files whose `applyTo` matches the changed files, and
[security-review.instructions.md](../.github/instructions/security-review.instructions.md) uses `applyTo: "**"`, so
its categories, AIProxyPlus trust boundaries, confidence threshold, and finding format apply to every change. That
file is the single definition of what the agentic scan looks for; edit it there and nowhere else.

For security-sensitive changes, authors should also run the dedicated, deeper Copilot CLI command against their
local changes before opening the pull request:

```text
copilot
/security-review
```

It also runs non-interactively with `copilot -p "/security-review" -s --no-ask-user` (validated with Copilot CLI
1.0.80). This is the dedicated command, not equivalent to a generic free-form security prompt.

Treat every agentic finding as advisory. It can produce false positives and miss vulnerabilities, so validate it
against the code and runtime context, and do not read an empty result as proof that a change is secure. Handle
findings about unpublished vulnerabilities according to repository policy.

Request the deeper review when a change affects authentication or authorization, external input handling,
cryptography or secrets, outbound network calls, database queries, file or process access, CI/CD permissions,
package or supply-chain configuration, or LLM prompts, tools, and agents.

### Why there is no security-review workflow

No GitHub Actions workflow runs the agentic scan. This is a public, personal repository, so the documented
short-lived `GITHUB_TOKEN` billing path for organization automation does not apply, and a workflow would otherwise
need a long-lived personal access token. Workflow logs and artifacts in a public repository are also unsuitable for
vulnerability details, and an AI step must not run against fork-controlled content with write access. Add a workflow
only if a safe short-lived credential, private auditable output, and fork-safe behavior can all be demonstrated.
The agentic scan must never approve, merge, push, or suppress deterministic findings.
