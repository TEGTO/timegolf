# TimeGolf — Product Requirements Document (v2, turn-based)

Self-contained spec. A fresh session must be able to implement any milestone (§15) from this file alone. When code and PRD disagree, fix one of them in the same change — never leave drift.

v1 (real-time multi-ball branching) is preserved in git commit `2df9a22`; v2 replaces it.

---

## 1. Vision

Turn-based multiplayer browser mini-golf with a **rewritable shared past**. Every hit of every player is recorded on one shared timeline. On your turn you may hit from the present — or go back and **re-do one of your own past hits**. The whole future is then re-simulated: every later recorded hit (yours and everyone else's) fires again with the same direction and power from wherever the balls now are. One changed shot can knock rivals off course or out of the hole.

- Lobbies: enter a name, pick a lobby from the list (or create one: name, optional password, max players), press Ready; the game starts when everyone in the lobby is ready.
- Match = 3 rounds; each round a fresh procedurally generated level; 10 turns per player per round.
- Score = hits + walking distance to the hole. No time component.
- Small, optimized codebase. Looks 2.5D (orthographic camera, lights, shadows, procedural textures).

## 2. Glossary

| Term | Meaning |
|---|---|
| Turn | One action by the current player: present hit, replace, or skip. Each player gets `turnsPerPlayer` (10) turns per round, taken one by one round-robin; the turn counter shows the rotation number (1–10). |
| Hit record | `{index, playerId, dir, power}` stored on the timeline. Replay fires it again with the same dir/power. |
| Timeline | Ordered list of hit records of the round. Skips are not recorded. |
| Checkpoint `i` | World state (all ball positions, holed flags) right before hit `i`. Checkpoint 0 = spawn positions. "Present" = state after the last hit. |
| Replace | Overwrite dir/power of one of your own past hit records, then re-simulate from its checkpoint to the end. |
| Void hit | A recorded hit whose ball is already holed when it replays → no movement, not counted. |

## 3. Rules

### 3.1 Turns
1. Round start: order = active non-spectator players sorted by join order, rotated by `round − 1` (start player rotates). One ball per player at a spawn slot (checkpoint 0; slots reused modulo count beyond 16+ players).
2. Turns go round-robin through the order; one full pass through the order = one rotation; a round has `turnsPerPlayer` (10) rotations. `TurnDto.turn` = rotation number, `turns` = 10. Skips count as that player's turn.
3. Current player has `turnSeconds` (60) to act: **Hit**, **Replace** or **Skip**. Timeout → auto-skip. Inactive (disconnected) player → auto-skip immediately. (only `TurnSkipped {reason:'inactive'}`, no `TurnStarted`). Every skip waits the playback gap before the next `TurnStarted`.
4. After an action the server computes the result instantly, broadcasts it, and waits for playback (§3.3) before starting the next turn.
5. **Holed players sit out**: a player whose ball is holed in the present gets no turns (silently passed over, no `TurnSkipped`; their seat in the rotation is simply skipped). If a later replay un-holes them, they get turns again from the next rotation.
6. Round ends after the last turn's playback, or as soon as (at a turn boundary) every active player in the order is holed → round results.

### 3.2 Actions
- **Hit (present)**: own ball must not be holed. Appends a hit record; simulate it from the present.
- **Replace(hitIndex, dir, power)**: `hitIndex` must be the caller's own, non-void record. Its dir/power are overwritten; simulate from checkpoint `hitIndex` through all later records in order. Hit count unchanged. (Holed players never have a turn, so they can't replace until un-holed.) Any own record may be replaced any number of times (no locks).
- **Skip**: nothing recorded.

### 3.3 Simulation & playback
- Simulating a record: launch its owner's ball (`velocity = dir · power · maxHitSpeed`), step physics (§4) until **all** balls rest or `maxHitSimSeconds` (20) elapsed (then all velocities zeroed). Record 20 Hz tracks of every ball that moved, bounces, and holed flags.
- If the owner's ball is holed at the record's checkpoint → record is **void** (no sim).
- Holed balls are removed from collisions.
- Deterministic: same level + same records → same result (server-only sim, fixed dt). Required for replay.
- Playback speed: present hit and the replaced hit itself at 1×; rewritten later hits at `replaySpeed` (3×). Next turn starts after total playback + 0.4 s gap.

**Example**: T1 Alice hits (A1), T2 Bob hits (B1) and knocks Alice's ball, T3 Alice replaces A1 with a softer shot → sim A1' from checkpoint 0 (index of A1), then B1 replays with Bob's original dir/power — but Alice's ball is somewhere else now, so Bob's ball may miss it and travel further (or hit something new). If B1 had holed Bob's ball before, it may not anymore.

### 3.4 Scoring (higher is better)
- Hits = player's non-void records in the current timeline.
- Round score = `ceil(100 / hits)` if holed (1 → 100, 2 → 50, 3 → 34, 7 → 15); not holed → `ceil(100 / (hits + distanceToHole))` (walking-path distance in tiles, §8 step 9, rounded to 1 decimal).
- Round end: each order player active at round end adds their round score to match points. Anyone who missed the round (inactive at round end, spectating) gets `max(0, worstScoreOfRound − 2)`; players joining mid-match are credited the sum of that value for every already-finished round.
- Match winner after round 3 = highest points among active players (ties shared); nobody > 0 → no winner.

