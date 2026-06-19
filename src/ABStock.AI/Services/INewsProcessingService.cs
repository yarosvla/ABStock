using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public interface INewsProcessingService
{
    Task<NewsSignal> AnalyzeAsync(NewsAnalysisRequest request, CancellationToken ct = default);
}