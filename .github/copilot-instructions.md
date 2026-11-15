# Copilot Coding Agent Instructions for Elsa Workflows

## Repository Overview

**Elsa Workflows** is a powerful .NET workflow library that enables workflow execution within any .NET application. This is version 3.0, supporting .NET 10.0 and .NET 11.0 and providing server APIs and programmatic workflow definition capabilities. This repository builds backend libraries and reference servers. Elsa Studio is maintained and hosted separately; Studio applications and deployment artifacts are not included here.

### Key Statistics
- **Language**: C# (.NET 11 SDK)
- **Architecture**: Modular library with 104+ projects
- **Code Size**: ~3,500 C# files across modules
- **License**: MIT
- **Build System**: .NET CLI and MSBuild for build/package automation; Microsoft Testing Platform for tests
- **Target Frameworks**: .NET 10.0 and .NET 11.0

## High-Level Architecture

### Directory Structure
```
src/
├── apps/               # Local orchestration, reference server, and telemetry
│   ├── Elsa.AppHost               # Aspire development graph (.NET 11)
│   ├── Elsa.Server.Web            # Workflow server only
│   └── Elsa.ServiceDefaults       # Shared OpenTelemetry configuration
├── common/             # Shared libraries (8 projects)
├── modules/            # Core functionality modules (70+ projects)
│   ├── Elsa.Workflows.Core         # Core workflow engine
│   ├── Elsa.Workflows.Runtime      # Runtime execution
│   ├── Elsa.Workflows.Api          # REST API
│   ├── Elsa.Http                   # HTTP activities
│   ├── Elsa.Email                  # Email activities
│   └── [many others]               # Database, messaging, etc.
└── clients/            # API clients
test/
├── unit/               # Unit tests
├── integration/        # Integration tests  
├── component/          # Component tests
└── performance/        # Performance tests
docker/                 # Docker configurations
```

### Core Components
- **Elsa.Workflows.Core**: Main workflow engine and activities
- **Elsa.Workflows.Runtime**: Workflow execution runtime
- **Elsa.Workflows.Api**: RESTful API for workflow management
- **Elsa.Workflows.Management**: Workflow definition management
- **Elsa modules**: Specialized functionality (HTTP, persistence, scheduling, etc.)

## Build Instructions

### Prerequisites
- **.NET SDK 11.0.100**, selected by `global.json` with patch roll-forward and prereleases disabled
- **.NET 10 runtime** alongside the build SDK to run both supported test and app targets
- **Build time**: Initial restore ~1-2 minutes, full compile ~5-10 minutes

### Build Commands

**Build interface**: standard `dotnet` commands on Linux, macOS, and Windows

```bash
# View build options
dotnet build --help

# Clean build artifacts
dotnet clean Elsa.slnx

# Restore packages (may show warnings for inaccessible feeds)
dotnet restore Elsa.slnx --ignore-failed-sources

# Build the solution
dotnet build Elsa.slnx --no-restore

# Create versioned NuGet packages (restores and builds implicitly)
dotnet pack Elsa.slnx --configuration Release --output packages -p:Version=3.9.0-preview.1

# Run the complete repository test suite through Microsoft Testing Platform
dotnet test --solution Elsa.slnx
```

Builds default to Debug, and CI commands explicitly select Release. Pass
`--configuration` to override. Packages go to `packages/`, while build and
intermediate outputs remain in each project's `bin/` and `obj/` directories.
Package versions come from metadata or an explicit `-p:Version=<version>`; no
timestamp suffix is generated. Use `pack --no-build` only after building the
same configuration and version.

**Targeted dotnet commands** for core components:
```bash
# Build specific core projects that don't require external packages
dotnet restore src/modules/Elsa.Workflows.Core/ --ignore-failed-sources
dotnet build src/modules/Elsa.Workflows.Core/ --no-restore
dotnet restore src/modules/Elsa.Workflows.Runtime/ --ignore-failed-sources  
dotnet build src/modules/Elsa.Workflows.Runtime/ --no-restore

# Build and test an individual core module
dotnet build src/modules/Elsa.Workflows.Core/Elsa.Workflows.Core.csproj --no-restore
dotnet test --project test/unit/Elsa.Workflows.Core.UnitTests/Elsa.Workflows.Core.UnitTests.csproj
```

