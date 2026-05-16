using ABStock.AI.Models;

namespace ABStock.AI.Internal;

internal interface IAspectMatcher
{
    AspectMatchResult Match(String newsText, AssetProfile profile);
}