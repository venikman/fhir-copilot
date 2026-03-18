---
description: |
  Use when applying functional programming patterns and principles in C# including immutability, pure functions, higher-order functions, and monadic patterns.
  USE FOR: functional programming concepts in C#, immutability patterns, pure functions, LINQ as functional operations, pattern matching, monadic error handling, higher-order functions
  DO NOT USE FOR: F# language specifics, specific FP library APIs (language-ext), parser combinators
source: https://github.com/Tyler-R-Kendrick/agent-skills/tree/main/skills/dotnet/functional/functional-programming
globs: "**/*.cs"
---

# Functional Programming in C#

## Core Principles

| Principle | Description | C# Feature |
|-----------|-------------|------------|
| Immutability | Data does not change after creation | `record`, `readonly`, `init` |
| Pure functions | Same input always produces same output, no side effects | Static methods, expression-bodied members |
| First-class functions | Functions as values, passed as arguments | `Func<>`, `Action<>`, lambda expressions |
| Higher-order functions | Functions that take or return functions | LINQ methods, custom combinators |
| Composition | Building complex operations from simple ones | Extension methods, LINQ chaining |
| Declarative style | Express *what* to compute, not *how* | LINQ, pattern matching |

## Rules (by impact)

### HIGH impact
- **Avoid side effects in functions** — functions should depend only on input and produce no observable mutations.
- **Use `IReadOnlyList<T>`, `IReadOnlyDictionary<K,V>`, and `ImmutableList<T>`** to prevent mutation of collections passed between functions.

### MEDIUM impact
- **Use C# `record` types for domain models** to get immutability, value equality, and `with` expressions for non-destructive updates.
- **Write pure functions wherever possible** — trivially testable and safe for concurrent use.
- **Use LINQ methods (`Select`, `Where`, `Aggregate`, `SelectMany`)** as the standard vocabulary for functional data transformations rather than imperative loops.
- **Implement `Result<T>`** or use language-ext for error handling that forces callers to handle both success and failure paths explicitly.
- **Use pattern matching (`switch` expressions) with exhaustive cases** to handle discriminated types, ensuring compiler warnings when a case is missing.
- **Separate pure business logic from impure I/O** (database, HTTP, file system) at architectural boundaries.
- **Use `Option<T>` instead of null returns** for methods that may not produce a value, making absence explicit in the type signature.
- **Compose small, focused functions into pipelines** rather than large methods with multiple unrelated transformations.

### LOW impact
- **Prefer expression-bodied members (`=>`)** for small pure functions to communicate simple computation with no side effects.
- **Prefer immutable data structures** — use records, readonly fields, and init-only setters.
- **Consider monadic patterns (Option, Result)** for composable error handling and nullable value chaining.

## Patterns

### Immutability
```csharp
public record Address(string Street, string City, string Zip);
public record Customer(Guid Id, string Name, string Email, Address Address, IReadOnlyList<Guid> OrderIds);

var updated = customer with { Email = "new@example.com" }; // non-destructive mutation
```

### Result/Either Pattern
```csharp
public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;
    public sealed record Error(string Message) : Result<T>;

    public TResult Match<TResult>(Func<T, TResult> ok, Func<string, TResult> error) =>
        this switch { Ok(var v) => ok(v), Error(var m) => error(m), _ => throw new InvalidOperationException() };

    public Result<TResult> Map<TResult>(Func<T, TResult> f) =>
        this switch { Ok(var v) => new Result<TResult>.Ok(f(v)), Error(var m) => new Result<TResult>.Error(m), _ => throw new InvalidOperationException() };

    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> f) =>
        this switch { Ok(var v) => f(v), Error(var m) => new Result<TResult>.Error(m), _ => throw new InvalidOperationException() };
}
```

### Option Pattern
```csharp
public readonly struct Option<T>
{
    private readonly T _value;
    private readonly bool _hasValue;
    private Option(T value) { _value = value; _hasValue = true; }
    public static Option<T> Some(T value) => new(value);
    public static Option<T> None => default;
    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) => _hasValue ? some(_value) : none();
    public Option<TResult> Map<TResult>(Func<T, TResult> f) => _hasValue ? Option<TResult>.Some(f(_value)) : Option<TResult>.None;
    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> f) => _hasValue ? f(_value) : Option<TResult>.None;
}
```

### Functional Error Handling Pipeline
```csharp
public static Result<Order> ProcessOrder(CreateOrderRequest request) =>
    ValidateItems(request.Items)
        .Bind(items => ValidateCustomer(request.CustomerId).Map(customer => (customer, items)))
        .Bind(tuple => CalculateTotal(tuple.items).Map(total => new Order(Guid.NewGuid(), tuple.customer.Id, tuple.items, total)))
        .Bind(order => SaveOrder(order));
```

### Higher-Order Functions
```csharp
public static Func<T, bool> CombineFilters<T>(params Func<T, bool>[] predicates)
    => item => predicates.All(p => p(item));
```
