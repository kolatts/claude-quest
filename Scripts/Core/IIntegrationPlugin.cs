using System;
using System.Collections.Generic;

namespace ClaudeCodeQuest.Core
{
    public enum AgentEventType
    {
        Idle,
        Typing,
        Reading,
        Thinking,
        WaitingInput,
        TurnComplete,
        SessionStart,
        SessionEnd,
        BranchCreated,
        CommitPushed,
        PrOpened,
        PrMerged,
        TicketUpdated,
        SkillUsed,
        Custom
    }

    public class AgentEvent
    {
        public string SessionId { get; set; }
        public string SourcePlugin { get; set; }
        public AgentEventType Type { get; set; }
        public DateTime Timestamp { get; set; }
        public string ToolName { get; set; }
        public string SkillName { get; set; }
        /// <summary>First 120 chars of context from the JSONL line.</summary>
        public string Detail { get; set; }
        public Dictionary<string, string> Metadata { get; set; }

        public AgentEvent()
        {
            Timestamp = DateTime.UtcNow;
            Metadata = new Dictionary<string, string>();
        }
    }

    public interface IIntegrationPlugin
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsEnabled { get; }

        void Initialize(Dictionary<string, string> config);
        /// <summary>Called every frame by PluginManager. Drain internal queue into OnEvent here.</summary>
        void Poll(double delta);
        void Shutdown();

        event Action<AgentEvent>? OnEvent;
    }
}