### 3.5 Phases (per lobby)
| Phase | Duration | Next |
|---|---|---|
| `Lobby` | until every active player is Ready (≥ 1 player) | new match round 1 (points reset, inactive purged, ready flags cleared) |
| `Playing` | 10 rotations, or all active players holed | `RoundResults` |
| `RoundResults` | `roundResultsSeconds` (5) | next round, or `MatchResults` after round 3 |
| `MatchResults` | `matchResultsSeconds` (15) | `Lobby` (ready flags cleared; last match results kept for display) |

No active players while a game runs (everyone disconnected) → game kept frozen; reconnect within `emptyLobbySeconds` resumes it, otherwise the lobby is deleted. Explicit **Leave** (Quit) by the last active player deletes the lobby. Returning to `Lobby` purges inactive players and clears points, votes, ready and spectator flags.

**Restart vote**: any active player may toggle a restart vote (any phase except `Lobby`). When votes > half of active players (spectators included) → game ends immediately → `Lobby` (votes and ready flags cleared). Disconnect clears that player's vote.

### 3.6 Players, identity, disconnect
- Join: `name` (1–16 chars trimmed) + `playerKey` (UUID per tab in `sessionStorage`; name in `localStorage`). Page reload with a saved name, session key and session `lobbyId` → auto-rejoin the same lobby (same player; password remembered in session). Main menu only on first visit or after **Quit** (leaves the lobby, clears session key/lobby). Duplicate name among active players → `#2`, `#3`… Color: `g = (index · 137.508°) mod 360 · 260/360`, hue = `g < 70 ? g : g + 100` (skips grass greens 70°–170°), `hsl(hue, 70%, 55%)`.
- Joining during `Playing` **before the round's first hit** → enters the round immediately (appended to order, spawn ball; server re-emits `RoundStarted` with updated order/balls while the current turn continues). After the first hit → **spectator** (sees everything, no ball, not in order) until next round; points 0. Joining during results phases → enters next round normally.
- Disconnect → inactive: ball stays (collides, can be knocked), points kept but hidden from leaderboard, turns auto-skipped, can't win. Reconnect with same `playerKey` → active again with same id/color/points/ball. If it's their turn, the turn timer restarts (`endsAt = now + turnSeconds`, `TurnStarted` re-emitted to others, new turn in `Welcome.turn`).

### 3.7 Lobbies
- A lobby = `{id, name (1–24 chars trimmed), password (optional, plain, in memory), maxPlayers (1–16)}`, each with its own independent match (§3.1–3.6 apply per lobby). Any number of lobbies.
- **Create**: from the main menu (name, optional password, max players); the creator joins it immediately. No host privileges.
- **Join**: from the lobby list. Rejected if wrong password (`Wrong password`) or active players ≥ maxPlayers (`Lobby is full`; a reconnecting existing player is always allowed). Joining while a game runs follows §3.6 (enter current round before its first hit, otherwise spectate and play from next round with 0 points).
- **Ready room** (`Lobby` phase): players toggle Ready (can unpress). Game starts the moment all active players are ready. Disconnecting in the ready room removes the player from the lobby.
- **Leave** (in-game Quit or ready-room Quit): player removed from the lobby (as a disconnect during a game → inactive, purged at match end).
- Lobby with no active players is deleted after `emptyLobbySeconds` (60) — grace for page reloads; deleted at once when the last player Quits (Leave).
- Lobby list shows: name, lock icon if password, `players/maxPlayers` (active), phase (`waiting` / `in game`). Pushed live to clients on the main menu.

## 4. Physics (server only)

Plane `(x, z)`, 1 unit = 1 cell. Same model as v1; fixed step `dt = 1/120` (60 Hz × 2 substeps) used only inside instant simulation.

| Constant | Default |
|---|---|
| `ballRadius` | 0.14 |
| `maxHitSpeed` | 14 u/s (`maxHitSpeed · dt < ballRadius` → no tunneling) |
| `rollingDecel` / `sandDecel` | 1.6 / 6.5 u/s² |
| `slopeAccel` | 2.4 u/s² (friction also applies → net 0.8 downhill) |
| `wallRestitution` / `ballRestitution` / `bumperRestitution` | 0.7 / 0.9 / 1.3 (clamped to max speed) |
| `restSpeed` | 0.06 u/s (rest only off slopes) |
| `holeRadius` / `holeCaptureSpeed` | 0.22 / 3.5 u/s (faster skims over) |
| `trackHz` | 20 (track point every 6 steps) |
| `maxHitSimSeconds` | 20 |

Per step: integrate moving balls (slope accel, friction, speed clamp) → ball–ball (grid broadphase, elastic w/ restitution; resting ball hit becomes moving) → walls/pillar edges/bumpers (push out, reflect) → hole capture → rest check. Bounces with impact ≥ 0.4 u/s recorded `(tMs, x, z, speed, kind 0 wall | 1 ball | 2 bumper)`.

