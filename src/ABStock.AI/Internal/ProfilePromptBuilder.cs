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

              A factor is a persistent characteristic, dependency, source of risk, or source of growth
              to which the asset's revenue, profitability, competitive position, or market value is sensitive.

              A factor is NOT a news event and must NOT describe something that has already happened.

              ---

              ## ASSET CONTEXT
              Name: {{request.Name}}
              Type: {{request.AssetType}}
              Description: {{request.Description}}
              Industry: {{(string.IsNullOrWhiteSpace(request.Industry) ? "not specified" : request.Industry)}}
              Government support: {{(request.IncludeGovernmentSupport ? "yes" : "not specified")}}

              ---

              ## CORE RULES (CRITICAL)

              1. Every factor MUST be directly and specifically related to this asset.
                - Do NOT generate generic market or macroeconomic statements.
                - Avoid generic factors such as:
                  - "market growth"
                  - "industry trends"
                  - "economic conditions"
                  - "increasing demand" without specifying demand for what

              2. A factor MUST describe a persistent driver, dependency, competitive condition, risk,
                or source of growth for the asset.

                GOOD factors:
                - "Спрос гиперскейлеров на серверные процессоры AMD EPYC"
                - "Спрос операторов дата-центров на ускорители AMD Instinct"
                - "Конкурентоспособность AMD EPYC относительно Intel Xeon"
                - "Конкурентоспособность AMD Instinct относительно NVIDIA в AI-нагрузках"
                - "Доступ AMD к передовым техпроцессам и производственным мощностям TSMC"
                - "Доступность HBM-памяти для ускорителей AMD Instinct"
                - "Распространённость ROCm среди разработчиков и операторов AI-инфраструктуры"

              3. Do NOT describe concrete events as factors.

                BAD factors:
                - "AWS заключил крупный контракт на поставку AMD EPYC"
                - "TSMC выделила AMD приоритетные 3-нм квоты"
                - "Microsoft продлил контракт с AMD"
                - "AMD получила субсидии по CHIPS Act"
                - "Суд обязал AMD выплатить компенсацию"
                - "Бенчмарки показали отставание MI300 от NVIDIA"

                These are NEWS EVENTS, not persistent asset factors.

                Instead describe the underlying driver:
                - BAD: "AWS заключил крупный контракт на поставку AMD EPYC"
                  GOOD: "Спрос крупных облачных провайдеров на серверные процессоры AMD EPYC"

                - BAD: "TSMC выделила AMD приоритетные 3-нм квоты"
                  GOOD: "Доступ AMD к передовым производственным мощностям TSMC"

                - BAD: "AMD получила субсидии по CHIPS Act"
                  GOOD: "Доступ AMD к государственным субсидиям и программам поддержки полупроводникового производства"

                - BAD: "Бенчмарки показали отставание MI300 от NVIDIA"
                  GOOD: "Производительность ускорителей AMD Instinct относительно конкурирующих ускорителей NVIDIA"

              4. Generate EXACTLY 20 positive and EXACTLY 20 negative factors.
                - Positive factors: isPositive = true
                - Negative factors: isPositive = false
                - Exactly 20 factors MUST have isPositive = true.
                - Exactly 20 factors MUST have isPositive = false.
                - Do not generate more or fewer factors of either polarity.

              5. The polarity describes the direction in which the factor affects the asset.

                Examples:
                - "Рост спроса гиперскейлеров на AMD EPYC" → isPositive = true
                - "Снижение спроса гиперскейлеров на AMD EPYC" → isPositive = false
                - "Рост конкурентоспособности AMD Instinct относительно NVIDIA" → isPositive = true
                - "Снижение конкурентоспособности AMD Instinct относительно NVIDIA" → isPositive = false

              6. Importance must reflect how strongly this factor can influence the asset price,
                revenue, profitability, or competitive position:
                - Range: 0.0 to 1.0
                - 1.0 = critical structural driver
                - 0.3 = minor secondary effect

              7. Generate EXACTLY 40 factors total.
                - The "factors" array MUST contain exactly 40 elements.
                - Do not return fewer than 40 factors.
                - Do not return more than 40 factors.

              8. Factors must be non-overlapping.
                - Do not generate duplicates or near duplicates.
                - Two differently worded factors describing the same underlying driver count as duplicates.

              9. Write every factor name in Russian.
                The application UI and incoming news are in Russian.

              ---

              ## OUTPUT FORMAT (STRICT JSON ONLY)

              Return ONLY valid JSON. No explanations, no text, no markdown.

              {
                "factors": [
                  {
                    "name": "string (asset-specific persistent driver)",
                    "isPositive": true,
                    "importance": 0.0
                  }
                ]
              }

              ---

              ## QUALITY REQUIREMENTS

              Each factor must pass ALL of these tests:

              1. ASSET SPECIFICITY:
                Is the factor specifically connected to this asset, its products, technologies,
                customers, suppliers, competitors, regulation, or business model?
                If no → REJECT it.

              2. DRIVER TEST:
                Does the factor describe something to which the asset is persistently sensitive?
                If no → REJECT it.

              3. NEWS TEST:
                Does the factor sound like a headline describing a specific event that already happened?
                If yes → REJECT it and rewrite it as the underlying persistent driver.

              4. DUPLICATE TEST:
                Does another factor already represent essentially the same underlying driver?
                If yes → REJECT it.

              Prefer factors related to:
                - demand for specific products or product families;
                - dependence on specific suppliers or technologies;
                - competitive position against specific competitors;
                - product performance and technological capabilities;
                - adoption of asset-specific software or ecosystems;
                - exposure to asset-specific regulation and export restrictions;
                - dependence on specific customer categories;
                - manufacturing capacity and supply-chain dependencies.

              Avoid:
                - macroeconomics;
                - vague sentiment;
                - generic industry statements;
                - descriptions of one-time events;
                - invented contracts, court decisions, grants, benchmarks, or other news.

              ---

              Before returning the JSON, verify:
                - the "factors" array contains EXACTLY 40 factors;
                - EXACTLY 20 factors have isPositive = true;
                - EXACTLY 20 factors have isPositive = false;
                - every factor describes a persistent driver rather than a news event;
                - no factors are duplicates or near duplicates.

              If any condition is not satisfied, correct the list before returning the JSON.

              Now generate exactly 40 factors.
              """;
    }
}