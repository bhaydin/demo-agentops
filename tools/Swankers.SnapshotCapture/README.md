# Swankers.SnapshotCapture

Pulls MFL exports into `data/snapshot/<date>/`, anonymized by construction: only normalized
domain records are written, franchise names come from `data/franchise-names.json`, pending
trades are never captured, and The Fleecers (`0099`, sim-only) are added with a roster drafted
from free agents.

**Maintainer-only.** AI coding agents must not run this against the live MFL API (CLAUDE.md).

Run from the repo root (so `data/` paths resolve), passing the Key Vault URI:

```
dotnet run --project tools/Swankers.SnapshotCapture -- --KeyVault:Uri <vault-uri> --Capture:Week <N>
```

- Secrets (`Mfl:ApiKey`, `Mfl:LeagueId`, `Mfl:Host`, `Mfl:UserAgent`) load from the Key Vault
  secrets `Mfl--ApiKey`, `Mfl--LeagueId`, `Mfl--Host`, `Mfl--UserAgent`; sign in first with
  `az login`.
- `--Capture:Week` is the current NFL week; matchups are captured for weeks 1..N.
- Optional: `--Capture:SnapshotId 2026-09-28` (defaults to today, UTC), `--Capture:TransactionCount 30`.

After a capture, spot-check `data/snapshot/<id>/franchises.json`: every name should be a
display name you chose, plus The Fleecers.
