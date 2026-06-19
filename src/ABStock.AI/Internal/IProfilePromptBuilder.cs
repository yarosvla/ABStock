using ABStock.AI.Models;

namespace ABStock.AI.Internal;

internal interface IProfilePromptBuilder
{
    string BuildPrompt(
        AssetProfileRequest request);
}