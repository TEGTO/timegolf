# TimeGolf

Turn-based multiplayer browser mini-golf with a rewritable shared past (replace your past hits → whole future re-simulates). **Spec: `docs/PRD.md`** — read it before any change; keep code and PRD in sync.

- Server: `server/TimeGolf` (.NET 10, SignalR, in-memory, authoritative deterministic physics, instant per-hit simulation). Tests: `server/TimeGolf.Tests` (NUnit).
- Client: `client/` (Rsbuild, React 19, TS, three.js, zustand).
- Shared contract: `server/TimeGolf/Dtos.cs` ↔ `client/src/types.ts` (camelCase JSON). Change both together.

## Commands
- `dotnet build TimeGolf.slnx` · `dotnet test TimeGolf.slnx`
- `dotnet run --project server/TimeGolf` (http://localhost:5000)
- `cd client && npm run dev` (http://localhost:3000, proxies `/hubs` + `/health` to :5000) · `npm run build` · `npx tsc --noEmit`
- `docker compose up --build` → http://localhost:8080

## Conventions
- C#: file-scoped namespaces, sealed classes, primary constructors, records, `System.Threading.Lock`, `[LoggerMessage]` in `*.Log.cs`, no `#region`, CRLF, no final newline. SonarAnalyzer is on — keep the build warning-free.
- Tests: bare `// Arrange` `// Act` `// Assert`, `Assert.EnterMultipleScope()`, no assertion messages, `SetArgDisplayNames`.
- TS: strict, Prettier (`.prettierrc`), single trailing newline.
- Comments explain WHY only. YAGNI: nothing outside the PRD.