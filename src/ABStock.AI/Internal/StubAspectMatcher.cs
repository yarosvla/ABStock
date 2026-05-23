using ABStock.Shared;

namespace ABStock.AI.Internal;

internal sealed class StubAspectMatcher : IAspectMatcher
{
    public AspectMatchResult Match(String newsText, AssetProfile profile)
    {
        return new AspectMatchResult(2, 0, 0, 0.5m);
    }
}