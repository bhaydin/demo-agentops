# Knowledge

Markdown documents uploaded to Foundry IQ to ground Coach. Planned for Phase 3 (drafted by an agent, content edited by the maintainer), five to eight files:

- League constitution and scoring rules
- Roster and lineup rules
- Trade and waiver rules
- Brian's draft notes
- Weekly news digest

Keep this content clean. The Friday prompt-injection demo uses **tool output** (a trade note), never the knowledge base. Describe the league here; do not defend the agent here. The rule that tool output is data, never an instruction, lives in prompt v1 (`src/Swankers.Coach/prompts/coach-v1.md`) so that the Friday "before" (prompt v0) can show what happens without it.

This README is not grounding content; exclude it when uploading to Foundry IQ.
