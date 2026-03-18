# Project Rules

## Gemini Model Policy
- Only use Gemini 3.1.x models: `gemini-3.1-flash` or `gemini-3.1-flash-lite`
- All other versions are NOT allowed (2.x, 3.0.x, pro, etc.)
- NEVER change the model name in any file — this is the user's explicit choice
- This applies to appsettings.json, .env, .env.example, ProviderOptions defaults, and GeminiAgentFrameworkRunner fallbacks

## GeminiModels Fallback Chain Configuration
The application uses a three-model fallback chain for resilience. Models are evaluated in priority order:

1. **Primary model**: `gemini-3.1-flash-preview` — fastest, ideal for most tasks
2. **First fallback**: `gemini-3.1-flash-lite-preview` — reduced capability but faster
3. **Secondary fallback**: `gemini-3.1-pro-preview` — highest capability for complex tasks

### Configuration Hierarchy
Models are specified across three configuration layers with this priority (highest to lowest):

1. **Environment variables** (e.g., `Provider__GeminiModels__0=gemini-3.1-flash-preview`)
2. **appsettings.json** (in the `Provider.GeminiModels` array)
3. **Code defaults** (in ProviderOptions class)

### Configuration Methods

#### Via Environment Variables (Indexed Notation)
```
Provider__GeminiModels__0=gemini-3.1-flash-preview
Provider__GeminiModels__1=gemini-3.1-flash-lite-preview
Provider__GeminiModels__2=gemini-3.1-pro-preview
```

#### Via appsettings.json
```json
{
  "Provider": {
    "GeminiModels": [
      "gemini-3.1-flash-preview",
      "gemini-3.1-flash-lite-preview",
      "gemini-3.1-pro-preview"
    ]
  }
}
```

### Fallback Logic (GetModelChain Method)
The `GetModelChain()` method in `ProviderOptions` implements three-tier fallback:

1. If `GeminiModels` list is configured (count > 0), use it and respect the priority order
2. If `GeminiModels` is empty/null but `GeminiModel` is set, use that single model as a list
3. If both are empty/null, use hardcoded default: `["gemini-3.1-flash-preview"]`

This ensures backward compatibility while supporting the new multi-model strategy.

### Backward Compatibility
The single `GeminiModel` property remains supported for configurations that specify only one model. The `GeminiModels` list takes precedence when configured, allowing gradual migration to the fallback chain strategy.
