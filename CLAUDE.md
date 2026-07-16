# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See also `../CLAUDE.md` for workspace-level context covering both repos, the full database schema, and how the bot and web backend connect.

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
- **FMBot.Persistence.*** - Data layer with EF Core, PostgreSQL, and repository pattern
- **FMBot.Domain** - Shared domain models and business logic
- **FMBot.LastFM** - Last.fm API integration and music data services
- **FMBot.Images** - Image generation using Puppeteer and SkiaSharp
- **FMBot.AppleMusic** - Apple Music API integration
- **FMBot.Discogs** - Discogs API integration for record collections
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
- **C# 14+ features** with nullable reference types enabled
- **NetCord** framework for Discord API interactions (migrated from Discord.Net). When unsure about NetCord APIs or types, you can look up documentation, search online, or read the local source code at `P:\NetCord`
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

## Localization

User-facing strings are localized via JSON files, managed in Weblate. Weblate/glossary/rollout details live in `../LOCALIZATION_NOTES.md`. When adding or migrating user-facing strings, follow ALL of these rules:

### Code API
- In builders (with `ContextModel context`): `context.Localize("key", ("name", value))` and `context.LocalizeCount("key", count, ("name", value))`. All interpolation arg values are **strings**, pre-formatted in C#: numbers via `.Format(context.NumberFormat)`, timestamps pre-rendered as `<t:unix:D>`, names already `Sanitize`d.
- Without a ContextModel (handlers, static helpers, background sends): `Localizer.ForGuild(guildId)` — guild id from `context.Guild?.Id` (text) or `context.Interaction.GuildId` (interactions).
- Helpers on `Localizer`: `TimeAgo(dateTime)`, `LongListeningTime(timeSpan)`, `Ordinal(n)` (returns "5th" complete), `FormatMonthDay(date)`/`FormatMonthDayYear(date)` (culture-aware month names AND day/month order), `PeriodLabel(timeSettings)` (localized label for `{{period}}` args — never feed `TimeSettingsModel.Description` into a localized key). Use these instead of `StringExtensions.GetTimeAgo`/`GetLongListeningTimeString`/`GetAmountEnd` — the English statics remain only for locale-less callers.
- Time periods: pass `language:` to `SettingService.GetTimePeriod` (from `context.Localizer.Language` or `LocalizationService.GetLanguage(...)`) so users can also TYPE periods in the guild's language. Input aliases are code-owned per language in `Models/PeriodAliases.cs` (lowercase, curated against search-text collisions — parsing must never depend on Weblate JSON); English tokens always keep working. `Contains` matches on space-padded boundaries, so an alias may be multiple words (`"3 aylık"`, `"senaste veckan"`); within a category list the longest form goes FIRST, because `ContainsAndRemove` strips values in array order and a short form matching first leaves debris in the search value. `Merge` puts localized aliases before the English tokens for the same reason. Categories are matched most-specific-first (quarterly/halfYearly before monthly, twoYear before yearly) so a label containing another period's word still resolves correctly. Localized month-name input comes from CLDR in `GetMonth`, full names only (abbreviations are uncurated and collide with search text; English `jan`–`dec` work in every guild), minus `PeriodTokens.ExcludedMonths` for names that are common search terms (`mars` in fr/sv-SE would eat "Bruno Mars"). Every `shared.period.*` display label MUST parse back to its own period — `PeriodLabelsRoundTripAsInput` enforces this, so a new or reworded label needs a matching alias. `TimeSettingsModel.Description` stays English — it round-trips through custom_ids, cache keys and URL/API parameters.
- Locale resolution: explicit guild setting → `UseDiscordGuildLocale` config flag (Discord guild locale) → English. DMs are always English. Any cache that stores rendered text MUST include the locale in its key.

