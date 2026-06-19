using ABStock.AI.Models;

namespace ABStock.AI.Internal;

internal sealed class ProfilePromptBuilder
    : IProfilePromptBuilder
{
    public string BuildPrompt(
        AssetProfileRequest request)
    {
      return $$"""
              You are a senior financial analyst and quantitative researcher.

              Your task is to generate a set of highly specific, asset-dependent market drivers for the given financial asset.

              ---

              ## ASSET CONTEXT
              Name: {request.Name}
              Type: {request.AssetType}
              Description: {request.Description}

              ---

              ## CORE RULES (CRITICAL)

              1. Every factor MUST be directly and uniquely related to this asset.
                - Do NOT generate generic market or macroeconomic statements.
                - Avoid phrases like:
                  - "market growth"
                  - "industry trends"
                  - "economic conditions"
                  - "increasing demand" (without specifying what exactly)

              2. Each factor must describe a concrete causal relationship with the asset.
                Examples of GOOD factors:
                - "Increased demand for NVIDIA H100 GPUs from hyperscaler AI data centers"
                - "Restrictions on export of advanced AI chips to China affecting NVIDIA revenue"
                - "Adoption of CUDA ecosystem by enterprise AI workloads increasing lock-in"

              3. Each factor must clearly be either:
                - Positive impact for the asset (isPositive = true)
                - Negative impact for the asset (isPositive = false)

              4. Importance must reflect how strongly this factor can influence the asset price or revenue:
                - Range: 0.0 to 1.0
                - 1.0 = critical structural driver
                - 0.3 = minor secondary effect

              5. Generate 40 to 50 factors total.

              6. Factors must be non-overlapping (no duplicates or near duplicates).

              ---

              ## OUTPUT FORMAT (STRICT JSON ONLY)

              Return ONLY valid JSON. No explanations, no text, no markdown.

              {
                "factors": [
                  {
                    "name": "string (asset-specific driver)",
                    "isPositive": true,
                    "importance": 0.0
                  }
                ]
              }

              ---

              ## QUALITY REQUIREMENTS

              - Each factor must pass this test:
                "If I remove the asset name, does the factor still remain meaningful and specific?"
                If yes → REJECT it.

              - Prefer:
                - supply chain specific effects
                - product-level demand drivers
                - regulatory events affecting this exact company/product
                - technological dependencies specific to this asset

              - Avoid:
                - macroeconomics
                - vague sentiment
                - generic industry statements

              ---

              Now generate the factors.
              """;
    }
}