using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public interface INewsProcessingService
{
    NewsSignal Analyze(NewsAnalysisRequest request);
}