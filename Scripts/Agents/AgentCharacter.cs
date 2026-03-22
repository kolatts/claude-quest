using Godot;
using System;
using ClaudeCodeQuest.Core;

namespace ClaudeCodeQuest.Agents
{
    /// <summary>
    /// Visual representation of one Claude Code session.
    /// State machine driven entirely by ApplyEvent() — no direct plugin references.
    /// Phase 1: all visuals are colored rectangles. Phase 2 replaces with sprites.
    /// </summary>
    public partial class AgentCharacter : Node2D
    {
        // ── state ──────────────────────────────────────────────────────────
        private AgentEventType _state = AgentEventType.Idle;
        private string _sessionId = "";
        private string _currentTool = "";
        private double _turnCompleteTimer = 0;
        private const double TurnCompleteDisplaySec = 1.5;

        // ── walk / drift ───────────────────────────────────────────────────
        private const float WalkSpeed = 20f;   // pixels/sec rightward drift while active
        private float _xpFloatY = 0f;
        private bool _showingXp = false;
        private double _xpTimer = 0;
        private const double XpDisplaySec = 1.5;

        // ── child node refs (assigned in BuildNodes, called from _Ready) ──
        private ColorRect _body = null!;
        private ColorRect _statusRing = null!;
        private Label _nameLabel = null!;
        private Label _toolLabel = null!;
        private Label _xpLabel = null!;
        private Label _koLabel = null!;

        // State → body color
        private static Color ColorIdle     = new Color(0.5f, 0.5f, 0.5f);   // gray
        private static Color ColorTyping   = new Color(0.2f, 0.8f, 0.2f);   // green
        private static Color ColorReading  = new Color(0.2f, 0.5f, 1.0f);   // blue
        private static Color ColorThinking = new Color(1.0f, 0.8f, 0.1f);   // yellow
        private static Color ColorWaiting  = new Color(0.9f, 0.1f, 0.1f);   // red
        private static Color ColorVictory  = new Color(1.0f, 1.0f, 1.0f);   // white flash

        // Ring colors match body but slightly darker
        private static Color RingIdle     = new Color(0.3f, 0.3f, 0.3f);
        private static Color RingTyping   = new Color(0.1f, 0.6f, 0.1f);
        private static Color RingReading  = new Color(0.1f, 0.3f, 0.8f);
        private static Color RingThinking = new Color(0.8f, 0.6f, 0.0f);
        private static Color RingWaiting  = new Color(0.7f, 0.0f, 0.0f);

        public override void _Ready()
        {
            BuildNodes();
            ApplyStateVisuals();
        }

        public override void _Process(double delta)
        {
            // Walk rightward while actively working
            if (_state == AgentEventType.Typing || _state == AgentEventType.Reading)
                Position += new Vector2((float)(WalkSpeed * delta), 0);

            // TurnComplete: auto-transition back to Idle after display window
            if (_state == AgentEventType.TurnComplete)
            {
                _turnCompleteTimer -= delta;
                if (_turnCompleteTimer <= 0)
                    SetState(AgentEventType.Idle);
            }

            // Floating +XP label
            if (_showingXp)
            {
                _xpTimer -= delta;
                _xpFloatY -= (float)(40 * delta);
                _xpLabel.Position = new Vector2(_xpLabel.Position.X, _xpFloatY);
                _xpLabel.Modulate = new Color(1, 1, 0, (float)(_xpTimer / XpDisplaySec));
                if (_xpTimer <= 0)
                {
                    _showingXp = false;
                    _xpLabel.Visible = false;
                }
            }
        }

        // ── public API ─────────────────────────────────────────────────────

        public void Initialize(string sessionId)
        {
            _sessionId = sessionId;
            _nameLabel.Text = sessionId.Length > 8 ? sessionId[..8] : sessionId;
        }

