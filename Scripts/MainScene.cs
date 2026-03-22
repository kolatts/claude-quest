using Godot;
using System.Collections.Generic;
using ClaudeCodeQuest.Core;
using ClaudeCodeQuest.Agents;

namespace ClaudeCodeQuest
{
    /// <summary>
    /// Root scene controller. Spawns/despawns AgentCharacter nodes in response to
    /// PluginManager signals. Never imports plugin-specific types — only AgentEvent.
    /// </summary>
    public partial class MainScene : Node2D
    {
        private const float AgentSpacing = 120f;
        private const float GroundY = 300f;
        private const float ScrollSpeed = 20f;  // ground scroll px/sec

        private readonly Dictionary<string, AgentCharacter> _agents = new();
        private Label _emptyLabel = null!;
        private ColorRect _ground = null!;
        private ColorRect _sky = null!;
        private Camera2D _camera = null!;
        private float _groundOffset = 0f;

        public override void _Ready()
        {
            BuildBackground();
            BuildEmptyLabel();
            BuildCamera();

            // Connect to PluginManager signal
            var pm = GetNode<PluginManager>("/root/PluginManager");
            pm.AgentEventReceived += OnAgentEventReceived;
        }

        public override void _Process(double delta)
        {
            ScrollGround(delta);
            UpdateCamera();
            _emptyLabel.Visible = _agents.Count == 0;
        }

        // ── signal handler ─────────────────────────────────────────────────

        private void OnAgentEventReceived(string sessionId, string sourcePlugin, int eventTypeInt, string toolName, string skillName, string detail)
        {
            var eventType = (AgentEventType)eventTypeInt;

            if (eventType == AgentEventType.SessionStart)
            {
                SpawnAgent(sessionId);
                return;
            }

            if (eventType == AgentEventType.SessionEnd)
            {
                DespawnAgent(sessionId);
                return;
            }

            if (!_agents.TryGetValue(sessionId, out var agent))
            {
                // Event arrived before SessionStart — spawn on first meaningful event
                agent = SpawnAgent(sessionId);
            }

            agent.ApplyEvent(new AgentEvent
            {
                SessionId = sessionId,
                SourcePlugin = sourcePlugin,
                Type = eventType,
                ToolName = toolName,
                SkillName = skillName,
                Detail = detail
            });
        }

        // ── agent lifecycle ────────────────────────────────────────────────

        private AgentCharacter SpawnAgent(string sessionId)
        {
            if (_agents.TryGetValue(sessionId, out var existing))
                return existing;

            var agent = new AgentCharacter();
            AddChild(agent);
            agent.Position = new Vector2(NextSpawnX(), GroundY);
            agent.Initialize(sessionId);
            _agents[sessionId] = agent;
            GD.Print($"[MainScene] Spawned agent: {sessionId}");
            return agent;
        }

        private void DespawnAgent(string sessionId)
        {
            if (!_agents.TryGetValue(sessionId, out var agent)) return;
            _agents.Remove(sessionId);
            agent.QueueFree();
            GD.Print($"[MainScene] Despawned agent: {sessionId}");
        }

        private float NextSpawnX()
        {
            float x = 100f;
            foreach (var agent in _agents.Values)
            {
                if (agent.Position.X >= x)
                    x = agent.Position.X + AgentSpacing;
            }
            return x;
        }

        // ── camera ─────────────────────────────────────────────────────────

        private void UpdateCamera()
        {
            if (_agents.Count == 0) return;

            float rightmost = 400f;
            foreach (var agent in _agents.Values)
            {
                if (agent.Position.X > rightmost)
                    rightmost = agent.Position.X;
            }

            var target = new Vector2(rightmost + 100f, GroundY - 100f);
            _camera.Position = _camera.Position.Lerp(target, 0.05f);
        }

        // ── background ─────────────────────────────────────────────────────

        private void BuildBackground()
        {
            _sky = new ColorRect
            {
                Color = new Color(0.08f, 0.06f, 0.14f),  // dark purple-blue night sky
                Size = new Vector2(2000, 400),
                Position = new Vector2(-200, -200)
            };
            AddChild(_sky);

            _ground = new ColorRect
            {
                Color = new Color(0.18f, 0.28f, 0.14f),  // dark green ground
                Size = new Vector2(4000, 80),
                Position = new Vector2(-200, GroundY - 10)
            };
            AddChild(_ground);
        }

        private void ScrollGround(double delta)
        {
            _groundOffset -= (float)(ScrollSpeed * delta);
            if (_groundOffset < -64f) _groundOffset += 64f;
            _ground.Position = new Vector2(-200 + _groundOffset, _ground.Position.Y);
        }

        private void BuildEmptyLabel()
        {
            _emptyLabel = new Label
            {
                Text = "Waiting for adventurers...",
                Position = new Vector2(300, 200),
                Modulate = new Color(0.6f, 0.6f, 0.6f)
            };
            AddChild(_emptyLabel);
        }

        private void BuildCamera()
        {
            _camera = new Camera2D
            {
                Position = new Vector2(400, 200),
                Enabled = true
            };
            AddChild(_camera);
        }
    }
}
