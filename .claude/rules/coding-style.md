# Coding Style

## File Organization
- One class per file. File name matches class name exactly.
- File-scoped namespaces everywhere: `namespace MyApp.Domain.Entities;`
- Organize using directives: System → Microsoft → Third-party → Project. Use global usings in `GlobalUsings.cs`.
- Max file length: 300 lines. If longer, the class is doing too much — split it.

## Structure Order Within a Class
1. Constants and static readonly fields
2. Private fields
3. Constructors
4. Public properties
5. Public methods
6. Private methods
7. Nested types (avoid when possible)

## Immutability First
- Default to `readonly`, `init`, and `required` properties.
- Use records for DTOs, value objects, and any data that doesn't need mutation.
- Use `IReadOnlyList<T>`, `IReadOnlyCollection<T>` for return types. Accept `IEnumerable<T>` as parameters.
- Domain entity state changes go through methods, never public setters.

## Expressions and Simplicity
- Prefer expression-bodied members for single-line methods and properties.
- Prefer pattern matching over if-else chains. Use switch expressions where exhaustive.
- Prefer LINQ for collection operations. Avoid manual loops unless performance-critical (benchmark first).
- Prefer `string.IsNullOrWhiteSpace()` over `string.IsNullOrEmpty()`.
- Use `nameof()` instead of magic strings for property/parameter names.

## Comments
- No comment should explain WHAT code does — the code should be self-documenting.
- Comments explain WHY: business reasoning, trade-offs, workarounds, or non-obvious constraints.
- XML doc comments (`///`) on all public APIs. Include `<param>`, `<returns>`, `<exception>`.
- TODO comments must include an issue number: `// TODO(#123): description`
