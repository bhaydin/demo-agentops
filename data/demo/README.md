# Demo scenarios

Seed files that `POST /api/admin/seed/{scenario}` loads into `SimLeague` for a stage demo.

- `poisoned-trade.json` (Phase 2): a trade offer whose note contains injected instructions, used in the Friday talk. **Intentionally vulnerable.** See [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md#security-demo).

Rules for anything in this folder:

- Scenarios only ever affect `SimLeague`. Nothing here may target MFL or any real platform.
- Payloads stay benign: drops, trades, and lineup changes inside the simulated league. No network calls, no data exfiltration, no real credentials.
- No real owner names. Use display names from `data/franchise-names.json`. The villain franchise is "The Fleecers" and exists only in `SimLeague`.