### Key + file conventions
- Files: `src/FMBot.Bot/Resources/Locales/{code}.json` — flat i18next v4 JSON. `en.json` is the source of truth and code-owned (changes via PRs only); other languages are Weblate-owned. Locale codes: en, pt-BR, es-ES, hi, de, pl, nl, fr, it, tr, sv-SE, id (Discord-supported locales only, ever).
- Keys: flat dotted camelCase, namespaced per domain (`shared.*`, `errors.*`, `fm.*`, `artist.whoknows.*`, `footer.*`, ...). Full-sentence keys — NEVER concatenate translated fragments into a sentence (word order differs per language). Conditional sentence variants get their own keys (`titleSelf`/`titleOther`).
- Plurals: code references the base key via `LocalizeCount`; JSON carries `key_one`/`key_other` (other languages may add `_few`/`_many` per CLDR). `{{count}}` is reserved for the plural driver and auto-formatted with the user's NumberFormat. Reuse `shared.*` plural units (plays, scrobbles, listeners, ...) when the English matches exactly.
- The bold listener unit keys (`artist.listenersBold`, `track.boldListeners`, `album.listenersBold`) are context-locked fragments: they only ever render inside a "by {{listeners}}" sentence in their own domain, and translations depend on that governing preposition (German uses the dative "Hörern", Polish the genitive). Never render them standalone and never add a consumer with different framing — mint a new key instead.
- Markdown stays inside JSON values (`###`, `**`, `-#`, backticks, complete links `[label]({{url}})` so labels translate). Custom emote tags (`<:name:id>`) must NEVER appear in JSON — concatenate them in C# or pass as an `{{emote}}` arg.
- When migrating existing hardcoded strings: rendered English output must stay byte-for-byte identical (grammar fixes like "1 plays"→"1 play" only when explicitly flagged in the report).

### Protected terms (never translated, stay verbatim in every language)
Command **names** (`fm`, `whoknows`, ... — the generic noun "command" in prose DOES translate), WhoKnows, GlobalWhoKnows, scrobble/scrobbles/scrobbling, plays (loanword), billboard/bb (literal input token, matched in `SettingService`), Top (as feature prefix), .fmbot, Last.fm, Spotify and other brand names, the .fmbot user types (supporter, owner, admin, contributor), Jumble. Protected loanwords may still take local casing/inflection where the file already does that (German capitalization/hyphen compounds, Polish case endings like `supporterem`, Turkish apostrophe suffixes like `supporter'ı`).

### Never localize
Log messages, exception text, SQL, custom ids, cache keys, admin/censor-only strings, featured descriptions (rendered once, broadcast to all guilds), user-authored template content, autocomplete option VALUES (labels may localize later; values round-trip into English parsing), `TimeSettingsModel.Description` and other stored/round-tripped period tokens (localized period display goes through `Localizer.PeriodLabel`; localized period INPUT is code-owned in `PeriodAliases.cs`, never JSON-driven — but each `shared.period.*` label must have an alias so it round-trips).

### Translation content style (for agents writing translations)
- Informal address (du/tu/je/jij), concise natural phrasing; preserve every `{{placeholder}}` exactly.
- In `` `{{value}}` label `` lines (`track.duration`, `track.keyBpm`, `track.danceableEnergetic`, `track.acousticInstrumental`, `track.speechfulLiveness`, `track.happy`), the backticked `{{...}}` is a pre-formatted value (usually a percentage) and the bare word next to it is a visible label that MUST translate. The English labels are adjectives (danceable, energetic, speechful) — keep them adjectives, not nouns.
- Dutch specifics (reference: `nl.json`): plays/scrobbles stay English; always "track"/"tracks", never "nummer" (de-word: "de track", "deze track", "die je zoekt"); luisteraars/artiesten/kronen; "Pagina" for page; "Aangevraagd door" for requested by; ordinals are `{{count}}e`; "uur" is uninflected in plural.
- German specifics (reference: `de.json`): "Befehl" for command; compounds with a protected English term or proper noun KEEP the hyphen (`Künstler-Plays`, `Mitglieder-Cache`, `Last.fm-Account`), pure-German compounds don't (`Künstlerinfos`); `{{date}}` needs a preposition ("entdeckt am {{date}}") since German has no bare adverbial date; generic masculine ("der Künstler", "der Nutzer") throughout.

### Slash command descriptions
`src/FMBot.Bot/Resources/SlashCommandLocalizations/{locale}.json` (NetCord nested schema: `commands` → `description`/`parameters`/`subcommands`). NEVER add `name` keys — command names stay identical in every locale, and the test suite fails if one appears. `en.json` there is generated from the `[SlashCommand]` attributes: regenerate with `FMBOT_REGEN_SLASH_LOCALIZATIONS=1 dotnet test --filter EnglishBaseFileMatchesSlashCommandAttributes`. Its `CopyToOutputDirectory=Never` csproj entry must stay (bare `en` is an invalid Discord locale and breaks command registration).

### Verification
After touching locale files or adding `Localize` calls, always run `dotnet test ./src/FMBot.Tests/FMBot.Tests.csproj` — `LocalizationTests` enforce key completeness (every referenced key exists in en.json, `_one`+`_other` for count keys), per-locale placeholder subsets, plural rules, and the no-`name`-keys guard.

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