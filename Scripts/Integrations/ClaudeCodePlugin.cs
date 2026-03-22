using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Newtonsoft.Json.Linq;
using ClaudeCodeQuest.Core;

namespace ClaudeCodeQuest.Integrations
{
    /// <summary>
    /// Watches ~/.claude/projects/**/*.jsonl for live Claude Code activity.
    /// Polls on a timer — no FileSystemWatcher (unreliable on macOS for appended files).
    /// Reads only new bytes from each file (tail-f style, starting from EOF on discovery).
    /// </summary>
    public class ClaudeCodePlugin : IIntegrationPlugin
    {
        public string Id => "claude-code";
        public string DisplayName => "Claude Code";
        public bool IsEnabled => _enabled;

        public event Action<AgentEvent> OnEvent;

        private bool _enabled = true;
        private string _projectsDir;
        private double _pollInterval = 1.5;
        private double _pollAccumulator = 0;
        private double _idleTimeoutSec = 7.0;
        private double _staleThresholdSec = 600; // 10 minutes

        // Per-file state
        private class FileTracker
        {
            public string SessionId;
            public long ByteOffset;
            public double TimeSinceLastEvent;
            public bool WaitingEmitted;
            public bool SessionStartEmitted;
        }

        private readonly Dictionary<string, FileTracker> _trackers = new();

        private static readonly HashSet<string> TypingTools = new(StringComparer.OrdinalIgnoreCase)
        {
            "Edit", "Write", "MultiEdit", "file_edit", "file_write", "str_replace_editor"
        };

        private static readonly HashSet<string> ReadingTools = new(StringComparer.OrdinalIgnoreCase)
        {
            "Read", "Grep", "Glob", "List", "file_read", "LS"
        };

        public void Initialize(Dictionary<string, string> config)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            _projectsDir = config.TryGetValue("projects_dir", out var dir) && !string.IsNullOrEmpty(dir)
                ? dir.Replace("~", home)
                : Path.Combine(home, ".claude", "projects");

            if (config.TryGetValue("poll_interval_sec", out var interval) && double.TryParse(interval, out var iv))
                _pollInterval = iv;

            if (!Directory.Exists(_projectsDir))
            {
                GD.PrintErr($"[ClaudeCodePlugin] projects_dir not found: {_projectsDir} — plugin will wait.");
            }
        }

        public void Poll(double delta)
        {
            _pollAccumulator += delta;
            if (_pollAccumulator < _pollInterval)
            {
                // Still tick idle timers every frame
                TickIdleTimers(delta);
                return;
            }
            _pollAccumulator = 0;

            if (!Directory.Exists(_projectsDir))
                return;

            var now = DateTime.UtcNow;

            // Find all .jsonl files, including sessions/ subdirectories
            var files = Directory.EnumerateFiles(_projectsDir, "*.jsonl", SearchOption.AllDirectories)
                .ToList();

            var activeFiles = new HashSet<string>();

            foreach (var filePath in files)
            {
                var info = new FileInfo(filePath);

                // Skip files not modified in the last stale threshold
                if ((now - info.LastWriteTimeUtc).TotalSeconds > _staleThresholdSec)
                {
                    if (_trackers.TryGetValue(filePath, out var staleTracker) && staleTracker.SessionStartEmitted)
                    {
                        Emit(new AgentEvent
                        {
                            SessionId = staleTracker.SessionId,
                            SourcePlugin = Id,
                            Type = AgentEventType.SessionEnd
                        });
                        _trackers.Remove(filePath);
                    }
                    continue;
                }

                activeFiles.Add(filePath);

                if (!_trackers.TryGetValue(filePath, out var tracker))
                {
                    // New file — start from current end (live activity only)
                    tracker = new FileTracker
                    {
                        SessionId = DeriveSessionId(filePath),
                        ByteOffset = info.Length,
                        TimeSinceLastEvent = 0,
                        WaitingEmitted = false,
                        SessionStartEmitted = false
                    };
                    _trackers[filePath] = tracker;
                }

                ReadNewLines(filePath, tracker);
            }

            // Clean up trackers for files that no longer exist
            foreach (var key in _trackers.Keys.ToList())
            {
                if (!activeFiles.Contains(key))
                {
                    var t = _trackers[key];
                    if (t.SessionStartEmitted)
                        Emit(new AgentEvent { SessionId = t.SessionId, SourcePlugin = Id, Type = AgentEventType.SessionEnd });
                    _trackers.Remove(key);
                }
            }
        }