### 4.1 Windmill (moving obstacle, deterministic)
- Classic mini-golf windmill across a straight corridor. `(x, z)` = center of its tunnel; `axis` 0 = ball travels along x, 1 = along z.
- Base: block 3 wide (across travel) × 1.2 deep (along travel) centered on `(x, z)`, with a 1-wide tunnel through the middle → physics = two solid 1×1.2 side blocks (pillar-like walls) + tunnel side walls.
- 4 blades, radius 1.4, rotate in the vertical plane facing the incoming ball; blade k at `θ + k·π/2`, `θ(t) = phase + windmillOmega · t` (rad, ω = 1.6 rad/s); `θ = 0` = a blade pointing straight down.
- **Gate**: the tunnel is blocked by wall segments across BOTH mouths (±0.6 along the axis from `(x, z)`, closed together) whenever `|IEEERemainder(θ, π/2)| ≤ windmillBladeHalfAngle` (18°), i.e. a blade points down; the blade has no velocity in physics (plain reflect, wall restitution).
- **World clock** `t`: starts at 0 per round, advances only during simulation. Record `k` simulates from world time `T_k` = sum of `durationMs` of all earlier non-void records; between turns the world (and windmill) is frozen at `T_end` (last record `worldTimeMs + durationMs`, 0 if empty), so players can plan; viewing checkpoint i shows `timeline[i].worldTimeMs`. Client draws blades on the camera-facing side (no travel direction on the wire). Replace re-sims from `T_k` → later records get new `T`. Deterministic.

## 5. Tunables (`appsettings.json` → `Game`)

`RoundsPerMatch 3`, `TurnsPerPlayer 10`, `WindmillOmega 1.6`, `WindmillBladeHalfAngleDeg 18`, `TurnSeconds 60`, `ReplaySpeed 3`, `PlaybackGapSeconds 0.4`, `MaxHitSimSeconds 20`, `RoundResultsSeconds 5`, `MatchResultsSeconds 15`, `EmptyLobbySeconds 60`, `MaxLobbyPlayers 16`, `SpawnSpacing 0.6`, `SpawnMargin 0.05`, `TickRate 60`, `Substeps 2`, physics constants of §4. `GameOptions` record, `ValidateOnStart`.

## 6. Level model

Coordinates: cell `(cx, cz)` covers `[cx, cx+1] × [cz, cz+1]`. x → right, z → down on screen; three.js `y` up.

```ts
type LevelDto = {
  seed: number
  round: number            // 1-based, drives difficulty
  width: number            // cells
  height: number           // cells
  cells: string            // row-major, width*height chars (see legend)
  heights: number[]        // per cell, tier index (0 = top); visual y = tier * -0.5
  walls: [number, number, number, number][]   // cell-boundary segments x1,z1,x2,z2 (pillar edges excluded)
  pillars: {x: number, z: number, w: number, d: number}[] // AABB obstacles (server physics adds their edges as walls)
  bumpers: {x: number, z: number, r: number}[]
  windmills: {x: number, z: number, axis: 0 | 1, phase: number}[] // §4.1
  start: {x: number, z: number, w: number, d: number}     // start zone rect
  spawns: [number, number][]                               // spawn slots
  hole: {x: number, z: number}
  palette: number          // theme index
}
```
Cell legend: `.` void · `g` grass · `s` sand · `>` slope +x · `<` slope −x · `v` slope +z · `^` slope −z.

Walls = merged boundary edges between floor and void, and between floor cells with different tier where neither is a slope (cliff). Slope cells connect tier `n` to `n+1` downhill along their direction.

## 7. Network protocol (SignalR, JSON camelCase, hub `/hubs/game`)

Same-origin always (nginx in prod, rsbuild proxy in dev).

