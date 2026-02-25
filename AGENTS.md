# Contributor Guidelines

This document outlines the essential steps for contributing to FSharp.Control.AsyncSeq.

## Key Requirements

### 1. Add Comprehensive Tests

- Write tests for all new features in `tests/FSharp.Control.AsyncSeq.Tests/`
- Cover both success cases and edge cases
- Include async-specific scenarios (cancellation, exceptions, etc.)
- Add benchmarks for performance-critical features in `tests/FSharp.Control.AsyncSeq.Benchmarks/`

### 2. Run Code Formatting

- Ensure code follows F# formatting conventions
- Run formatting tools before committing
- Keep code style consistent with the existing codebase

### 3. Build and Test

Build the solution:
```bash
dotnet build FSharp.Control.AsyncSeq.sln
```

Run all tests:
```bash
dotnet test FSharp.Control.AsyncSeq.sln
```

### 4. Update Documentation

- Update relevant documentation in the `docs/` folder
- Add code examples demonstrating new features
- Update API documentation in `.fsi` signature files
- Keep documentation clear and concise

### 5. Update Release Notes

- Add an entry to `RELEASE_NOTES.md` describing your changes
- Reference relevant issue/PR numbers
- Mark breaking changes clearly with **Breaking:** prefix
- Follow the existing format and style

## PR Checklist

Before submitting a pull request, verify:

- [ ] Tests added and passing
- [ ] Code formatted consistently  
- [ ] Build succeeds without warnings
- [ ] Documentation updated
- [ ] Release notes updated
- [ ] Changes described in PR description

Thank you for contributing to FSharp.Control.AsyncSeq!
