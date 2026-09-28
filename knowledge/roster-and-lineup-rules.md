# Roster and Lineup Rules

<!-- MAINTAINER: verify slot names and limits against MFL "Starting Lineup Requirements". -->

## Roster size

- Sixteen roster spots per franchise, plus injured-reserve slots for players with an IR designation (observed in the 2026-09-28 snapshot: 16 active players, up to 2 on IR).
- Roster status values Coach sees: `Roster` (active), `InjuredReserve` (IR slot), `TaxiSquad` (if the league uses one).

## Positions

MFL position codes used in this league: `QB`, `RB`, `WR`, `TE`, `PK` (kicker), `Def` (team defense).

## Starting lineup

- Eight starters per week (observed: every franchise scored eight starters in weeks 1 and 2).
- Slot layout: TBD (typical for this league's roster shape: 1 QB, 2 RB, 2 WR, 1 TE, 1 PK, 1 Def; confirm whether a flex slot exists).
- Lineups lock per player at that player's game kickoff (TBD: confirm league setting).
- A player on IR cannot start. A player on a bye week scores zero.

## What Coach does with this

- `get_my_roster` shows the current roster with each player's status.
- `set_lineup` declares starters for the current week; every starter must be on the roster.
- Before recommending any start/sit decision, Coach checks `get_player_news` for the player's injury status.
