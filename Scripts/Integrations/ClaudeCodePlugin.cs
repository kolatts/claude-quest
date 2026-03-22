using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
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

        public event Action<AgentEvent>? OnEvent;

        private bool _enabled = true;
        private string _projectsDir = "";
        private double _pollInterval = 1.5;
        private double _pollAccumulator = 0;
        private double _idleTimeoutSec = 7.0;
        private double _staleThresholdSec = 120;

        private class FileTracker
        {
            public string SessionId = "";
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
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

            _projectsDir = config.TryGetValue("projects_dir", out var dir) && !string.IsNullOrEmpty(dir)
                ? dir.Replace("~", home)
                : Path.Combine(home, ".claude", "projects");

            if (config.TryGetValue("poll_interval_sec", out var interval) && double.TryParse(interval, out var iv))
                _pollInterval = iv;

            GD.Print($"[ClaudeCodePlugin] Watching: {_projectsDir}");

            if (!Directory.Exists(_projectsDir))
                GD.PrintErr($"[ClaudeCodePlugin] projects_dir not found: {_projectsDir}");
        }

        public void Poll(double delta)
        {
            _pollAccumulator += delta;
            if (_pollAccumulator < _pollInterval)
            {
                TickIdleTimers(delta);
                return;
            }
            _pollAccumulator = 0;

            if (!Directory.Exists(_projectsDir))
                return;

            var now = DateTime.UtcNow;
            var files = Directory.EnumerateFiles(_projectsDir, "*.jsonl", SearchOption.AllDirectories).ToList();

            GD.Print($"[ClaudeCodePlugin] Poll: found {files.Count} jsonl file(s), {_trackers.Count} tracked");

            var activeFiles = new HashSet<string>();

            foreach (var filePath in files)
            {
                var info = new FileInfo(filePath);

                if ((now - info.LastWriteTimeUtc).TotalSeconds > _staleThresholdSec)
                {
                    if (_trackers.TryGetValue(filePath, out var staleTracker) && staleTracker.SessionStartEmitted)
                    {
                        Emit(new AgentEvent { SessionId = staleTracker.SessionId, SourcePlugin = Id, Type = AgentEventType.SessionEnd });
                        _trackers.Remove(filePath);
                    }
                    continue;
                }

                activeFiles.Add(filePath);

                if (!_trackers.TryGetValue(filePath, out var tracker))
                {
                    var age = (now - info.LastWriteTimeUtc).TotalSeconds;
                    var startOffset = age < 60.0 ? Math.Max(0, info.Length - 8192) : info.Length;

                    tracker = new FileTracker
                    {
                        SessionId = DeriveSessionId(filePath),
                        ByteOffset = startOffset,
                    };
                    _trackers[filePath] = tracker;
                    GD.Print($"[ClaudeCodePlugin] Tracking new file: {Path.GetFileName(filePath)} offset={startOffset}");
                }

                ReadNewLines(filePath, tracker);
            }

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

        private void ReadNewLines(string filePath, FileTracker tracker)
        {
            try
            {
                using var stream = new FileStream(filePath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = System.IO.FileAccess.Read,
                    Share = FileShare.ReadWrite
                });

                if (stream.Length <= tracker.ByteOffset)
                    return;

                stream.Seek(tracker.ByteOffset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string? line;
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
            JsonObject? obj;
            try { obj = JsonNode.Parse(line)?.AsObject(); }
            catch { return; }
            if (obj == null) return;

            var type = obj["type"]?.GetValue<string>();
            if (type == null) return;

            AgentEvent? ev = null;

            if (type == "assistant")
                ev = ClassifyAssistantMessage(obj, tracker.SessionId);
            else if (type == "system" && obj["subtype"]?.GetValue<string>() == "turn_duration")
                ev = new AgentEvent { SessionId = tracker.SessionId, SourcePlugin = Id, Type = AgentEventType.TurnComplete };

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

        private AgentEvent? ClassifyAssistantMessage(JsonObject obj, string sessionId)
        {
            var content = obj["message"]?["content"]?.AsArray();
            if (content == null) return null;

            foreach (var item in content)
            {
                if (item?["type"]?.GetValue<string>() != "tool_use") continue;

                var toolName = item["name"]?.GetValue<string>() ?? "";
                var inputStr = item["input"]?.ToJsonString() ?? "";
                var detail = inputStr.Length > 120 ? inputStr[..120] : inputStr;

                AgentEventType eventType;
                if (TypingTools.Contains(toolName))
                    eventType = AgentEventType.Typing;
                else if (ReadingTools.Contains(toolName))
                    eventType = AgentEventType.Reading;
                else
                    eventType = AgentEventType.Thinking;

                string? skillName = null;
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

            return new AgentEvent { SessionId = sessionId, SourcePlugin = Id, Type = AgentEventType.Thinking };
        }

        private void TickIdleTimers(double delta)
        {
            foreach (var tracker in _trackers.Values)
            {
                if (!tracker.SessionStartEmitted || tracker.WaitingEmitted) continue;
                tracker.TimeSinceLastEvent += delta;
                if (tracker.TimeSinceLastEvent >= _idleTimeoutSec)
                {
                    tracker.WaitingEmitted = true;
                    Emit(new AgentEvent { SessionId = tracker.SessionId, SourcePlugin = Id, Type = AgentEventType.WaitingInput });
                }
            }
        }

        private static string DeriveSessionId(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath) ?? filePath;

        private static string? ExtractSkillName(string input)
        {
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
