namespace ABStock.Shared;

/// <summary>
/// Well-known agent names shared between the agent strategies and the UI so the
/// latter can recognise specific participants (e.g. to hide the ambient driver).
/// </summary>
public static class AgentNames
{
    /// <summary>The hidden ambient demand driver of the demo simulation.</summary>
    public const string SentimentFlow = "SentimentFlow";
}
