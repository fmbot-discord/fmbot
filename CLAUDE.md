# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FMBot is a production-grade Discord bot that integrates with Last.fm to provide music statistics and social features. Built with .NET 9.0, it serves millions of users across thousands of Discord servers with sophisticated architecture supporting sharding, extensive external API integrations, and advanced features like image generation and AI-powered content.

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
- **FMBot.Persistence.*** - Data layer with EF Core, PostgreSQL, and repository pattern
- **FMBot.Domain** - Shared domain models and business logic
- **FMBot.LastFM** - Last.fm API integration and music data services
- **FMBot.Images** - Image generation using Puppeteer and SkiaSharp
- **FMBot.AppleMusic** - Apple Music API integration
- **FMBot.Discogs** - Discogs API integration for record collections
- **FMBot.Youtube** - YouTube integration for video content
- **FMBot.Tests** - NUnit test suite

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
- **Extensive migration history** (100+ migrations)
- **Key entities**: Users, Guilds, Artists, Albums, Tracks, UserPlays, UserCrowns
- **PostgreSQL extensions**: citext, pg_trgm for text search

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
- **C# 9.0+ features** with nullable reference types enabled
- **NetCord** framework for Discord API interactions (migrated from Discord.Net)
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
- **NUnit** testing framework with Moq for mocking
- Test files organized in `FMBot.Tests/` project
- Focus on service layer and business logic testing
- Minimal integration tests due to external API dependencies

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

## Localization

User-facing strings are translated via `LocalizationService` + JSON locale files managed through Weblate.

### Key Files
- **Locale files**: `src/FMBot.Bot/Resources/Locales/{locale}.json` — nested JSON (Weblate "Nested JSON" format)
- **`LocalizationService`**: `src/FMBot.Bot/Services/LocalizationService.cs` — singleton, loads all locale JSONs at startup, flattens to dot-notation keys
- **`LocaleAccessor`**: `src/FMBot.Bot/Services/LocaleAccessor.cs` — lightweight per-request accessor returned by `LocalizationService.For(locale)`

### Supported Languages
`en` (source), `pt-BR`, `es-ES`, `hi`, `de`, `pl`, `nl`, `fr`, `it`, `tr`, `sv-SE`

### How to Use in Builders
```csharp
var t = this._localizationService.For(context.Locale);

// Simple lookup (falls back to English, then returns key itself)
var title = t["guild_settings.title"];

// With named placeholders
var text = t.Get("guild_settings.users_blocked_from_wk", ("count", blockedCount.ToString()));
```

### Adding New Translatable Strings
1. Add the key + English value to `en.json` under the appropriate category
2. Add the same key with an empty `""` value to all other locale files
3. Use `t["category.key"]` or `t.Get("category.key", ...)` in the builder code
4. Weblate will pick up new keys automatically on next sync

### JSON Structure
Locale files use **nested JSON** (categories as objects), which gets flattened to dot-notation at load time:
```json
{
  "category_name": {
    "key": "English text with {placeholder}"
  }
}
```
Accessed in code as `t["category_name.key"]`.

### Do NOT Translate
Command names, "WhoKnows", "Crown/crowns", "Scrobble/scrobbles", "Supporter", "Top" — these stay in English in all locales.

### Guild Locale
- Stored as `PreferredLocale` on the `Guild` entity (nullable text column, defaults to `"en"`)
- Accessible via `GuildService.GetGuildLocaleAsync(discordGuildId)`
- Set via `GuildService.SetGuildLocaleAsync(discordGuild, locale)`
- Passed through `ContextModel.Locale` to builders