        public void Shutdown() { }

        // ── private helpers ────────────────────────────────────────────────

        private void ReadNewLines(string filePath, FileTracker tracker)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length <= tracker.ByteOffset)
                    return;

                stream.Seek(tracker.ByteOffset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    tracker.ByteOffset = stream.Position;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ProcessLine(line, tracker);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ClaudeCodePlugin] Error reading {filePath}: {ex.Message}");
            }
        }

        private void ProcessLine(string line, FileTracker tracker)
        {
            JObject obj;
            try { obj = JObject.Parse(line); }
            catch { return; } // malformed line — skip silently

            var type = obj["type"]?.ToString();
            if (type == null) return;

            AgentEvent ev = null;

            if (type == "assistant")
            {
                ev = ClassifyAssistantMessage(obj, tracker.SessionId);
            }
            else if (type == "system")
            {
                var subtype = obj["subtype"]?.ToString();
                if (subtype == "turn_duration")
                {
                    ev = new AgentEvent
                    {
                        SessionId = tracker.SessionId,
                        SourcePlugin = Id,
                        Type = AgentEventType.TurnComplete
                    };
                }
            }

            if (ev == null) return;

            if (!tracker.SessionStartEmitted)
            {
                Emit(new AgentEvent { SessionId = tracker.SessionId, SourcePlugin = Id, Type = AgentEventType.SessionStart });
                tracker.SessionStartEmitted = true;
            }

            tracker.TimeSinceLastEvent = 0;
            tracker.WaitingEmitted = false;
            Emit(ev);
        }

        private AgentEvent ClassifyAssistantMessage(JObject obj, string sessionId)
        {
            var content = obj["message"]?["content"] as JArray;
            if (content == null) return null;

            foreach (var item in content)
            {
                if (item["type"]?.ToString() != "tool_use") continue;

                var toolName = item["name"]?.ToString() ?? "";
                var input = item["input"];
                var detail = input?.ToString() ?? "";
                if (detail.Length > 120) detail = detail[..120];

                AgentEventType eventType;
                if (TypingTools.Contains(toolName))
                    eventType = AgentEventType.Typing;
                else if (ReadingTools.Contains(toolName))
                    eventType = AgentEventType.Reading;
                else if (toolName.Equals("Task", StringComparison.OrdinalIgnoreCase) ||
                         toolName.Equals("Bash", StringComparison.OrdinalIgnoreCase))
                    eventType = AgentEventType.Thinking;
                else
                    eventType = AgentEventType.Thinking;

                // Check for skill usage in tool input
                string skillName = null;
                var inputStr = input?.ToString() ?? "";
                if (inputStr.Contains("SKILL.md") || inputStr.Contains("skills/"))
                    skillName = ExtractSkillName(inputStr);

                return new AgentEvent
                {
                    SessionId = sessionId,
                    SourcePlugin = Id,
                    Type = eventType,
                    ToolName = toolName,
                    SkillName = skillName,
                    Detail = detail
                };
            }

            // Text-only assistant message → Thinking
            return new AgentEvent
            {
                SessionId = sessionId,
                SourcePlugin = Id,
                Type = AgentEventType.Thinking
            };
        }

        private void TickIdleTimers(double delta)
        {
            foreach (var tracker in _trackers.Values)
            {
                if (!tracker.SessionStartEmitted) continue;
                if (tracker.WaitingEmitted) continue;

                tracker.TimeSinceLastEvent += delta;
                if (tracker.TimeSinceLastEvent >= _idleTimeoutSec)
                {
                    tracker.WaitingEmitted = true;
                    Emit(new AgentEvent
                    {
                        SessionId = tracker.SessionId,
                        SourcePlugin = Id,
                        Type = AgentEventType.WaitingInput
                    });
                }
            }
        }

        private static string DeriveSessionId(string filePath)
        {
            // Use the filename (without extension) as the session ID
            return Path.GetFileNameWithoutExtension(filePath);
        }

        private static string ExtractSkillName(string input)
        {
            // Look for patterns like "skills/frontend-design/SKILL.md" or ".claude/pdf"
            var idx = input.IndexOf("skills/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = input[(idx + 7)..];
                var end = rest.IndexOfAny(new[] { '/', '"', '\'', '\n', ' ' });
                return end > 0 ? rest[..end] : rest;
            }
            return null;
        }

        private void Emit(AgentEvent ev)
        {
            ev.Timestamp = DateTime.UtcNow;
            OnEvent?.Invoke(ev);
        }
    }
}
