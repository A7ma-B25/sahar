namespace Sahar.Core.Activities;

/// <summary>What Claude Code is doing, from its tool name (spec 0.13 §2 Claude Code).</summary>
public enum ClaudeStep { None, Editing, Reading, Running, Searching, Web }
