# Contributing to FaceOFFx

We welcome contributions to FaceOFFx! This document provides guidelines for
contributing to the project.

## Getting Started

1. Fork the repository on GitHub
2. Clone your fork locally: `git clone https://github.com/OpenPhysical/FaceOFFx.git`
3. Create a new branch for your feature: `git checkout -b feature/your-feature-name`
4. Make your changes following the guidelines below
5. Submit a pull request

## Development Setup

### Prerequisites

- .NET10 SDK, with .NET8/9/10 runtimes for the complete test matrix
- Visual Studio 2022, VS Code, or JetBrains Rider
- Git

### Building the Project

```bash
# Clone the repository
git clone https://github.com/OpenPhysical/FaceOFFx.git
cd FaceOFFx

# Build the solution
dotnet build

# Run tests
dotnet test

# Run the release CLI
dotnet run --project src/FaceOFFx.Cli --framework net8.0 -- sample.jpg --filesize-target preferred --output portrait.jp2

# Run the diagnostics CLI
dotnet run --project src/FaceOFFx.Diagnostics.Cli --framework net8.0 -- detect --corpus people --output artifacts/diagnostics/detect

# Generate samples from a source-watermarked portrait
dotnet run --project src/FaceOFFx.Diagnostics.Cli --framework net8.0 -- docs samples \
  --input datasets/samples/cardholders/source_watermarked/construction/german-male.png \
  --size-profile preferred \
  --output artifacts/diagnostics/docs-samples
python3 scripts/ReadmeGallery/render_diagnostics.py artifacts/diagnostics/docs-samples
```

Samples require a new or empty output directory. Untagged samples record an automatic sRGB assumption; supported embedded RGB matrix profiles are converted before analysis. The manifest retains source hashes, color evidence, capacity and fixed landmark-mask review. JP2 exports are independently decoded by the development Python renderer. Select a named corpus with `--corpus` instead.

## Code Style Guidelines

### General Principles

- Use C# 12.0 features where appropriate
- Enable nullable reference types
- Follow functional programming patterns (Result\<T>, Maybe\<T>)
- No nulls in domain models - use Maybe\<T> for optional values
- Direct dependencies - use libraries directly without wrapper abstractions

### Formatting

- Use the .editorconfig settings provided
- Run `dotnet format` before committing
- Use meaningful variable and method names
- Keep methods focused and small

### Testing

- Write unit tests for all public methods
- Use AwesomeAssertions for test assertions
- Follow the existing test structure and naming conventions
- Ensure all tests pass before submitting PR

### Comments and Documentation

- Add XML documentation comments to all public APIs
- Keep comments concise and meaningful
- Update README.md if adding new features

## Pull Request Process

1. **Before submitting:**
   - Ensure all tests pass: `dotnet test`
   - Run code formatting: `dotnet format`
   - Update documentation if needed
   - Add/update tests for new functionality

2. **PR Guidelines:**
   - Create a descriptive title
   - Reference any related issues
   - Describe what changes you made and why
   - Include screenshots for UI changes
   - Keep PRs focused - one feature/fix per PR

3. **Review Process:**
   - PRs require at least one approval
   - Address review feedback promptly
   - Keep discussions professional and constructive

## Reporting Issues

When reporting issues, please include:

- Clear description of the problem
- Steps to reproduce
- Expected vs actual behavior
- System information (OS, .NET version)
- Sample code or images if applicable
- Error messages or stack traces

## Feature Requests

We welcome feature requests! Please:

- Check existing issues first
- Describe the use case clearly
- Explain why this feature would be valuable
- Consider submitting a PR if you can implement it

## Code of Conduct

### Our Standards

- Be respectful and inclusive
- Welcome newcomers and help them get started
- Accept constructive criticism gracefully
- Focus on what's best for the community
- Show empathy towards others

### Unacceptable Behavior

- Harassment, discrimination, or personal attacks
- Trolling or inflammatory comments
- Publishing private information without consent
- Any conduct that could be considered inappropriate

## Architecture Guidelines

### Domain-Driven Design

- Keep domain models in Core project
- No external dependencies in Core (except CSharpFunctionalExtensions)
- Use value objects for domain concepts
- Keep business logic in domain models

### Clean Architecture

- Dependencies flow inward (Infrastructure → Core)
- Use interfaces for external dependencies
- Keep frameworks at the edges
- Separate concerns clearly

### Error Handling

- Use Result\<T> for operations that can fail
- Provide meaningful error messages
- Fail fast with descriptive errors
- Log errors appropriately

## Release Process

1. Update version numbers in project files
2. Update CHANGELOG.md with release notes
3. Build and test library/tool packages and verify licenses, bundled models and local consumers
4. Create and push a signed version tag only under release authorization
5. Review CI artifacts; NuGet publication is a separately authorized manual step

## Questions?

If you have questions, feel free to:

- Open an issue for discussion
- Join our community discussions
- Check existing documentation

Thank you for contributing to FaceOFFx!
