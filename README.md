# Claude Code Quest

A standalone Godot 4.x (C#) macOS app that visualizes Claude Code agent sessions as fantasy quest characters. Each running agent becomes a party member on an adventure — their tools are spells, their skills are equipment, their tasks are quests.

This is **not** a VS Code extension. It's a standalone game window that runs alongside your terminal (especially [cmux](https://cmux.dev)).

## The Fantasy Quest Metaphor

| Dev Workflow Concept | Fantasy RPG Element |
|---|---|
| Agent session | Party member / hero character |
| Task / prompt | Quest the hero has accepted |
| `Edit` / `Write` | Crafting (quill animation, sparks) |
| `Bash` | Casting a spell (terminal lightning) |
| `Read` / `Grep` / `Glob` | Perception / scouting (book / magnifying glass) |
| `Task` (subagent) | Summoning a companion |
| Skills (`~/.claude/skills/`) | Equipped gear / class specialization |
| Waiting for user input | Hero looks at camera: "Awaiting orders!" |
| Turn complete | Victory pose, XP gained |
| cmux workspace | Party's current location / camp |
| Git branch | Which side-quest path they're on |
| Linked PR | A quest submitted for review |

Skills define the character's visual class. A `frontend-design` skill makes the character look like a wizard with a design wand. A `pdf` skill equips them with a scroll.

## Architecture

```
Data Sources → Plugin Layer → Unified Event Bus → Godot Scene
```

Every integration is a plugin implementing `IIntegrationPlugin`. The scene layer never knows which plugin produced an event — this is the load-bearing architectural constraint.

### Plugin Interface

```csharp
IIntegrationPlugin:
  Id: string
  DisplayName: string
  IsEnabled: bool
  Initialize(config)
  Poll(delta)        // called every frame
  Shutdown()
  event OnEvent → AgentEvent
```

### Unified Event Model

```
AgentEvent:
  SessionId: string
  SourcePlugin: string
  Type: AgentEventType
  Timestamp: DateTime
  ToolName?: string        // "Edit", "Bash", "Read", etc.
  SkillName?: string       // parsed from JSONL
  Detail?: string          // first 120 chars of context
  Metadata?: Dictionary    // git_branch, pr_status, workspace_title, etc.
```

`AgentEventType` values: `Idle`, `Typing`, `Reading`, `Thinking`, `WaitingInput`, `TurnComplete`, `SessionStart`, `SessionEnd`, `BranchCreated`, `CommitPushed`, `PrOpened`, `PrMerged`, `TicketUpdated`, `SkillUsed`, `Custom`

## Plugins

### Claude Code Plugin (MVP)
- Watches `~/.claude/projects/` for recently modified `.jsonl` files
- Uses polling (not file watchers — more reliable on macOS)
- Reads only new lines from each active file (like `tail -f`)
- Tool → visual state mapping:
  - `Edit`, `Write`, `Bash`, `MultiEdit` → Typing
  - `Read`, `Grep`, `Glob`, `List` → Reading
  - `Task` → Thinking (subagent spawn)
  - Text-only assistant response → Thinking
  - `system` + `subtype: "turn_duration"` → TurnComplete
- Idle timeout: 7 seconds after last tool call → WaitingInput
- Sessions not modified in >10 minutes are removed

### cmux Plugin (MVP)
- Connects to Unix domain socket at `~/Library/Application Support/cmux/cmux.sock`
- JSON-RPC protocol, polls `workspace.list` every 3 seconds
- Emits `SessionStart`/`SessionEnd` as workspaces appear/disappear
- Emits `WaitingInput` when `has_unread=true` (the blue ring in cmux)
- Emits `BranchCreated` when `git_branch` changes
- Silently disabled if cmux isn't running

### Stub Plugins (interface only, not yet implemented)
- **Jira** — REST API polling. Config: `base_url`, `api_token`, `project_key`, `poll_interval_sec`
- **Git** — watches `.git/refs/`. Config: `repo_paths`
- **GitHub PR** — GitHub API polling. Config: `repo`, `token`, `poll_interval_sec`
- **Skill Tracker** — parses JSONL for skill-specific patterns

## Configuration

Location: `~/.claude-code-quest/config.json`

```json
{
  "claude-code": {
    "Enabled": true,
    "Settings": {
      "projects_dir": "~/.claude/projects"
    }
  },
  "cmux": {
    "Enabled": true,
    "Settings": {
      "socket_path": "~/Library/Application Support/cmux/cmux.sock",
      "poll_interval_sec": "3"
    }
  },
  "jira": { "Enabled": false, "Settings": { "base_url": "", "api_token": "", "project_key": "" } },
  "git": { "Enabled": false, "Settings": { "repo_paths": "" } },
  "github-pr": { "Enabled": false, "Settings": { "repo": "", "token": "" } },
  "skill-tracker": { "Enabled": false, "Settings": {} }
}
```

Copy `config.example.json` to `~/.claude-code-quest/config.json` to get started.

## Project Structure

```
claude-code-quest/
├── project.godot
├── ClaudeCodeQuest.csproj
├── config.example.json
├── README.md
├── CLAUDE.md
├── Scripts/
│   ├── Core/
│   │   ├── IIntegrationPlugin.cs      # plugin interface + AgentEvent + AgentEventType
│   │   └── PluginManager.cs           # autoload, config, event routing
│   ├── Integrations/
│   │   ├── ClaudeCodePlugin.cs        # JSONL watcher
│   │   ├── CmuxPlugin.cs             # Unix socket client
│   │   └── StubPlugins.cs            # Jira, Git, GitHub PR, Skill Tracker stubs
│   ├── Agents/
│   │   └── AgentCharacter.cs          # visual representation, state machine, animations
│   └── MainScene.cs                   # scene controller, spawns/despawns agents, camera
├── Scenes/
│   ├── Main.tscn
│   └── AgentCharacter.tscn
└── Assets/
    ├── Sprites/
    ├── Fonts/
    └── Themes/
```

## Build Phases

**Phase 1 — MVP**
- [ ] Project scaffolding (Godot project, .csproj, folder structure)
- [ ] Core plugin interface and PluginManager
- [ ] ClaudeCodePlugin (JSONL watching, tool classification)
- [ ] CmuxPlugin (socket connection, workspace state)
- [ ] AgentCharacter with placeholder sprites (colored rectangles)
- [ ] MainScene with basic side-scrolling layout
- [ ] State machine: idle → typing → reading → thinking → waiting → idle
- [ ] Color-coded status rings (green/blue/yellow/red/gray)
- [ ] Status bubble showing current tool name
- [ ] Branch/PR metadata display from cmux
- [ ] Config file loading/saving

**Phase 2 — Visual Polish**
- [ ] Real pixel art character sprites with animation frames
- [ ] Tool-specific visual effects (particles, overlays)
- [ ] Skill badge icons
- [ ] Procedural terrain generation
- [ ] Camera system with smooth follow
- [ ] Victory animation on TurnComplete
- [ ] Subagent companion spawning

**Phase 3 — Integrations**
- [ ] Jira plugin (REST API polling)
- [ ] Git plugin (watch `.git/refs`)
- [ ] GitHub PR plugin (API polling)
- [ ] Skill Tracker (parse JSONL for skill patterns)
- [ ] Quest log UI (history of completed tasks)

**Phase 4 — Gamification**
- [ ] XP system (earn XP per tool call, level up agents)
- [ ] Achievement badges (first 100 edits, first PR merged, etc.)
- [ ] Sound effects and music
- [ ] Agent customization / skins
- [ ] Persistent stats across sessions

## Tech Stack

- **Engine:** Godot 4.6 with .NET/C# support
- **Language:** C# (net8.0)
- **Dependencies:** Newtonsoft.Json (JSONL parsing, cmux socket protocol)
- **Target:** macOS standalone
- **Rendering:** 2D pixel art, `canvas_textures` filter set to `nearest`

## Key References

- [pixel-agents](https://github.com/pablodelucca/pixel-agents) — proved JSONL transcript watching works; their `transcriptParser.ts` and `fileWatcher.ts` are the reference implementation for tool classification heuristics and idle detection
- [cmux socket API docs](https://www.cmux.dev/docs/api) — JSON-RPC over Unix domain socket; key methods: `workspace.list`, `workspace.current`, `surface.list`, `system.identify`
- JSONL location: `~/.claude/projects/<project-hash>/<session-id>.jsonl` (or in a `sessions/` subdirectory)

## License

MIT — see [LICENSE](LICENSE).