### Expected Build Warnings
- `NU1900`: Unable to load service index for external feeds (safe to ignore)
- `NU1801`: Service index warnings for feedz.io sources (safe to ignore)

### Successful Build Indicators
- Core modules (Elsa.Workflows.Core, etc.) compile successfully
- Server applications (Elsa.Server.Web) build
- Most modules show "succeeded with X warning(s)" (warnings are acceptable)

## Testing

### Test Structure
- **Unit tests**: `test/unit/` - Fast, isolated tests
- **Integration tests**: `test/integration/` - End-to-end scenarios
- **Component tests**: `test/component/` - Feature testing
- **Performance tests**: `test/performance/` - Benchmarks

### Running Tests
```bash
# Complete repository suite
dotnet test --solution Elsa.slnx

# Focused project while iterating
dotnet test --project test/unit/Elsa.Workflows.Core.UnitTests/Elsa.Workflows.Core.UnitTests.csproj
```

The root `global.json` selects Microsoft Testing Platform (MTP), and all test
projects use the TUnit version managed in `Directory.Packages.props`. Use `[Test]` for a test case,
`[Arguments(...)]` for parameterized cases, and await TUnit assertions, for
example `await Assert.That(actual).IsEqualTo(expected)`.

The literal `--` separates the outer `dotnet test` command from each TUnit test
application. Put solution/project selection and aggregate MTP options before
`--`; put forwarded options such as `--coverage`, `--report-trx`, and
`--coverage-output-format` after it.

Coverage is collected by the Microsoft Testing Platform coverage extension
using `test/coverage.settings.xml` and evaluated as an aggregate in the packages
workflow. Do not add Coverlet packages, MSBuild properties, or per-project
coverage thresholds.

## Development Guidelines

### Code Standards
- **Language version**: C# latest
- **Target frameworks**: .NET 10.0 and .NET 11.0
- **Nullable reference types**: Enabled
- **Implicit usings**: Enabled
- **EditorConfig**: Configured (4-space indentation, CRLF line endings)

### Architecture Patterns
- **Modular design**: Each feature area is a separate project/module
- **Dependency injection**: Heavy use of Microsoft.Extensions.DependencyInjection
- **Activity-based**: Workflows are built from composable activities
- **Async/await**: Extensive use throughout for scalability

### Common Gotchas
1. **NuGet Source Mapping**: Configured in NuGet.Config, restricts where packages can be sourced
2**Multiple Target Frameworks**: Some projects conditionally target different frameworks
3**Build Warnings**: Many NU1900/NU1801 warnings are expected and safe

## Continuous Integration

### GitHub Actions Workflow
- **Trigger**: Pull requests to `main` branch
- **Runner**: ubuntu-latest  
- **.NET SDK**: selected by `global.json`, plus the .NET 10 runtime to run both test targets
- **Commands**: `dotnet restore Elsa.slnx`, `dotnet build Elsa.slnx --configuration Release --no-restore`,
  then `dotnet test --solution Elsa.slnx --configuration Release --no-build`
- **File**: `.github/workflows/pr.yml`

### CI Pipeline Steps
1. Checkout code
2. Set up the SDK selected by `global.json` and the .NET 10 runtime
3. Restore and build with the .NET CLI
4. Run all TUnit applications through the root MTP entry point
5. Expected warnings for external feed access

The packages workflow runs the native unit/integration and component solution
filters in a .NET 10/.NET 11 matrix, collects Microsoft Cobertura coverage in
separate artifacts, and applies the shared aggregate coverage gate after the runs complete.

## Key Configuration Files

- **Build**: `Directory.Build.props` and `Directory.Build.targets` (shared MSBuild settings)
- **SDK and test runner**: `global.json`
- **Dependencies**: `Directory.Packages.props` (central package management)
- **Global settings**: `Directory.Build.props`
- **NuGet**: `NuGet.Config` (package sources and mapping)
- **Solution**: `Elsa.slnx` (119 projects)
- **Docker**: `docker/` directory with multiple Dockerfiles
- **GitHub Actions**: `.github/workflows/`

