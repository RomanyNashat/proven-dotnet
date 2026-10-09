---
name: benchmark
description: Create and run BenchmarkDotNet benchmarks for performance-critical code paths. Compare EF Core vs Dapper, tracked vs untracked, different caching strategies, etc.
allowed-tools: Read, Write, Edit, Bash, Grep, Glob
---

## Benchmark Workflow

1. **Identify** the code path to benchmark (hot query, serialization, encryption, etc.)

2. **Create** benchmark class with `[MemoryDiagnoser]`:
   - Reference `skills/code-quality/` for BenchmarkDotNet patterns
   - Include baseline and comparison variants
   - Use `[GlobalSetup]` for realistic test data

3. **Run** benchmarks:
   ```bash
   dotnet run -c Release --project Benchmarks/ -- --filter "*<BenchmarkClass>*"
   ```

4. **Analyze** results:
   - Compare Mean execution time
   - Check Allocated memory (Gen0/Gen1/Gen2 collections)
   - Identify if the optimization justifies the code complexity

5. **Document** findings in a comment or ADR if the benchmark led to an architectural decision.

Common benchmarks to run:
- EF Core tracked vs `AsNoTracking()` vs compiled query vs Dapper
- `System.Text.Json` vs manual serialization
- `string.Concat` vs `StringBuilder` vs `string.Create()`
- AES-GCM encryption throughput
- Redis `GetAsync` vs `IMemoryCache` for hot data