### Client → Server
| Method | Args | Returns |
|---|---|---|
| `ListLobbies` | — | `LobbyInfoDto[]` (also subscribes the connection to `Lobbies` updates while not in a lobby) |
| `CreateLobby` | `name, password \| null, maxPlayers` | `string` lobbyId (HubException on invalid) |
| `Join` | `lobbyId, password \| null, name, playerKey` | `WelcomeDto` (HubException: invalid name/key, `Lobby not found`, `Wrong password` (also checked on reconnect), `Lobby is full`; client matches messages with `includes`, SignalR wraps them) |
| `Leave` | — | void (back to menu; connection receives `Lobbies` again) |
| `SetReady` | `ready: boolean` | void (only in `Lobby` phase) |
| `Hit` | `dirX, dirZ, power` | void |
| `Replace` | `hitIndex, dirX, dirZ, power` | void |
| `Skip` | — | void |
| `VoteRestart` | — | void (toggles caller's vote) |
| `Aim` | `viewIndex: number \| null, dirX, dirZ, power: number \| null` | void — live aim of the current-turn player (§10.2); ignored from anyone else, during playback or outside `Playing`; server keeps ≤ 20 msgs/s per connection |

A connection is in at most one lobby; game methods act on the caller's lobby. Invalid action → `ActionRejected {reason}` to caller. Reasons: `Not joined`, `Not your turn`, `Round is not in play`, `Invalid hit`, `Ball is holed`, `Hit not found`, `Not your hit`, `Hit is void`.

### Server → Client
| Event | Payload | When |
|---|---|---|
| `TurnStarted` | `TurnDto {turn, turns, playerId, endsAt}` | turn begins (after previous playback) |
| `TurnSkipped` | `{turn, playerId, reason: 'skip' \| 'timeout' \| 'inactive'}` | skip |
| `TimelinePlayed` | `{fromIndex, replaced, hits: HitPlayDto[]}` | after Hit (one entry = new index) or Replace (entries `fromIndex..end`) |
| `RoundStarted` | `{round, rounds, level, order, balls, players}` | round begins; re-sent (same round) when a player joins before the first hit — keep current turn |
| `RoundEnded` | `{round, rows: RoundRowDto[]}` | → RoundResults |
| `MatchEnded` | `{rows: MatchRowDto[], winnerIds}` | → MatchResults |
| `Phase` | `{phase, phaseEndsAt, round, rounds}` | phase change (`phaseEndsAt` 0 when none) |
| `Players` | `PlayerDto[]` | any player/score change |
| `ActionRejected` | `{reason}` | invalid action |
| `AimUpdated` | `AimDto {playerId, viewIndex: number \| null, dirX, dirZ, power: number \| null}` | relayed to the other connections of the lobby (not the sender); transient, not in `Welcome` |
| `Lobbies` | `LobbyInfoDto[]` | to menu connections (not in a lobby) whenever any lobby is created/deleted or changes players/phase |

```ts
type BallPosDto = {playerId: string, x: number, z: number, holed: boolean}
type TrackDto = {playerId: string, points: number[]}           // flat [x0,z0,x1,z1,…] at 20 Hz from hit start; last point = rest/hole
type HitPlayDto = {index: number, playerId: string, dirX: number, dirZ: number, power: number, void: boolean,
                   start: BallPosDto[],                         // checkpoint `index` (all balls)
                   tracks: TrackDto[],                          // balls that moved
                   bounces: number[][],                         // [tMs, x, z, speed, kind]
                   end: BallPosDto[],                           // all balls after
                   durationMs: number,                          // sim duration (0 if void)
                   worldTimeMs: number}                         // world clock at record start (§4.1)
type TurnDto = {turn: number, turns: number, playerId: string, endsAt: number}
type LobbyInfoDto = {id: string, name: string, locked: boolean, players: number, maxPlayers: number, phase: string} // phase = Phase name; client shows `Lobby` as "waiting", others "in game"
type PlayerDto = {id, name, color, points, hits, holed, distance: number | null, score: number | null, active, spectator, restartVote, ready}
type RoundRowDto = {playerId, name, color, hits, holed, distance: number | null, score, points}
type MatchRowDto = {playerId, name, color, points}
type WelcomeDto = {lobby: LobbyInfoDto, lastMatch: MatchEndedDto | null, playerId, serverTime, phase, phaseEndsAt, round, rounds, level: LevelDto | null, order: string[],
                   balls: BallPosDto[],                         // present state
                   timeline: HitPlayDto[],                      // full current timeline (lets late joiners/spectators scrub)
                   turn: TurnDto | null,                        // null during playback / non-Playing
                   players: PlayerDto[],
                   config: {ballRadius, holeRadius, maxHitSpeed, replaySpeed, playbackGapMs, windmillOmega, windmillBladeHalfAngle /*rad*/}}
```
- `ballId` = `playerId` (one ball per player). `LevelDto` as in §6.
- Coordinates rounded to 3 decimals on the wire; the server keeps full precision internally.
- Times are server unix ms; client keeps a smoothed `offset = serverTime − Date.now()`.

## 8. Procedural level generation (server)

Goal: random but always beatable and "designed-looking" (constrained composition of hand-tuned pieces, not raw noise).

1. **Seeded RNG** (`System.Random(seed)`), seed = random per round. Same seed → same level (test-friendly).
2. **Module grid**: 7×5 modules, each module = 6×6 cells → level 42×30 cells max (trimmed to used bounds + 1 cell margin).
3. **Path**: self-avoiding random walk on the module grid from a random edge module, length `L = clamp(4 + round + rand(0..2), 5, 10)`. Turn bias: 60% keep direction, 40% turn. Dead end → exhaustive backtracking (always succeeds for L ≤ 10 on 7×5).
4. **Pieces**: each path module gets entry side & exit side (straight or bend). First module = **Start** (full 6×6 grass, no obstacles). Last = **Hole** (open or corridor end; hole at random interior cell with ≥1.5 clearance from walls). Middle modules pick a weighted piece; weights shift with `round` (harder later):

| Piece | Straight | Bend | Content |
|---|---|---|---|
| Open | ✔ | ✔ | full module floor |
| Corridor | ✔ | ✔ | band width 2–4 centered entry→exit (L-shape on bends) |
| Chicane | ✔ | | corridor width 4 + alternating wall stubs from both sides (mirror pattern) |
| Plaza | ✔ | ✔ | full floor + one symmetric pillar pattern (center block, 2 or 4 mirrored posts, diamond) |
| Bridge | ✔ | | band width 2 (narrow), raised look |
| Bunker | ✔ | ✔ | full floor + ONE 6-cell oval sand bunker (outer corner of a bend, or off-axis against a side wall on straights), outside the module border ring; openings forced to width 2 |
| Ramp | ✔ | | corridor width 3–4: 2 rows grass (tier n) → 2 rows slope (tier n) → 2 rows grass (tier n+1); later modules tier+1 |
| Bumpers | ✔ | ✔ | full floor + 3 bumpers r 0.35 in a triangle or line pattern centred on the path |
| Windmill | ✔ | | corridor width 3 + windmill (§4.1): along the path at module center, across at the corridor center |

   **Design rules (consistency)**: every obstacle comes from a named pattern (no random scatter); patterns are symmetric about the path axis unless the piece is a bend; sand only as whole bunkers (no single random cells), ≤ 1 bunker per module and ≤ 1 per 3 modules on average; no two identical pieces adjacent; ≥ 1 "feature" piece (Ramp, Windmill, Bumpers, Bunker, Chicane, Plaza) per level, windmill from round 1 with rising weight; the hole module is clean (no obstacles within 1.5 of the hole).
   Entry/exit openings: centered 2–4 cells on the shared module edge; adjacent pieces both use the narrower opening. Modules touching on the grid but not consecutive on the path get a void border row (no shortcuts). Start module exit edge is trimmed to the shared opening.
5. **Rasterize** into `cells`/`heights`, build walls (merge colinear unit edges), pillars, bumpers.
6. **Validate**: BFS over a 0.25-unit grid; a node is passable if a ball of `ballRadius + 0.02` fits (no wall/pillar/bumper/windmill-base overlap, on floor; windmill gate treated as open). Must reach hole from start zone center. If unreachable → remove obstacles of the blocking module(s) one by one; still failing → regenerate with `seed + 1`.
7. **Palette**: `rand(0..4)` theme, all bright daytime: Meadow, Beach, Autumn, Candy, Snow (tint for grass/sand/walls/sky).
8. Spawn slots: grid with `SpawnSpacing` inside start zone with ≥0.4 wall clearance, shuffled.
9. **Distance field**: after generation, Dijkstra from the hole over the same 0.25 grid (8-neighbour, passable nodes only) → `level.DistanceTo(p)` = walking-path distance in tiles (field value of nearest passable node + offset to it; within 1 tile of the hole = straight line, so 0 at the hole). Used for scoring (§3.4).

## 9. Server architecture

Single project `server/TimeGolf` (.NET 10). In memory, no DB, no auth.

| File | Responsibility |
|---|---|
| `Program.cs` | DI, SignalR JSON camelCase, `/health`, `GameOptions` binding |
| `GameHub.cs` | `Hub<IGameClient>`: `ListLobbies`, `CreateLobby`, `Join`, `Leave`, `SetReady`, `Hit`, `Replace`, `Skip`, `VoteRestart` (routed to caller's lobby), disconnect |
| `IGameClient.cs` | typed client; method names = §7 event names |
| `GameLoop.cs` (+`.Log.cs`) | `BackgroundService` ~20 Hz: `lobbyManager.Step(now)` + `OutboxDispatcher.Dispatch(lobbyManager.Drain())` |
| `OutboxDispatcher.cs` (+`.Log.cs`) | one send chain per lobby (and one for menu connections): ordered within a lobby across ticks, concurrent across lobbies, failures logged, never blocks the loop |
| `Dtos.cs` | wire records |
| `Game/LobbyManager.cs` | lobbies (create/list/join/leave), connection → lobby map, empty-lobby cleanup, `Lobbies` broadcasts; steps matches in parallel (`Parallel.ForEach`, each match under its own lock); `Drain` → per-lobby `OutboxBatch`es |
| `Game/Match.cs` | one per lobby: phases (incl. ready room), players, order, turns, actions, scoring; outbox; `System.Threading.Lock` |
| `Game/AimThrottle.cs` | per-connection rolling 1 s limit (20) for `Aim` |
| `Game/Phase.cs`, `LobbySettings.cs`, `Outgoing.cs`, `OutboxBatch.cs`, `LobbyException.cs` | small shared types |
| `Game/Simulation/*` | `Timeline` (hit records, checkpoints, deterministic `Simulate(from)`), `Physics` (§4 + bounces), `Ball`, `BallState`, `HitRecord`, `HitResult`, `Track`, `Bounce`/`BounceKind`/`TimedBounce` |
| `Game/Levels/*` | `Level` (data + queries), `LevelWalls` (wall merging), `Surface` (+`SurfaceExtensions`), `Pillar`, `Bumper`, `Windmill`, `LevelGenerator`, `LevelGenValidator` (BFS + distance field) |
| `Game/Geometry/*` | `Vec2`, `Wall` (closest point / distance), `Rect` |
| `Game/Scoring.cs`, `Game/Mapping.cs`, `Game/Player.cs` | |

`Match` and `LobbyManager` take `now` as a parameter and emit typed `IGameClient` calls (static lambdas + payload) into an outbox → no SignalR dependency, unit-testable (tests use a recording fake client). One type per file; tunables in `GameOptions`, other numbers as named constants.

## 10. Client architecture

`client/` — Rsbuild, React 19, TS, three.js (plain), zustand, `@microsoft/signalr`, plain CSS split in `styles/{base,menu,room,game,overlays}.css`. Each module keeps its constants at the top (names with units); shared ones in `constants.ts` / `render/constants.ts`. Bright daytime look: Lilita One (display) + Nunito (UI) from Google Fonts, light/white translucent in-game panels, white cards for results. Tab title "TimeGolf", `public/favicon.svg` (ball + red flag).

| File | Responsibility |
|---|---|
| `input.ts` | declarative binding table (§12 controls) → intents; keyboard on `window` (ignored in inputs), pointer/wheel/contextmenu on the canvas; 6 px click slop (click never shoots); handles chorded mouse buttons (RMB pressed during an LMB drag) |
| `constants.ts`, `serverClock.ts` | shared UI constants; server clock offset (`syncClock`/`serverNow`) |
| `net.ts` | single connection opened on the menu, handlers registered before Join, `ListLobbies` + `Lobbies` updates, `createLobby`/`join`/`leave`/`setReady`, game actions; auto-rejoin of the session lobby on load and after reconnect (failure → menu + toast) |
| `session.ts` | name (`localStorage`) / playerKey, lobbyId, lobby password (`sessionStorage`) |
| `store.ts` | zustand: me, players, phase, round, order, turn, timeline, viewed checkpoint, aim mode, changed future (previous path after a replace, from the first divergence), hovered marker, watched aim |
| `playback.ts` | queue of `HitPlayDto`; plays tracks (lerp, speed 1×/3×) against a real-time deadline (flushes to final state when the tab is hidden or lags > 400 ms), bounce/hit/hole sfx, hole sink; on `replaced` jumps world to `start` of first entry |
| `render/Path.ts` (+`Badges.ts`) | my ball's future path colored by hit with `#n` badges (DOM overlay in `Badges.ts`), rest ring, changed-future ghost + `#i · Name` badge, marker-hover highlight; hidden during playback |
| `render/Windmill.ts` | windmill model (stone house, tunnel, roof, 4 lattice sails) animated by world clock |
| `render/Watch.ts` | current player's live aim seen by others: ghost ball at their viewed checkpoint + their arrow eased between updates |
| `render/Bunker.ts` | sand cells drawn as grass + one rounded, soft-edged decal per bunker (built once per level) |
| `render/Scene.ts` | orchestration + intent methods (`beginAim/dragAim/endAim/cancelAim`, `pan`, `rotate`, `rotateBy`, `zoomBy`, `focus`); no DOM listeners |
| `render/Camera.ts`, `Lights.ts` | `CameraRig` (orbit yaw, zoom, pan, follow, picking); hemisphere + sun + shadow-frustum fit |
| `render/LevelMesh.ts` (+`Terrain.ts`, `Obstacles.ts`, `Hole.ts`) | level assembly: tiers/ramps/chevrons/cliffs + `heightAt`; walls/pillars/bumpers; cup/flag |
| `render/Balls.ts`, `Aim.ts`, `palettes.ts`, `textures.ts`, `constants.ts` | balls (positioned by playback or viewed checkpoint), aim arrow, palette data, canvas texture painters, shared render constants |
| `ui/MainScreen` (+`Scenery`, `LobbyRow`, `CreateLobbyForm`, `useFlash`; name + lobby list + create), `ReadyRoom`, `Hud`, `Leaderboard`, `Timeline`, `Results`, `Toast` | DOM overlays |

### 10.1 Timeline view & replace flow
- Timeline bar = one marker per hit record, colored by player, numbered; void dimmed; skips not shown. Plus "Now" at the end.
- Click marker `i` (or ←/→) → **view checkpoint i**: world shows `timeline[i].start` (static), marker highlighted. Esc / "Now" → present.
- On your turn, viewing one of your own non-void records → "Re-do hit #i" mode: your ball at that checkpoint is aimable; the original shot's direction/power shown as a faint arrow; release → `Replace(i, …)`.
- Viewing the present on your turn (ball not holed) → normal aim → `Hit`.
- Viewing checkpoint i (incl. re-do mode): my ball's future path through records i..end is drawn on the ground — each segment colored by the player whose hit caused it (solid = my hit, dashed = knocked by someone else's hit) with a small numbered badge `#n` (hit number) where the segment starts, ring at the final rest point. Other players' paths not shown.
- **Changed future**: when a replace changes my ball's path, my previous path (from the first divergence) is kept as a faint grey ghost; it is shown only while viewing a past checkpoint at or before the replaced hit, next to the new attributed path, with a badge naming the hit that changed it (`#3 · Bob`). Cleared on the next timeline change. **Viewing the present (Now) shows no paths at all** — a plain continuation stays uncluttered. Hovering a timeline marker highlights the path segments caused by that hit.
- While playback runs, input is disabled and the view follows playback.

### 10.2 Watching the current player
- The current-turn player's client sends `Aim` whenever their viewed checkpoint changes (`viewIndex` = viewed record index, `null` = Now) and while aiming (throttled to 10/s): `dirX/dirZ/power` = current aim, `power = null` when not aiming (cancel/release). Release/cancel always sends `power: null` (just before `Hit`/`Replace` too, so a too-weak release clears watchers' arrows); receivers also clear on `TimelinePlayed`. Invalid/out-of-turn `Aim` is dropped silently (no `ActionRejected`).
- Other clients show it live (cleared on `Welcome`, `TurnStarted`, `TurnSkipped`, `TimelinePlayed`, new-round `RoundStarted`; a same-round `RoundStarted` keeps it; late messages from a non-current player are ignored):
  - **Timeline**: a small dot in that player's color (white ring) above the marker they're viewing (or above `Now`).
  - **World**: if they view a past checkpoint, a translucent ghost of their ball at its position in `timeline[viewIndex].start`, only when their viewed moment differs from mine (my own view of the world is unchanged); their aim arrow (their color, semi-transparent, same power scale as mine) from that ball/ghost while `power != null`.
  - Camera keeps following the current player's (ghost) ball unless I panned.
