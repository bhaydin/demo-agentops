# WorkplaceMeetingPrep corpus seed (fictional)

Everything here is invented for the WI.NET demo: Northwind Traders, the engagement, the people, and the dates. None of it describes a real client or a real person.

Seeding is a manual maintainer step (docs/BUILD-PLAN.md, W1). Send each item as its own email from the demo account to itself, in the order below, with the subject exactly as written. The `[NW-DPM]` tag bounds retrieval, so every subject needs it. The dates in the bodies are the fictional business dates; the real send times only have to keep this order.

Each body opens with its business date written out, as in "September 30, 2026". That date is authoritative for the engagement's chronology. The time a mailbox receives the message is transport metadata only: for a seeded mailbox it is the day of seeding, and it never decides which fact is the latest.

The ids in parentheses are the evidence ids used in the build plan's corpus table, in order of receipt. They are for cross-reference only and are not part of the emails.

## 1. Kickoff notes (E1)

Subject: `[NW-DPM] Kickoff notes`

> Kickoff notes, September 22, 2026.
> Attendees: Dana Okafor (Northwind sponsor), Priya Raman, Marcus Lindqvist.
> Planned pilot go-live: November 2, 2026.

## 2. Steering recap (E2)

Subject: `[NW-DPM] Steering committee recap`

> Steering committee recap, September 29, 2026.
> Decision: the pilot scope is limited to the finance data mart. The steering committee agreed and Dana Okafor confirmed it.

## 3. Security review action (E3)

Subject: `[NW-DPM] Security review summary`

> September 30, 2026.
> Action: Priya Raman will deliver the security review summary by October 9, 2026. It has not been delivered yet.

## 4. ERP extract risk (E4)

Subject: `[NW-DPM] ERP extract issue`

> September 30, 2026.
> Priya Raman raised a risk: the source ERP extract is missing ledger mappings for two business units. Nobody has been assigned to resolve it.

## 5. Go-live moved (E5)

Subject: `[NW-DPM] Go-live date change approved`

> October 1, 2026.
> Dana Okafor approved moving the pilot go-live from November 2 to November 16, 2026. This replaces the date in the kickoff notes.

## 6. Sandbox access complete (E6)

Subject: `[NW-DPM] Sandbox access request`

> October 1, 2026.
> Marcus Lindqvist confirmed the sandbox access request is complete. All four team members can sign in.

## 7. Fabric suggestion (E7)

Subject: `[NW-DPM] Working session notes`

> October 2, 2026.
> Marcus Lindqvist: We should consider Fabric after the pilot. It is worth a look once the finance data mart is live.

## 8. Revised statement of work (E8)

Subject: `[NW-DPM] Statement of work`

> October 2, 2026.
> Marcus Lindqvist will send the revised statement of work to Dana Okafor.

## 9. Acceptance sign-off question (E9)

Subject: `[NW-DPM] Acceptance criteria sign-off`

> October 2, 2026.
> Priya Raman asked: who signs off on the pilot acceptance criteria? We need a named approver before go-live.

Do not send a reply to item 9. The unanswered question is part of the corpus.
