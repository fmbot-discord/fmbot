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

### Database Architecture
- **PostgreSQL** with Entity Framework Core
- **Snake_case** naming convention
- **Extensive migration history** (100+ migrations)
- **Key entities**: Users, Guilds, Artists, Albums, Tracks, UserPlays, UserCrowns
- **PostgreSQL extensions**: citext, pg_trgm for text search

### External API Integrations
Primary services: Last.fm (core), Spotify (features), Apple Music (metadata), YouTube (videos), Discogs (collections), MusicBrainz (metadata), OpenAI (AI features), Genius (lyrics)

### Docker & Deployment
- **Multi-stage Dockerfile** with .NET 9.0 runtime
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
- **Discord.Net** framework for Discord API interactions
- **Async/await** patterns throughout
- **Structured logging** with Serilog
- **Extension methods** for common operations

### Testing
- **NUnit** testing framework with Moq for mocking
- Test files organized in `FMBot.Tests/` project
- Focus on service layer and business logic testing
- Minimal integration tests due to external API dependencies

## Key Technical Considerations

### Performance
- **Background jobs** with Hangfire for async processing
- **Caching strategies** for frequently accessed data
- **Database indexing** for optimal query performance
- **Sharding** for Discord bot scaling

### Security
- **API keys** managed through configuration
- **User privacy** controls and data protection
- **Rate limiting** for external API calls
- **Input validation** and sanitization

### Monitoring & Diagnostics
- **Structured logging** with Serilog to Seq
- **Prometheus metrics** for monitoring
- **Health checks** for application status
- **Diagnostic tools** for memory and performance analysis

## File Organization

Important directories:
- `src/FMBot.Bot/SlashCommands/` - Discord slash command implementations
- `src/FMBot.Bot/Services/` - Business logic services
- `src/FMBot.Bot/Builders/` - Response building logic
- `src/FMBot.Persistence.EntityFrameWork/Migrations/` - Database migrations
- `src/FMBot.Images/` - Image generation services
- `docker/` - Docker Compose configurations

When working with commands, check both `SlashCommands/` and `TextCommands/` directories as the bot supports both interaction types.