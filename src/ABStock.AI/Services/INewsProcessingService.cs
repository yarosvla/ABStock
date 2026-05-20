using ABStock.AI.Models;

namespace ABStock.AI.Services;

public interface INewsProcessingService
{
    NewsSignal Analyze(NewsAnalysisRequest request);
}