## Docker Support

Multiple Docker configurations available:
- `ElsaServer.Dockerfile` - Server only
- `ElsaServer-Datadog.Dockerfile` - Server with Datadog instrumentation
- Docker Compose configurations for development

## Quick Start for Development

1. **Clone and build core components**:
   ```bash
   git clone https://github.com/DenDeline/flamme.git
   cd flamme
   dotnet restore Elsa.slnx --ignore-failed-sources
   dotnet build Elsa.slnx --no-restore
   ```

2. **Work with core modules**:
   ```bash
   dotnet build src/modules/Elsa.Workflows.Core/Elsa.Workflows.Core.csproj
   dotnet test --project test/unit/Elsa.Workflows.Core.UnitTests/Elsa.Workflows.Core.UnitTests.csproj
   ```

3. **Work with core modules that don't require external dependencies**:
   ```bash
   cd src/modules/Elsa.Workflows.Core
   dotnet restore --ignore-failed-sources
   dotnet build --no-restore
   ```

## Running the Applications

### Aspire Development Graph

The root `aspire.config.json` selects `src/apps/Elsa.AppHost/Elsa.AppHost.csproj`.
From the repository root, use Aspire CLI 13.6.5 to start the reference server and
PostgreSQL with isolated ports:

```bash
aspire start --non-interactive --isolated --apphost src/apps/Elsa.AppHost/Elsa.AppHost.csproj
aspire wait server --non-interactive --apphost src/apps/Elsa.AppHost/Elsa.AppHost.csproj
```

See the [Aspire development guide](../doc/aspire-development.md) for endpoint
discovery, the workflow demo, telemetry, shutdown, and retained data.

### Development Workflow Server

To run the workflow server for development:

```bash
cd src/apps/Elsa.Server.Web
dotnet restore --ignore-failed-sources
dotnet run --framework net11.0
```

The server will start on the configured ports (check `appsettings.json` or environment variables).

## Troubleshooting

### Common Issues

1. **Restore Fails**: Verify the configured NuGet source is reachable and that the selected SDK is installed.
   - Use `dotnet restore Elsa.slnx --locked-mode` for reproducible CI validation
   - Regenerate and review locks after intentional dependency changes

2. **Test Failures**: If many tests fail to run:
   - External package dependencies may be unavailable
   - Run a specific project with `dotnet test --project <csproj>`
   - Remember that options after `--` are passed to every selected TUnit application

3. **Slow Initial Build**: First restore and compile can take 5-10+ minutes:
   - This is normal for a large solution with 100+ projects
   - Subsequent builds are much faster (incremental)
   - Consider building specific projects/modules when iterating

## Additional Resources

### Documentation
- **Official Documentation**: [https://docs.elsaworkflows.io/](https://docs.elsaworkflows.io/)
- **README**: See [README.md](../README.md) for quick start and features overview
- **Contributing Guide**: See [CONTRIBUTING.md](../CONTRIBUTING.md) for contribution guidelines

### Community Support
- **GitHub Issues**: [Report bugs, request features, and ask questions](https://github.com/DenDeline/flamme/issues)
- **Discord**: [Join the community chat](https://discord.gg/hhChk5H472)
- **Stack Overflow**: [Tag: elsa-workflows](http://stackoverflow.com/questions/tagged/elsa-workflows)

### Enterprise Support
- **ELSA-X**: [Professional support and enterprise solutions](https://elsa-x.io)

## Important Notes for Coding Agents

1. **Always use `--ignore-failed-sources`** when restoring packages
2. **Focus on core workflow functionality**
3. **Build warnings are normal** - don't try to fix NU1900/NU1801 warnings
4. **Use `dotnet test --project <csproj>` while iterating and `dotnet test --solution Elsa.slnx` before broad changes are handed off**
5. **Use targeted dotnet commands** for individual components while iterating
6. **Check project references** before attempting builds - some projects have conditional references
7. **Start with core modules** like `Elsa.Workflows.Core`, `Elsa.Workflows.Runtime` which are more likely to build successfully

Trust these instructions for build and development workflows. Only search for additional information if these instructions are incomplete or found to be incorrect.
