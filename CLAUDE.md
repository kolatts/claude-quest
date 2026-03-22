# CLAUDE.md — Claude Code Quest

Instructions for Claude Code working in this repository.

## Project Summary

Claude Code Quest is a standalone Godot 4.x (C#) macOS app that visualizes Claude Code agent sessions as fantasy RPG characters. It is purely observational — it reads JSONL transcripts and a cmux Unix socket. It never writes to either.

## Tech Stack

- **Engine:** Godot 4.6 with .NET/C# support
- **Language:** C# (net8.0 targeting)
- **Key dependency:** Newtonsoft.Json (for JSONL and cmux JSON-RPC parsing)
- **Platform target:** macOS standalone (pixel art, side-scrolling 2D)

## Load-Bearing Architectural Constraint

The scene layer must **never** know which plugin produced an event. Every plugin emits `AgentEvent`. The `MainScene` and `AgentCharacter` consume only `AgentEvent` — never plugin-specific types. Do not break this contract.

## Plugin Interface Contract

All integrations implement `IIntegrationPlugin`:

```csharp
string Id { get; }
string DisplayName { get; }
bool IsEnabled { get; }
void Initialize(Dictionary<string, string> config);
void Poll(double delta);   // called every frame by PluginManager
void Shutdown();
event Action<AgentEvent> OnEvent;
```

New plugins go in `Scripts/Integrations/`. Register them in `PluginManager.cs`.

## JSONL Format

Each line is a JSON object. Key fields:

```
{ "type": "assistant", "message": { "content": [ { "type": "tool_use", "name": "Edit", "input": { ... } } ] } }
{ "type": "system", "subtype": "turn_duration", ... }
{ "type": "user", ... }
```

Tool → AgentEventType mapping:
- `Edit`, `Write`, `MultiEdit`, `file_edit`, `file_write` → `Typing`
- `Read`, `Grep`, `Glob`, `List`, `file_read` → `Reading`
- `Task` → `Thinking` (subagent spawn)
- Text-only assistant message → `Thinking`
- `system` + `subtype: "turn_duration"` → `TurnComplete`
- No new data for 7 seconds after a tool call → `WaitingInput`

## Polling Rules

- **JSONL files:** poll every 1–2 seconds. Do NOT use `FileSystemWatcher` — it is unreliable on macOS for appended files.
- **cmux socket:** poll `workspace.list` every 3 seconds.
- **Read from end of file on discovery.** When a new JSONL file is found, start from the current byte offset (not from byte 0). We only care about live activity.
- **Stale session threshold:** JSONL files not modified in >10 minutes are removed from active tracking.

## macOS Path Handling

- Home directory: `Environment.GetFolderPath(SpecialFolder.UserProfile)`
- Tilde (`~`) is NOT automatically expanded in C# — resolve it manually everywhere
- cmux socket path: `~/Library/Application Support/cmux/cmux.sock`
- JSONL path: `~/.claude/projects/<project-hash>/<session-id>.jsonl` (or `sessions/` subdirectory)
- Config path: `~/.claude-code-quest/config.json`

## Graceful Degradation Rules

- If cmux socket file does not exist → silently disable the cmux plugin, no exceptions thrown
- If `~/.claude/projects/` is empty or missing → show "Waiting for adventurers..." in the scene
- If config file is missing → use defaults (do not crash)
- Never crash on missing or malformed data — log warnings and continue

## Configuration Shape

The config at `~/.claude-code-quest/config.json` has one key per plugin ID. Each entry has `Enabled: bool` and `Settings: Dictionary<string,string>`. `PluginManager` passes the `Settings` dict to `IIntegrationPlugin.Initialize()`.

## AgentCharacter State Machine

Valid states and transitions:

```
Idle → Typing | Reading | Thinking | WaitingInput
Typing → Idle | TurnComplete
Reading → Idle | TurnComplete
Thinking → Idle | TurnComplete
WaitingInput → Typing | Reading | Thinking
TurnComplete → Idle
```

Status ring colors: green=active/typing, blue=reading, yellow=thinking, red=waiting, gray=idle.

## Visual / Rendering Rules

- Pixel art: 16x16 or 32x32 tiles
- `canvas_textures` filter must be `nearest` (no smoothing)
- Placeholder sprites are colored rectangles — do not block implementation on art assets
- Camera follows the rightmost active agent with smooth panning
- Agents in the same cmux workspace cluster together

## File Layout

```
Scripts/Core/          IIntegrationPlugin.cs, PluginManager.cs
Scripts/Integrations/  ClaudeCodePlugin.cs, CmuxPlugin.cs, StubPlugins.cs
Scripts/Agents/        AgentCharacter.cs
Scripts/               MainScene.cs
Scenes/                Main.tscn, AgentCharacter.tscn
Assets/Sprites/        spritesheets
Assets/Fonts/          pixel fonts
Assets/Themes/         Godot UI themes
```

## Stub Plugins

`StubPlugins.cs` contains skeleton implementations for Jira, Git, GitHub PR, and Skill Tracker. Their config shapes are defined so users can see what's coming. `IsEnabled` returns `false` and `Poll` is a no-op until implemented.

## Planning Convention

All planning documents live in `planning/` and are named `YYYYMMDD-<topic>.md`.

**Preferred method: update the plan, don't create new files.** When scope changes, new decisions are made, or phases are revised, edit the existing plan document in place. Only create a new planning file if the scope change is large enough to warrant a fresh document (e.g., a major pivot or a new distinct feature track).

Current plan: `planning/20260322-mvp.md`

When working on a task:
1. Check the relevant phase checklist in the plan before starting
2. Mark items complete (`- [x]`) as you finish them **immediately after implementation** — do not batch updates
3. If implementation diverges from the plan (different approach, renamed file, skipped item), update the plan to reflect what was actually built
4. Add new open questions to the "Open Questions" section rather than making unilateral decisions
5. If a technical decision changes, update the "Key Technical Decisions" table

The plan is a living record of what exists, not just what was intended.

## What NOT to Do

- Do not write to JSONL files or the cmux socket
- Do not add platform-specific code outside of macOS path resolution
- Do not use `FileSystemWatcher` for JSONL monitoring
- Do not read JSONL from byte 0 on discovery — only read new content
- Do not let `MainScene` import or reference plugin-specific types directly