- Spectators see the same.

## 11. Rendering (2.5D)

- `OrthographicCamera` offset `(8, 12, 10)` rotated around the target by a yaw angle (Q/E = ±90° eased, middle-mouse drag = free), wheel zoom 0.5–2.5×, RMB pan, F refocus; follows the moving ball during playback; on my turn my own ball, on others' turns the current player's (ghost) ball, unless I panned.
- Sky-gradient background, white hemisphere light + warm sun; `PCFShadowMap` (radius 2, map 4096) with the light frustum fitted to the level's bounding box and near-zero bias (shadows touch their casters); cliffs cast too. Tiers with lighter cliff sides; slopes = tilted quads with grass shaded darker downhill + one small white chevron per cell pointing downhill. Procedural CanvasTextures per palette (grass stripes, sand, stone, wood). Walls thin boxes, pillars, bumpers, hole cup + constant red flag on white pole.
- Balls: `InstancedMesh`, per-player color, ring under own ball, current-turn player's ball pulses. Inactive desaturated.
- Checkpoint view: balls at checkpoint positions; replace mode shows the original shot as a faint arrow.
- Aim: drag-back arrow from own ball, power = drag length / 3 units.
- No per-frame allocations in the render loop.

## 12. UI / UX & sound

- **Main screen**: big "TimeGolf" title (no subtitle/footer), pill name input, then the live lobby list (rows: name, 🔒 if locked, `players/max`, `waiting`/`in game`, Join; locked → small password prompt) and a `Create` button opening a compact form (name, password optional, max players 1–16, default 8). Over the CSS sky/sun/clouds/hill scene (respects reduced motion). Empty list → just the Create button. Full lobby → disabled `Full` button. Empty lobby name → `<player>'s game`. Empty player name → name field shakes/focuses. Server unreachable → `Offline · Retry`.
- **Ready room** (`Lobby` phase overlay, white card): lobby name, players with ready ticks, last match results (if any), big `Ready` toggle, Quit. Course view hidden/dimmed behind.
- **Controls panel top-left**: mute, `Restart n/needed` toggle (needed = floor(active/2)+1), Quit (`Leave`, clear session key/lobby, back to menu). Only Quit returns to the menu.
- **HUD top-center**: `Round 1/3 · Turn 5/10 · Alice · 0:42` ("Your turn" highlighted). During playback: `Alice's shot` for a present hit, `Alice changed hit #3` for a replace (no word "replaying"); just `Round x/y` in the gap. Skip button on your turn.
- **Leaderboard top-right**: 50% opacity (hover 1), active non-spectators: place, dot, name, live round score (`⛳ 50` if holed, else `16`); markers/labels number hits as `index + 1`, points.
- **Timeline bar bottom**: markers (§10.1) + "Now" button + small `Re-do #i` chip in re-do mode. No instructional text or key hints anywhere.
- **Results**: white card table — round: place, name, hits, distance (or `holed`), +points; match: winner + points; countdown.
- **Spectator banner**: "Spectating until next round".
- **Toasts**: `ActionRejected`, `TurnSkipped` (timeout).

