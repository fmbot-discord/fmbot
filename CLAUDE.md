# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See also `../CLAUDE.md` for workspace-level context covering both repos, the database schema, and how the bot and web backend connect.

## Project Overview

FMBot is a production-grade Discord bot that integrates with Last.fm to provide music statistics and social features. Built with .NET 10.0, it serves millions of users across thousands of Discord servers with sophisticated architecture supporting sharding, extensive external API integrations, and advanced features like image generation and AI-powered content.

## Development Commands

### Building the Project
```bash
# Build the entire solution
dotnet build ./src/FMBot.Discord.sln --configuration Release

# Build specific project
dotnet build ./src/FMBot.Bot/FMBot.Bot.csproj --configuration Release
```

### Running Tests
```bash
# Run all tests
dotnet test ./src/FMBot.Tests/FMBot.Tests.csproj

# Run tests with verbose output
dotnet test ./src/FMBot.Tests/FMBot.Tests.csproj --verbosity normal
```

### Local Development
```bash
# Run the bot locally (requires configuration)
dotnet run --project ./src/FMBot.Bot/FMBot.Bot.csproj

# Run with Docker Compose for local development
docker-compose -f docker/docker-compose-local.yml up
```

### Database Migrations
```bash
# Add new migration
dotnet ef migrations add MigrationName --project ./src/FMBot.Persistence.EntityFrameWork --startup-project ./src/FMBot.Bot

# Update database
dotnet ef database update --project ./src/FMBot.Persistence.EntityFrameWork --startup-project ./src/FMBot.Bot
```

## Architecture Overview

