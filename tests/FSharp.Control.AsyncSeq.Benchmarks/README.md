# AsyncSeq Performance Benchmarks

This project contains systematic performance benchmarks for FSharp.Control.AsyncSeq using BenchmarkDotNet.

## Purpose

These benchmarks provide:
- **Baseline Performance Metrics**: Establish current performance characteristics
- **Regression Detection**: Detect performance regressions in future changes  
- **Optimization Validation**: Measure impact of performance improvements
- **Issue Verification**: Verify fixes for known performance issues (#35, #57, etc.)

## Benchmark Suites

### Core Operations (`core`)
Tests fundamental AsyncSeq operations:
- `unfoldAsync` - Core sequence generation
- `replicate` - Simple constant generation
- `mapAsync` - Common transformation operation
- `chooseAsync` - Filtering with async projection

### Append Operations (`append`) 
Tests append functionality (previously had memory leaks):
- `ChainedAppends` - Sequential append operations
- `MultipleAppends` - Batch append operations

### Builder Patterns (`builder`)
Tests computation expression performance (previously O(n²)):
- `RecursiveAsyncSeq` - Recursive `yield!` patterns
- `UnfoldAsyncEquivalent` - Optimized equivalent using `unfoldAsync`

## Usage

### Build and Run
```bash
# Build benchmarks
dotnet build tests/FSharp.Control.AsyncSeq.Benchmarks -c Release

# Run all benchmark suites
dotnet run --project tests/FSharp.Control.AsyncSeq.Benchmarks -c Release

# Run specific suite
dotnet run --project tests/FSharp.Control.AsyncSeq.Benchmarks -c Release -- core
dotnet run --project tests/FSharp.Control.AsyncSeq.Benchmarks -c Release -- append
dotnet run --project tests/FSharp.Control.AsyncSeq.Benchmarks -c Release -- builder
```

### Interpreting Results

BenchmarkDotNet provides detailed metrics including:
- **Mean**: Average execution time
- **Error**: Standard error of measurements  
- **StdDev**: Standard deviation
- **Gen0/Gen1/Gen2**: Garbage collection counts
- **Allocated**: Memory allocations per operation

### Key Performance Indicators

Watch for:
- **Linear scaling**: Execution time should scale linearly with element count
- **Low GC pressure**: Minimal Gen0/Gen1 collections for streaming operations
- **Constant memory**: No memory leaks in long-running operations
- **No O(n²) patterns**: Performance should not degrade quadratically

## Integration with Performance Plan

These benchmarks support the performance improvement plan phases:

**Round 1 (Foundation)**:
- ✅ Systematic benchmarking infrastructure (this project)
- ✅ Baseline performance documentation
- 🔄 Performance regression detection

**Round 2-4**: 
- Use these benchmarks to measure optimization impact
- Add new benchmarks for additional performance improvements
- Validate performance targets and success metrics

## Known Issues Being Monitored

- **Issue #35**: Memory leaks in append operations (fixed in PR #193)
- **Issue #57**: O(n²) performance in recursive patterns (fixed in 2016)  
- **Issue #50**: Excessive append function calls
- **Issues #74, #95**: Parallelism inefficiencies

Results from these benchmarks help verify these issues remain resolved and detect any regressions.