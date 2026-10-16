# RimTalk Agent Guidelines

RimWorld mod (C#, Harmony) that generates AI pawn dialogue. Targets RimWorld 1.5 and 1.6 from one codebase (`#if V1_5` for version splits); 1.7 loads the 1.6 build.

If `AGENTS.local.md` exists, read it and follow it; it holds personal local rules (not committed) and overrides this file.

## Coding Principles
- **Think first**: state assumptions; if a request is ambiguous, ask instead of guessing. Push back when a simpler approach exists.
- **Simplicity**: minimum code that solves the problem. No speculative features, options, or single-use abstractions.
- **Surgical changes**: every changed line traces to the request. Match existing style; don't refactor or reformat unrelated code. Remove only orphans your change created; mention other dead code instead of deleting it.
- **Verify**: define a success check (build, test, repro) and loop until it passes.

## Build & Test
No game install needed; the project references `Krafs.Rimworld.Ref` by default. Verify both versions and tests before finishing:
```
dotnet build RimTalk.csproj -c Release -p:GameVersion=1.5
dotnet build RimTalk.csproj -c Release -p:GameVersion=1.6
dotnet test Tests/RimTalk.Tests.csproj -c Release
```
Delete `obj/` when switching `GameVersion` if restore picks up the wrong reference version.

## Codebase Map
- `Source/Service/`: core orchestration (`TalkService`, `AIService`, `ContextBuilder`).
- `Source/Prompt/`: Scriban template parsing (`Parser/`), variable providers, `PromptManager`.
- `Source/PawnMemory/`: event-driven pawn memory (`PawnMemoryTracker`, `MemoryHookService`).
- `Source/API/`, `Source/Compatibility/`: public modder API (`RimTalkPromptAPI`, `ContextHookRegistry`) and external mod bridges.
- `Source/Client/`: LLM provider HTTP/SSE clients.
- `Source/Data/`: runtime and saved state (`RimTalkWorldComponent`, `PawnState`, `TalkHistory`, `TalkRequest`).
- `Source/UI/`, `Source/Settings/`: IMGUI windows and mod settings (`partial class Settings`).
- `Source/Patch/`: Harmony patches. Keep bodies minimal; delegate to Services.
- `Source/Error/`, `Source/Util/`: error handling (`AIErrorHandler`) and shared helpers.
- `Tests/`: xUnit tests, including `ApiCompatibilityTests` (member and ABI compatibility; see below) and `CodebaseLanguageTests` (English-only source).

## Architecture Rules
- **Thin orchestrators**: never put feature state or tracking logic into `TalkService`/`AIService`. Give each feature its own class with one entry point (e.g. `EncounterTracker`, `AIErrorHandler`).
- **New feature, new file**: a feature with its own state, dialog, or tab gets its own class/file. Small changes to an existing feature stay where that feature lives. Large classes are split by area into `partial` files named `Type.Area.cs` (e.g. `DebugWindow.Details.cs`); add to the matching one. Don't add unrelated helpers to grab-bag files like `*Util.cs`.
- **Short methods in new code**: decompose long methods you write into helpers, without adding abstraction layers. Don't restructure existing methods (see Backward Compatibility).
- **Main thread only for game state**: AI requests run on background threads (`Task.Run` in `TalkService`). Background code must not touch pawns, maps, defs, `Find.*`, or Unity APIs. Hand results back through a queue drained on the main thread (see `PawnState.DrainIncomingTalkResponses`) or `AIErrorHandler.EnqueueMessage` (drained every frame). Never use `LongEventHandler.ExecuteWhenFinished` from background code: outside a long event it runs immediately on the calling thread.

## Backward Compatibility (100+ addons Harmony-patch this mod)
Assume addons patch or reflect on **every** member, including private and UI ones. `Tests/AbiBaseline/members-*.txt` snapshots all members from the last release, and `VerifyAllMembersPreserved_SinceLastRelease` fails if one disappears or changes.
- Never remove, rename, move, or change the signature of an existing method, field, property, or nested type, whatever its access level. Changing a parameter list (even adding an optional one) breaks patch lookups; add a new method instead and keep the old one working.
- Give that new method a new name if the old one had no overloads; name-only Harmony patches throw `AmbiguousMatchException` on a newly added overload.
- Don't move logic out of an existing method so callers bypass it; addon patches on it would silently stop working.
- Never edit the snapshot files to make the test pass. Intentional removals need maintainer approval.
- Never rename core Defs (e.g. `RimTalkInteraction`) or enum values.
- **Save compatibility**: never rename `Scribe_*.Look` keys or saved types/fields; existing player saves would silently lose data.

## Localization
- All code, identifiers, and comments in English.
- No hardcoded player-facing strings or branching on game language. Use `"Key".Translate(...)` and add new keys to `Languages/English/Keyed/Translation_English.xml` only; translators update the other languages.

## Performance & Concurrency
- Hot paths (ticks, pawn iteration, combat hooks) must not allocate: no LINQ, use indexed `for`; cache `Enum.GetValues` in `static readonly`; prefer `DistanceToSquared`.
- No `Traverse` or uncached reflection in frequent paths. Cache `FieldInfo`/`MethodInfo` or use `AccessTools.MethodDelegate<T>`.
- Use `ConcurrentDictionary` for lookups, not `ConcurrentBag`. Cancel tokens on timeout; use generation tokens so stale async callbacks can't overwrite newer state.
- Parse structured text with real parsers or anchored regex, not `string.Contains`.

## Testing
- Add or update tests for core logic, state machines, and algorithms where subtle changes ripple.
- Skip tests for UI tweaks, defs/settings, simple fixes, and wiring.

## Comments
Brief, explaining *why* for non-obvious logic. Preserve existing comments.
