using ABStock.Shared;

namespace ABStock.AI.Internal;

internal interface IAspectMatcher
{
    AspectMatchResult Match(String newsText, AssetProfile profile);
}