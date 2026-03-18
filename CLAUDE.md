# Project Rules

## Gemini Model Policy
- Allowed models: `gemini-3-flash-preview`, `gemini-3.1-flash-lite-preview`, `gemini-3.1-pro-preview`, `gemini-2.5-flash`, `gemini-2.5-pro`, `gemini-2.0-flash`
- The fallback chain order is: 3-flash → 3.1-flash-lite → 3.1-pro → 2.5-flash → 2.5-pro → 2.0-flash
- NEVER change the model names in any file — this is the user's explicit choice
- This applies to appsettings.json, .env, .env.example, ProviderOptions defaults, and GeminiAgentFrameworkRunner fallbacks

## Model Fallback Chain
On HTTP 429 (rate limit), the runner automatically tries the next model in the chain. Each model gets one attempt per request. Configure via `Provider.GeminiModels` array in appsettings.json or `Provider__GeminiModels__N` env vars.
