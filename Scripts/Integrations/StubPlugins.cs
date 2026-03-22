using System;
using System.Collections.Generic;
using ClaudeCodeQuest.Core;

namespace ClaudeCodeQuest.Integrations
{
    /// <summary>
    /// Stub plugins — interface-only, not yet implemented.
    /// Config shapes are defined so users can see what's coming.
    /// IsEnabled always returns false; Poll is a no-op.
    /// </summary>

    public class StubJiraPlugin : IIntegrationPlugin
    {
        public string Id => "jira";
        public string DisplayName => "Jira";
        public bool IsEnabled => false;

        public event Action<AgentEvent>? OnEvent;

        // Expected config keys: base_url, api_token, project_key, poll_interval_sec
        public void Initialize(Dictionary<string, string> config) { }
        public void Poll(double delta) { }
        public void Shutdown() { }
    }

    public class StubGitPlugin : IIntegrationPlugin
    {
        public string Id => "git";
        public string DisplayName => "Git";
        public bool IsEnabled => false;

        public event Action<AgentEvent>? OnEvent;

        // Expected config keys: repo_paths (comma-separated)
        public void Initialize(Dictionary<string, string> config) { }
        public void Poll(double delta) { }
        public void Shutdown() { }
    }

    public class StubGitHubPrPlugin : IIntegrationPlugin
    {
        public string Id => "github-pr";
        public string DisplayName => "GitHub PR";
        public bool IsEnabled => false;

        public event Action<AgentEvent>? OnEvent;

        // Expected config keys: repo, token, poll_interval_sec
        public void Initialize(Dictionary<string, string> config) { }
        public void Poll(double delta) { }
        public void Shutdown() { }
    }

    public class StubSkillTrackerPlugin : IIntegrationPlugin
    {
        public string Id => "skill-tracker";
        public string DisplayName => "Skill Tracker";
        public bool IsEnabled => false;

        public event Action<AgentEvent>? OnEvent;

        public void Initialize(Dictionary<string, string> config) { }
        public void Poll(double delta) { }
        public void Shutdown() { }
    }
}