        /// <summary>The only way state changes — called by MainScene with events from PluginManager.</summary>
        public void ApplyEvent(AgentEvent ev)
        {
            switch (ev.Type)
            {
                case AgentEventType.Typing:
                case AgentEventType.Reading:
                case AgentEventType.Thinking:
                    _currentTool = string.IsNullOrEmpty(ev.ToolName) ? ev.Type.ToString() : ev.ToolName;
                    _toolLabel.Text = _currentTool;
                    SetState(ev.Type);
                    break;

                case AgentEventType.WaitingInput:
                    _currentTool = "";
                    _toolLabel.Text = "";
                    SetState(AgentEventType.WaitingInput);
                    break;

                case AgentEventType.TurnComplete:
                    SetState(AgentEventType.TurnComplete);
                    ShowXpFloat();
                    break;

                case AgentEventType.Idle:
                    SetState(AgentEventType.Idle);
                    break;
            }
        }

        // ── private ────────────────────────────────────────────────────────

        private void SetState(AgentEventType newState)
        {
            _state = newState;
            if (newState == AgentEventType.TurnComplete)
                _turnCompleteTimer = TurnCompleteDisplaySec;
            ApplyStateVisuals();
        }

        private void ApplyStateVisuals()
        {
            // Reset rotation
            _body.RotationDegrees = 0;
            _koLabel.Visible = false;

            switch (_state)
            {
                case AgentEventType.Idle:
                    _body.Color = ColorIdle;
                    _statusRing.Color = RingIdle;
                    break;

                case AgentEventType.Typing:
                    _body.Color = ColorTyping;
                    _statusRing.Color = RingTyping;
                    break;

                case AgentEventType.Reading:
                    _body.Color = ColorReading;
                    _statusRing.Color = RingReading;
                    break;

                case AgentEventType.Thinking:
                    _body.Color = ColorThinking;
                    _statusRing.Color = RingThinking;
                    break;

                case AgentEventType.WaitingInput:
                    _body.Color = ColorWaiting;
                    _body.RotationDegrees = 90; // lying flat = KO'd
                    _statusRing.Color = RingWaiting;
                    _koLabel.Visible = true;
                    _toolLabel.Text = "Awaiting orders!";
                    break;

                case AgentEventType.TurnComplete:
                    _body.Color = ColorVictory;
                    _statusRing.Color = RingTyping;
                    break;
            }
        }

        private void ShowXpFloat()
        {
            _xpFloatY = -60f;
            _xpLabel.Position = new Vector2(-10, _xpFloatY);
            _xpLabel.Text = "+XP";
            _xpLabel.Visible = true;
            _xpLabel.Modulate = Colors.Yellow;
            _showingXp = true;
            _xpTimer = XpDisplaySec;
        }

        private void BuildNodes()
        {
            // Status ring (flat ellipse under feet)
            _statusRing = new ColorRect
            {
                Size = new Vector2(36, 10),
                Position = new Vector2(-18, 26),
                Color = RingIdle
            };
            AddChild(_statusRing);

            // Body rectangle — 32x32, centered
            _body = new ColorRect
            {
                Size = new Vector2(32, 32),
                Position = new Vector2(-16, -16),
                Color = ColorIdle
            };
            AddChild(_body);

            // Name label (above head)
            _nameLabel = new Label
            {
                Position = new Vector2(-24, -40),
                Text = "agent",
                Modulate = Colors.White
            };
            AddChild(_nameLabel);

            // Tool label (below name)
            _toolLabel = new Label
            {
                Position = new Vector2(-24, -24),
                Text = "",
                Modulate = new Color(0.8f, 0.8f, 0.8f)
            };
            AddChild(_toolLabel);

            // KO indicator (shown when WaitingInput)
            _koLabel = new Label
            {
                Position = new Vector2(-10, -56),
                Text = "✚",
                Modulate = Colors.Red,
                Visible = false
            };
            AddChild(_koLabel);

            // Floating XP label
            _xpLabel = new Label
            {
                Position = new Vector2(-10, -60),
                Text = "+XP",
                Modulate = Colors.Yellow,
                Visible = false
            };
            AddChild(_xpLabel);
        }
    }
}
