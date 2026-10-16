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

              A factor is a specific positive or negative change in an asset-related
              business driver that could affect the asset's revenue, profitability,
              competitive position, or market value.

              Each factor MUST describe a concrete, verifiable change that could
              be confirmed or contradicted by a financial news article.

              Examples:
              - "Рост спроса на Tesla Model 3 и Model Y"
              - "Снижение спроса на Tesla Model 3 и Model Y"
              - "Увеличение производственных мощностей Tesla Gigafactory"
              - "Рост себестоимости производства автомобилей Tesla"

              Do NOT generate static topics or characteristics such as:
              - "Спрос на электромобили Tesla"
              - "Конкурентоспособность Tesla"
              - "Зависимость Tesla от поставщиков"

              A factor describes a possible development, not an event
              that has necessarily already occurred.

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

              2. Every factor MUST describe a specific positive or negative
              change affecting the asset.

              GOOD factors:
              - "Рост спроса на серверные процессоры AMD EPYC"
              - "Снижение спроса на ускорители AMD Instinct"
              - "Повышение конкурентоспособности AMD EPYC относительно Intel Xeon"
              - "Снижение конкурентоспособности AMD Instinct относительно NVIDIA"
              - "Улучшение доступа AMD к производственным мощностям TSMC"
              - "Ухудшение доступности HBM-памяти для AMD Instinct"
              - "Расширение использования ROCm разработчиками AI-инфраструктуры"

              BAD factors:
              - "Спрос на серверные процессоры AMD EPYC"
              - "Конкурентоспособность AMD относительно Intel"
              - "Доступность HBM-памяти"

              Every factor must have a clear direction of change.
              Positive factors describe developments beneficial to the asset.
              Negative factors describe developments harmful to the asset.

              3. Factors MUST describe possible changes, not confirmed past events.

              GOOD:
              - "Рост продаж автомобилей Tesla Model 3 и Model Y"
              - "Снижение спроса на ускорители AMD Instinct"
              - "Увеличение производственных мощностей TSMC"

              BAD:
              - "Tesla reported record sales in Q3 2026"
              - "AMD announced a new contract yesterday"
              - "TSMC opened a new factory last month"

              Do NOT include specific dates, completed transactions,
              announcements, or historical events.

              Each factor must be suitable for checking whether
              a future news article confirms or contradicts it.

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

                An increase is not always positive, and a decrease is not always negative.
                For example:
                - "Снижение себестоимости производства Tesla" → isPositive = true
                - "Рост себестоимости производства Tesla" → isPositive = false

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
                - Two differently worded factors describing the same change count as duplicates.
                - Opposite changes in the same underlying driver are allowed
                  and are NOT considered duplicates.

              9. Generate three representations of each factor:
                - "name": a short Russian label for display in the UI.
                - "nameEn": a short English label for embedding generation.
                - "hypothesisEn": a complete, grammatically correct English
                  statement for Natural Language Inference (NLI).

                All three fields MUST describe exactly the same event.
                Do not add, remove, or change any financial meaning.
                Preserve company names, product names, and technologies.

                The hypothesisEn must be a factual statement, not a
                prediction, possibility, or question.

                Distinguish demand, orders, sales, deliveries, production,
                revenue, and market share. An increase in one metric
                does not prove an increase in another.

              10. Translate the provided asset description into English:
                  - Return it in the top-level "descriptionEn" field.
                  - Preserve all factual information from the original description.
                  - Do not invent, remove, or reinterpret information.
                  - Do not include the asset name or industry unless they are present
                    in the original description.

              ---

              ## OUTPUT FORMAT (STRICT JSON ONLY)

              Return ONLY valid JSON. No explanations, no text, no markdown.

              {
                "descriptionEn": "Accurate English translation of the provided asset description",
                "factors": [
                  {
                    "name": "Russian factor name",
                    "nameEn": "Accurate English translation of the same factor",
                    "hypothesisEn": "Actual sales of Tesla Model 3 and Model Y have increased.",
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

              2. CHANGE TEST:
                Does the factor describe a specific positive or negative
                change in an asset-related business driver?
                If no → REJECT it.

                The factor must have a clear direction of change
                and must be suitable for NLI verification.

              3. NEWS TEST:
                Does the factor describe a specific event that has
                already happened, including dates or announcements?
                If yes → REJECT it.

                The factor must describe a possible development
                that future news could confirm or contradict.

              4. DUPLICATE TEST:
                Does another factor describe essentially the same change?
                If yes → REJECT it.

                Opposite changes in the same underlying driver are allowed
                and are NOT considered duplicates.

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
                - every factor describes a specific possible positive or negative change;
                - every factor can be confirmed or contradicted by a news article;
                - no factor is merely a static topic or characteristic;
                - no factors are duplicates or near duplicates.
                - "descriptionEn" is an accurate English translation of the asset description;
                - every factor has "name", "nameEn", and "hypothesisEn";
                - all three fields describe exactly the same event;
                - every "hypothesisEn" is a complete English factual statement.

              If any condition is not satisfied, correct the list before returning the JSON.

              Now generate exactly 40 factors.
              """;
    }
}