### Project Structure
- **FMBot.Bot** - Main Discord bot application and entry point
- **FMBot.Core** - Shared helpers used by both the bot and the web backend
- **FMBot.Persistence** / **FMBot.Persistence.Domain** / **FMBot.Persistence.EntityFrameWork** - Data layer with EF Core, Dapper repositories, entities and migrations
- **FMBot.Domain** - Shared domain models, enums and interfaces
- **FMBot.LastFM** / **FMBot.LastFM.Domain** - Last.fm API integration and music data services
- **FMBot.Images** - Image generation using Puppeteer and SkiaSharp
- **FMBot.AppleMusic** - Apple Music API integration
- **FMBot.Discogs** - Discogs API integration for record collections
- **FMBot.Subscriptions** - Supporter/subscription logic (Stripe, Discord entitlements, OpenCollective)
- **Protos/** - gRPC contract shared with the web backend (proto files only, no project) — see `../CLAUDE.md`
- **FMBot.Tests** - NUnit test suite

`FMBot.Youtube` still exists on disk but is not in the solution and nothing references it; ignore it.

### Key Entry Points
- `src/FMBot.Bot/Program.cs` - Application bootstrap
- `src/FMBot.Bot/Startup.cs` - DI configuration and service registration
- `src/FMBot.Bot/Handlers/CommandHandler.cs` - Text command routing
- `src/FMBot.Bot/Handlers/InteractionHandler.cs` - Slash command routing

### Key Architectural Patterns

**Command Pattern**: Discord interactions are handled through:
- Slash commands in `SlashCommands/` directory
- Text commands in `TextCommands/` directory
- Separate command handlers for business logic

**Builder Pattern**: Response construction uses dedicated builders:
- `AlbumBuilders`, `ArtistBuilders`, `ChartBuilders`, etc.
- Each builder handles specific Discord embed formatting

**Repository Pattern**: Data access abstracted through:
- Interfaces in `FMBot.Domain/Interfaces/`
- Implementations in `FMBot.Persistence/Repositories/`
- Entity Framework Core with PostgreSQL

**Dependency Injection**: Extensive use of Microsoft DI with service registration organized by category in `Startup.cs`

**Response Pattern**: Commands return `ResponseModel` objects built via builders:
1. Handler receives interaction → creates `ContextModel`
2. Service/Builder processes request → returns `ResponseModel`
3. Handler sends response embed/components to Discord

### Database Architecture
- **PostgreSQL** with Entity Framework Core
- **Snake_case** naming convention
- **Migrations were reset in March 2026** (`InitialMigration` 2026-03-14); everything before that is gone from the repo, so don't look for older history
- **Key entities**: Users, Guilds, Artists, Albums, Tracks, UserPlays, UserCrowns
- **PostgreSQL extensions**: citext, pg_trgm (text search), hstore (`user_interactions.command_options`), unaccent

### External API Integrations
Primary services: Last.fm (core), Spotify (features), Apple Music (metadata), YouTube (videos), Discogs (collections), MusicBrainz (metadata), OpenAI (AI features), Genius (lyrics)

### Docker & Deployment
- **Multi-stage Dockerfile** with .NET 10.0 runtime
- **Puppeteer/Chrome** for image generation
- **Sharding support** for Discord bot scaling
- **Health checks** and monitoring capabilities
- **Diagnostic tools** pre-installed in containers

## Development Workflow

### Branch Strategy
- `main` - Production branch (stable releases)
- `dev` - Development branch (active development)
- Create PRs against `dev` branch for new features

### Configuration
- JSON-based configuration with environment variable overrides
- Configuration files in `configs/` directory (not tracked in git)
- Multiple environment support (local, dev, prod)

### Code Conventions
- **`LangVersion` latest** (C# 14). Nullable reference types are **not** enabled in the bot projects (only `FMBot.Tests` turns them on), so `?` annotations are informational, not enforced — hence the `userSettings` null gotcha below
- **NetCord** framework for Discord API interactions (migrated from Discord.Net). When unsure about NetCord APIs or types, you can look up documentation, search online, or read the local source code at `P:\NetCord` (Windows) / `/Users/thom/projects/NetCord` (macOS)
- **Async/await** patterns throughout
- **Structured logging** with Serilog
- **Extension methods** for common operations

### Naming Conventions
- Services: `*Service.cs` (e.g., `AlbumService.cs`)
- Builders: `*Builders.cs` (plural, e.g., `AlbumBuilders.cs`)
- Slash commands: `*SlashCommands.cs`
- Text commands: `*Commands.cs`
- Database columns: `snake_case`
- C# properties: `PascalCase`

### Testing
- **NUnit 4** testing framework (no mocking library; tests are mostly pure-function and file-based, e.g. `LocalizationTests`, `HelpServiceTests`)
- Test files organized in `FMBot.Tests/` project
- Focus on service layer and business logic testing
- Minimal integration tests due to external API dependencies

## Localization

User-facing strings are localized via JSON files, managed in Weblate. The full rules (code API, key and plural conventions, protected terms, never-localize list, translation style) are in the `localization` skill at `.claude/skills/localization/SKILL.md` — read it before adding or migrating user-facing strings, editing locale files, writing translations, or changing any slash command or parameter description (those changes include writing 11 translations). Weblate/glossary/rollout details live in `../LOCALIZATION_NOTES.md`. Never skip these:

- A NEW `en.json` key must be seeded into all 11 other locale files in the same change (`EveryEnglishKeyExistsInEveryLocale` fails otherwise)
- Editing a `[SlashCommand]`/`[SlashCommandParameter]` description or adding/removing a parameter means updating all 11 translated `SlashCommandLocalizations` files in the same change — no test catches drift there. Never add `name` keys
- Full-sentence keys only — NEVER concatenate translated fragments into a sentence
- After touching locale files or adding `Localize` calls, run `dotnet test ./src/FMBot.Tests/FMBot.Tests.csproj`

## Common Gotchas
- Always check `userSettings` for null - user may not be registered with Last.fm
- Guild-specific features require `GuildId` from context
- Rate limits: Last.fm has aggressive limits, use caching where possible
- Image generation requires Puppeteer - won't work without Chrome installed
- Background jobs use Hangfire - check `TimerService` for scheduled tasks

## Adding a New Command

### Slash Command
1. Add method in appropriate `SlashCommands/*SlashCommands.cs` file
2. Create/update builder in `Builders/` for response construction
3. Register any new services in `Startup.cs`

### Text Command
1. Add method in appropriate `TextCommands/*Commands.cs` file
2. Reuse existing builders where possible
3. Text commands often mirror slash commands - check for existing implementation

## File Organization

Important directories:
- `src/FMBot.Bot/SlashCommands/` - Discord slash command implementations
- `src/FMBot.Bot/TextCommands/` - Text command implementations
- `src/FMBot.Bot/Handlers/` - Command and interaction routing
- `src/FMBot.Bot/Services/` - Business logic services
- `src/FMBot.Bot/Builders/` - Response building logic
- `src/FMBot.Bot/Models/` - DTOs including `ContextModel`, `ResponseModel`
- `src/FMBot.Persistence.EntityFrameWork/Migrations/` - Database migrations
- `src/FMBot.Images/` - Image generation services
- `docker/` - Docker Compose configurations

When working with commands, check both `SlashCommands/` and `TextCommands/` directories as the bot supports both interaction types.