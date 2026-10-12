# Copilot security review

GitHub Copilot CLI's `/security-review` command provides an additional AI-assisted review of the staged and
unstaged changes in the current worktree. It complements the repository's deterministic security controls; it
does not replace CodeQL, dependency review, Dependabot, secret scanning, tests, or branch rules.

The capability is experimental/public preview. Its findings are advisory, can contain false positives, and can
miss vulnerabilities. Validate every applicable finding against the code and runtime context before remediation
or a merge decision. An empty result is not proof that a change is secure.

## Pull-request review integration

The active `Automatic Copilot review for every pull request` repository ruleset requests Copilot code review for
each pull request and again after new pushes. GitHub Copilot code review loads matching files from
`.github/instructions`; because
[`security-review.instructions.md`](../.github/instructions/security-review.instructions.md) uses
`applyTo: "**"`, its security categories, trust boundaries, confidence threshold, and finding format apply to every
changed file in every automatic Copilot pull-request review.

This integrates security analysis into the existing PR review without running pull-request code, exposing Actions
secrets, or granting an AI reviewer write access. It remains advisory: maintainers must validate applicable
findings, while CodeQL and dependency review remain the required security checks.

For security-sensitive changes, the pull-request template also asks the author to run or request the dedicated
CLI `/security-review`. The dedicated command performs a deeper security-specific review of the local worktree; it
is complementary to, not identical with, native Copilot code review.

## Run a review locally

Use a current [GitHub Copilot CLI](https://docs.github.com/copilot/how-tos/copilot-cli) authenticated for an account
that can use Copilot:

```text
copilot login
copilot
```

From the interactive session opened in this repository, run:

```text
/security-review
```

GitHub Copilot CLI 1.0.80 exposes `/security-review` directly in `copilot help commands`; that version does not
document a separate experimental-mode setup command as a prerequisite. Because the feature is still preview,
verify the command shown by the installed CLI before use:

```text
copilot --version
copilot help commands
```

The local desktop login uses an OAuth browser flow and stores the resulting token in the system credential store
when available. Do not place access tokens in repository files, shell history, or committed configuration.
Handle reviewed source and findings according to repository policy, especially when changes contain credentials,
personal data, customer prompts, or unpublished vulnerability details.

## When to run it

Request a review for security-sensitive changes, especially changes to:

- authentication, authorization, browser sessions, or antiforgery handling;
- external input validation, database queries, or response rendering;
- cryptography, credential storage, Vault integration, or logging;
- outbound network calls, endpoint URL validation, or protocol forwarding;
- file access, temporary files, process execution, or shell commands;
- GitHub Actions permissions, package manifests, lockfiles, or supply-chain configuration;
- LLM prompts, tools, agents, retrieved content, or model-output handling.

Use the repository's
[security-review instructions](../.github/instructions/security-review.instructions.md) to keep findings focused
on high-confidence vulnerabilities and AIProxyPlus trust boundaries.

## Existing authoritative controls

- CodeQL scans C# and JavaScript/TypeScript on pull requests, pushes to `main`, and a weekly schedule using the
  `security-extended` query suite.
- Dependency review blocks pull requests that introduce vulnerabilities of moderate severity or higher.
- Dependabot checks NuGet and npm dependencies weekly; its pull requests receive dedicated build and test
  validation before trusted auto-merge automation can proceed.
- GitHub secret scanning and push protection are enabled in repository settings.
- The `main` ruleset requires both CodeQL language checks and dependency review.

These controls remain the authoritative merge checks. Copilot security review is not a required status check and
must not approve, merge, modify, or push code, suppress deterministic findings, or publish sensitive findings.

## Automation status

Dedicated non-interactive execution is supported. On 2026-10-12, the repository validated GitHub Copilot CLI
1.0.80 with:

```text
copilot -p "/security-review" -s --no-ask-user
```

This invocation dispatches the dedicated slash command in programmatic mode. It is not equivalent to a generic
prompt such as `copilot -p "Review these changes for security vulnerabilities"`.

No GitHub Actions workflow invokes the command. This is a public, personal repository, while GitHub's documented
short-lived `GITHUB_TOKEN` billing policy applies to organization-owned automation and requires
`copilot-requests: write`. A safe, private, auditable destination for potentially sensitive vulnerability details
has also not been configured; workflow logs and ordinary artifacts from a public repository are not suitable.
The repository therefore does not introduce a PAT, another long-lived secret, or a workflow that could expose
findings from fork-controlled content.

Automation should be added only if GitHub officially documents and the repository validates a dedicated
non-interactive security-review interface, safe short-lived authentication, least-privilege permissions,
private auditable output, and fork-safe behavior for this ownership model. Until then, run the dedicated command
manually. Do not create a long-lived personal access token for this preview capability.

Native Copilot code review remains separately enabled by the repository ruleset for pull requests. It is also
advisory and does not replace required checks. Its automatic security guidance comes from the repository
instruction file described above; the dedicated CLI command remains a separate optional review for
security-sensitive changes.