| Input | Action |
|---|---|
| LMB drag own ball (your turn) | hit / replace |
| Esc / RMB while aiming | cancel aim; Esc otherwise → present |
| Click marker, ←/→ | view checkpoint |
| S | skip (your turn) |
| Wheel / RMB drag / F | zoom / pan / refocus |
| Q / E, MMB drag | rotate camera (quarter turns eased / free) |
| M | mute |

**Sound** (`sfx.ts`, Web Audio, `/sfx/{hit,hole,switch,wall}.mp3`, oscillator fallback): `hit` on each played record, `wall` per bounce (pitch by kind, volume by speed & distance, max 4 per 50 ms), `hole`, `switch` on checkpoint change (throttled 120 ms) and on your turn start. M mute persisted.

## 13. Deployment

```
docker-compose.yml
  backend:  build server/TimeGolf/Dockerfile (context repo root), expose 8080, healthcheck curl /health, restart unless-stopped,
            env ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Production}
  frontend: build client/Dockerfile (context client), nginx:alpine, ports "${PORT:-8080}:80",
            depends_on backend service_healthy, restart unless-stopped
```
- `client/nginx.conf`: SPA fallback; `/hubs/` → `http://backend:8080` with WebSocket upgrade, `proxy_read_timeout 86400`; `/health` → backend; gzip; long cache for hashed assets.
- No external networks/Traefik labels: `docker compose up --build` works anywhere. Dokploy: create Compose app from repo, attach domain to service `frontend` port 80 in the Domains UI.
- `.env.example`: `PORT=8080`, `ASPNETCORE_ENVIRONMENT=Production`.
- Dev: `dotnet run --project server/TimeGolf` (http://localhost:5000) + `npm run dev` in `client/` (http://localhost:3000, rsbuild proxy `/hubs` (ws) and `/health` → 5000).

## 14. Repo layout & conventions

```
.editorconfig  .dockerignore  .gitignore  global.json  Directory.Build.props  Directory.Packages.props
TimeGolf.slnx  docker-compose.yml  .env.example  CLAUDE.md  docs/PRD.md
server/TimeGolf/        server/TimeGolf.Tests/
client/  (package.json, rsbuild.config.ts, tsconfig.json, Dockerfile, nginx.conf, public/, src/)
```
- Copied from `Tests/snakeio`: `.editorconfig` (CRLF, no final newline for C#), `Directory.Build.props` (net10.0, nullable, `EnforceCodeStyleInBuild`, SonarAnalyzer.CSharp, `.Tests` projects auto-get NUnit/Moq), central package management, `global.json`.
- C#: file-scoped namespaces, `sealed` classes, primary constructors, records for DTOs, collection expressions, `System.Threading.Lock`, `[LoggerMessage]`, no `#region`, comments only for non-obvious WHY.
- Tests: NUnit, `// Arrange` `// Act` `// Assert` bare markers, `Assert.EnterMultipleScope()`, no assertion messages, `SetArgDisplayNames`.
- TS: strict, Prettier (single quotes, no bracket spacing), files end with single newline.
- YAGNI: no feature not in this PRD.

## 15. Milestones

**M1 (done, v1)** — scaffolding, docker, level generator, physics, rendering, UI style.

**M2 — Turn engine (server)**: `Timeline` + `Match` turns/actions/replay/scoring, hub, tests (§16). ✅ `dotnet test` green.

**M3 — Client turn play**: playback, HUD turn/timer/skip, hit on your turn. ✅ two tabs alternate turns, balls collide, 10 turns each → results.

**M4 — Rewrite the past**: timeline markers, checkpoint view, replace mode, replay 3×. ✅ replacing an early hit visibly changes the other player's later shot outcome (incl. un-holing).

**M5 — Polish**: spectator flow, reconnect, sounds, results. ✅ full 3-round match in docker.

## 16. Test list

- Timeline: present hit appends record & moves only expected balls; replace re-sims all later records with same dir/power; replay can un-hole another player; record of a holed ball replays void; determinism (same records twice → identical end).
- Actions: not your turn / not joined / invalid numbers rejected; replace only own non-void.
- Holed: holed player passed over without using turns; all active holed → round ends; un-holed by a replay → gets turns again.
- Turns: round-robin order rotates by round; 10 rotations (skips count) end the round after playback; timeout → skip; inactive → instant skip; next turn waits for playback duration (1× / 3×).
- Scoring: holed = ceil(100/hits); not holed = ceil(100/(hits + path distance)); ranking by score desc; missed round = worst − 2 (≥ 0) incl. inactive and late joiners; no winner with 0 points.
- Restart vote: minority no-op; majority → new match, points & votes reset; second press withdraws.
- Players: join before first hit enters current round; join after first hit spectates until next round; reconnect restores; dedupe names among active; all leave → `Lobby`.
- Lobbies: create/join/password/full; ready start only when all active ready; unready blocks; match end → `Lobby` with ready cleared; restart vote → `Lobby`; ready-room disconnect removes player; empty lobby deleted after 60 s, at once after the last Leave; two lobbies fully independent; `Lobbies` pushed to menu connections only.
- Aim relay: only current-turn player's `Aim` relayed, to others only; ignored during playback / other phases; rate limit.
- Physics: no tunneling, rests on flat, slope keeps moving, sand stops sooner, hole capture/skim, bounces recorded.
- Generator: deterministic; 1000 seeds × rounds beatable; spawns ≥16; hole clearance; distance field finite at spawns, ~0 at hole, ≥ straight-line distance.