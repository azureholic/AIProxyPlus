# Copilot customizations

The agents, instructions, and skills in this directory were copied from
[github/awesome-copilot](https://github.com/github/awesome-copilot) at commit
`336af71f1b7d2e6e15a8a986ba79ca031a40549b` on 2026-08-03.

GitHub Copilot code review is automatically requested for pull requests through
the repository's `main` branch ruleset. Reviews use `.github/copilot-instructions.md`
as the repository-wide review guidance.

They were selected for this repository's .NET 10, ASP.NET Core Minimal API,
.NET Aspire, PostgreSQL/EF Core, React 19, TypeScript, Vitest, Docker, security,
architecture, documentation, and draw.io workflows. The upstream MIT license is
preserved in `awesome-copilot-LICENSE`.

## Instructions

- `aspnet-rest-apis.instructions.md`
- `containerization-docker-best-practices.instructions.md`
- `dotnet-architecture-good-practices.instructions.md`
- `playwright-typescript.instructions.md`
- `security-review.instructions.md`
- `self-explanatory-code-commenting.instructions.md`
- `update-docs-on-code-change.instructions.md`

## Agents

- `expert-dotnet-software-engineer.agent.md`
- `expert-react-frontend-engineer.agent.md`
- `implementation-plan.agent.md`
- `playwright-tester.agent.md`
- `se-system-architecture-reviewer.agent.md`

## Skills

- `aspnet-minimal-api-openapi`
- `csharp-async`
- `csharp-xunit`
- `documentation-writer`
- `dotnet-best-practices`
- `drawio`
- `multi-stage-dockerfile`
- `postgresql-code-review`
- `postgresql-optimization`
- `review-and-refactor`
- `webapp-testing`

## Security scanning

Security scanning is defined once in [docs/security-scanning.md](../docs/security-scanning.md). The only
customization file for it is `instructions/security-review.instructions.md`, which drives Copilot code review on
every pull request. The generic upstream security agent, skill, and one-time setup prompt were removed so there is
one definition of the agentic scan and so the project skill no longer shadows the built-in `/security-review`
command.
