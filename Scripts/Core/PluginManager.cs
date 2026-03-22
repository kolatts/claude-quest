using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ClaudeCodeQuest.Core;
using ClaudeCodeQuest.Integrations;

namespace ClaudeCodeQuest.Core
{
    /// <summary>
    /// Autoload node. Loads config, owns all plugins, calls Poll() every frame,
    /// and re-emits AgentEvents as a Godot signal for the scene layer.
    /// </summary>
    public partial class PluginManager : Node
    {
        [Signal]
        public delegate void AgentEventReceivedEventHandler(string sessionId, string sourcePlugin, int eventType, string toolName, string skillName, string detail);

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude-code-quest", "config.json");

        private readonly List<IIntegrationPlugin> _plugins = new();
        private readonly Queue<AgentEvent> _eventQueue = new();

        public override void _Ready()
        {
            var config = LoadConfig();
            RegisterPlugins(config);
        }

        public override void _Process(double delta)
        {
            foreach (var plugin in _plugins)
            {
                if (plugin.IsEnabled)
                    plugin.Poll(delta);
            }

            while (_eventQueue.Count > 0)
            {
                var ev = _eventQueue.Dequeue();
                EmitSignal(SignalName.AgentEventReceived,
                    ev.SessionId ?? "",
                    ev.SourcePlugin ?? "",
                    (int)ev.Type,
                    ev.ToolName ?? "",
                    ev.SkillName ?? "",
                    ev.Detail ?? "");
            }
        }

        public override void _ExitTree()
        {
            foreach (var plugin in _plugins)
                plugin.Shutdown();
        }

        private void RegisterPlugins(JObject config)
        {
            Register(new ClaudeCodePlugin(), config);
            Register(new StubJiraPlugin(), config);
            Register(new StubGitPlugin(), config);
            Register(new StubGitHubPrPlugin(), config);
            Register(new StubSkillTrackerPlugin(), config);
        }

        private void Register(IIntegrationPlugin plugin, JObject config)
        {
            var settings = new Dictionary<string, string>();
            bool enabled = true;

            if (config.TryGetValue(plugin.Id, out var section) && section is JObject sectionObj)
            {
                enabled = sectionObj["Enabled"]?.Value<bool>() ?? true;
                if (sectionObj["Settings"] is JObject settingsObj)
                {
                    foreach (var kv in settingsObj)
                        settings[kv.Key] = kv.Value?.ToString() ?? "";
                }
            }

            if (!enabled)
            {
                GD.Print($"[PluginManager] {plugin.DisplayName} is disabled in config.");
                return;
            }

            plugin.OnEvent += ev => _eventQueue.Enqueue(ev);
            plugin.Initialize(settings);
            _plugins.Add(plugin);
            GD.Print($"[PluginManager] Registered: {plugin.DisplayName}");
        }

        private JObject LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JObject.Parse(json);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[PluginManager] Could not load config: {ex.Message} — using defaults.");
            }
            return new JObject();
        }
    }
}
