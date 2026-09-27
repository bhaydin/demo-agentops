# Initial prompt for Claude Code

Copy everything between the lines into Claude Code after placing `AGENTS.md`, `CLAUDE.md`, and `docs/` in the empty repository root.

---

You're starting a new repository: Swankers Coach, a conference demo that has to be ready by Thursday, October 1. Everything you need is in `AGENTS.md`, `CLAUDE.md`, `docs/ARCHITECTURE.md`, and `docs/BUILD-PLAN.md`. The architecture is locked. Your job right now is Phase 0 only.

Do this in order:

1. **Read** `AGENTS.md`, `docs/ARCHITECTURE.md`, and `docs/BUILD-PLAN.md` in full. Then tell me, in five bullets or fewer, what you understand the demo to be and which parts are intentionally vulnerable. I want to confirm we agree before any code exists.

2. **Check the toolchain.** Run `dotnet --info`, `azd version`, `git --version`, and `pwsh --version`. Report anything missing or older than what the plan needs.

3. **Verify packages.** Using the Microsoft Learn MCP server (ask me to connect it if it isn't available) and NuGet, find the current package IDs and latest stable or clearly labeled prerelease versions for:
   - Microsoft Agent Framework core
   - Microsoft Agent Framework Foundry hosting integration (the one exposing `AddFoundryResponses` / `MapFoundryResponses`)
   - Microsoft Agent Framework evaluation with Foundry evaluators (`FoundryEvals`)
   - The official C# MCP SDK (server with ASP.NET Core streamable HTTP, and client)
   - OpenTelemetry for ASP.NET Core, HttpClient, and OTLP export
   - xUnit
   
   Give me a table of package ID, version, stable or prerelease, and the source page. Flag anything prerelease. Do not add packages you could not verify.

4. **Propose the Phase 0 plan**: the exact files and projects you'll create, and the `Directory.Packages.props` contents. Wait for my approval.

5. **After I approve**, execute Phase 0 exactly as written in `docs/BUILD-PLAN.md`. Initialize git if it isn't already, and commit in small steps with conventional commit messages.

6. **Gate.** Run `dotnet build SwankersCoach.slnx` and `dotnet test`. Tick the Phase 0 boxes, record the verified versions in the plan, and give me the five-line summary. Then stop. Do not start Phase 1.

Two things I care about more than speed: never guess at an API that you haven't verified, and never write to the real MFL league.

---

## Codex kickoff (use after the Phase 0 gate)

Codex reads `AGENTS.md` automatically. Start it with:

> Read AGENTS.md and docs/BUILD-PLAN.md. Claim Phase <N> by adding "Codex" after its Owner line, then propose a plan for that phase only and wait for approval. Follow every hard rule in AGENTS.md, and stop at the phase gate.

Good candidates for Codex: Phase 1 domain models and tests, Phase 5 golden set and evaluators, Phase 6 Blazor UI. Keep Phase 2 (MCP server and the vulnerable paths) with one agent end to end.
