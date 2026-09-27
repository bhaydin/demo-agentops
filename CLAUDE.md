# CLAUDE.md

@AGENTS.md

The file above is the shared source of truth for every coding agent in this repo. This file adds Claude Code specifics only. If the two ever conflict, AGENTS.md wins; flag the conflict.

## How to work here

- **Plan before each phase.** Read the phase in `docs/BUILD-PLAN.md`, then present a short plan (files to create or change, packages, tests) and wait for approval before writing code.
- **Verify before you type an API.** Use the Microsoft Learn MCP server for Microsoft Agent Framework, Foundry hosted agents, Foundry evaluations, and the C# MCP SDK. If it is not connected, ask the maintainer to run:
  `claude mcp add --transport http microsoft-learn https://learn.microsoft.com/api/mcp`
  Cite the doc page in your phase summary for every non-trivial API you used.
- **Prove it works.** Run `dotnet build` and the relevant `dotnet test` before saying a task is done. Paste the result summary, not the full log.
- **Stop at the gate.** When a phase's checklist is complete, tick the boxes in `docs/BUILD-PLAN.md`, write a five-line summary (what changed, versions, open risks), and stop.

## Guardrails specific to Claude Code

- Do not read or print files under user-secrets, `.azure/`, or any `*.env`. Ask the maintainer for values instead.
- Do not run `azd up`, `azd deploy`, or anything that creates Azure resources without explicit approval in chat.
- Do not run `tools/Swankers.SnapshotCapture` against the live MFL API; the maintainer runs it.
- Keep edits inside the phase's scope. If you notice a problem elsewhere, note it in the summary instead of fixing it.
- The intentionally vulnerable paths described in AGENTS.md are part of the product. Preserve them and their markers.

## Environment

- Maintainer works in Visual Studio on Windows with PowerShell 7. Scripts are `pwsh`; keep them cross-platform.
- Solution format is `.slnx`. Open it in Visual Studio; build from the CLI with `dotnet build SwankersCoach.slnx`.
