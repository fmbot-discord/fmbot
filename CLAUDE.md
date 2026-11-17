# CLAUDE.md

Guidance for Claude Code when working in this repository.

.fmbot is a Discord bot for Last.fm music statistics, built on .NET 11 and NetCord. It runs sharded across several instances against a database with billions of rows, so most design choices here come from scale: cached reads, Dapper for hot queries, and responses that reach Discord within its 3-second interaction window.

This repo is one half of a workspace. The bot needs the private web backend (`../fmbot-web-backend`) at runtime, and `src/FMBot.Discord.sln` includes its projects by relative path. If `../CLAUDE.md` exists, it covers the workspace: how the two repos connect, the database rules and table sizes, and the working agreements. Without the web backend checkout, build `src/FMBot.Bot/FMBot.Bot.csproj` instead of the solution.

## Commands

```bash
dotnet build ./src/FMBot.Discord.sln                      # everything (Debug)
dotnet build ./src/FMBot.Bot/FMBot.Bot.csproj             # just the bot
dotnet test ./src/FMBot.Tests/FMBot.Tests.csproj          # NUnit 4
dotnet run --project ./src/FMBot.Bot/FMBot.Bot.csproj     # reads src/FMBot.Bot/configs/config.json
docker-compose -f docker/docker-compose-local.yml up
```

`configs/config.json` is untracked; the bot writes a template on first run. Don't create EF migrations without being asked. When asked, use the EF project as its own startup project (it has a design-time factory):

```bash
dotnet ef migrations add MigrationName --project ./src/FMBot.Persistence.EntityFrameWork --startup-project ./src/FMBot.Persistence.EntityFrameWork
```

## Projects

| Project | What it holds |
|---------|---------------|
| `FMBot.Bot` | The bot: handlers, commands, builders, services, locale files. Entry points `Program.cs`, `Startup.cs` (DI and gRPC clients) |
| `FMBot.Core` | Logic shared with the web backend: `TimePeriodParser`, `PeriodAliases`, `UpdateService`, `MetadataFilter`, WhoKnows filters, chart rendering (`Charts/`) |
| `FMBot.Persistence` | Dapper repositories (`Repositories/`), including `SearchSql` and `PlayRepository` |
| `FMBot.Persistence.Domain` | Entity classes (`Models/`), the source of truth for columns |
| `FMBot.Persistence.EntityFrameWork` | `FMBotDbContext`, migrations, model snapshot |
| `FMBot.Domain` | Enums, constants, shared models, `PublicProperties` |
| `FMBot.LastFM` / `FMBot.LastFM.Domain` | Last.fm API client and models |
| `FMBot.Images` | Image generation: Puppeteer + Chrome for HTML pages (`Pages/`), SkiaSharp for graphs |
| `FMBot.AppleMusic`, `FMBot.Discogs` | API clients |
| `FMBot.Subscriptions` | Supporter logic (Stripe, Discord entitlements, legacy OpenCollective) |
| `FMBot.Tests` | NUnit tests |
| `src/Protos/` | gRPC contract with the web backend. Proto files only, no project. Edit protos here, never in the web backend's copies |

`src/FMBot.Youtube` and `src/Shared.Domain` are leftovers that are not in the solution. Ignore them.

Migrations were reset in March 2026 (`InitialMigration`, 2026-03-14). Older history is not in the repo.

## Inside FMBot.Bot

| Folder | Role |
|--------|------|
| `Handlers/` | `CommandHandler` (text) and `InteractionHandler` (slash, components, modals, autocomplete) route to the modules below |
| `SlashCommands/` | `*SlashCommands.cs`, one module per domain |
| `TextCommands/` | `*Commands.cs`, with `LastFM/` and `Guild/` subfolders |
| `Interactions/` | `*Interactions.cs`: button, select menu and modal handlers. Their custom ids are constants in `Resources/InteractionConstants.cs` |
| `Builders/` | `*Builders.cs`: take a `ContextModel`, call services, return a `ResponseModel`. All response construction lives here so text, slash and component paths share it |
| `Services/` | Business logic and data access orchestration. `TimerService` holds the Hangfire jobs |
| `AutoCompleteHandlers/` | Slash autocomplete providers |
| `Attributes/` | Command metadata and preconditions (`[UsernameSetRequired]`, `[RequiresIndex]`, `[GuildOnly]`, `[SupporterExclusive]`, `[CommandCategories]`, `[Options]`, `[Examples]`, `[ExcludeFromHelp]`, ...) |
| `Models/` | `ContextModel`, `ResponseModel`, `Localizer`, per-domain DTOs |
| `Resources/` | Locale JSON, slash description translations, `SlashCommandCanonicalNames.cs`, `FlowCommands.cs`, `DiscordConstants.cs` |

Most features exist as both a text command and a slash command. When you change one, check the other and the component handlers that re-render the same builder.

## Anatomy of a command

Slash command (see `ArtistSlashCommands.WhoKnowsAsync`):

```csharp
this.Context.DeferInBackground();
var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
try
{
    var response = await artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, contextUser), ...);
    await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
    await this.Context.LogCommandUsedAsync(response, userService);
}
catch (Exception e)
{
    await this.Context.HandleCommandException(e, userService);
}
```

Text commands follow the same shape with `TriggerTypingAsync`, `prefixService.GetPrefix(...)`, `new ContextModel(this.Context, prfx, contextUser)` and `this.Context.SendResponse(...)`. Text options are parsed out of the remainder string by `SettingService` helpers.

`ResponseModel` carries the result in the shape named by its `ResponseType`: `Text`, `Embed`, `Paginator`, `ImageWithEmbed`, `ImageOnly`, `SupporterRequired` or `ComponentsV2` (built in `ComponentsContainer` / `TopLevelComponents`). `CommandResponse` on it is what gets logged, so set it when the outcome isn't `Ok` (`NotFound`, `UsernameNotSet`, ...).

Rules that are easy to get wrong:

- **Defer in the background.** `DeferInBackground()` / `DeferUpdateInBackground()` start the defer without awaiting it, and `SendResponse`, `SendFollowUpResponse`, `HandleCommandException`, `UpdateInteractionEmbed`, `ModifyComponents`, `ModifyMessage` and `DisableButtonsAndMenus` wait for it before they touch Discord. Respond only through those helpers. A raw `ModifyResponseAsync` or `SendFollowupMessageAsync` after a background defer needs `await this.Context.WaitForPendingDefer()` first. Some flows (index, import, modals that edit mid-flow) still await their defer deliberately; don't convert those without tracing them.
- **Anything that does database, Last.fm, Stripe or gRPC work must defer first** and sit inside the try/catch. An uncaught exception in a component handler gives the user no reply at all, only "interaction failed" or buttons left disabled.
- **Component handlers** that replace a message: `DeferUpdateInBackground()`, then `var disable = this.Context.DisableButtonsAndMenus(...).ObserveFaults()`, and `await disable` before every final edit or followup so the disable edit lands first. `GameInteractions` (jumble play again) is the reference.
- **Custom ids bind positionally on `:`**, and the last handler parameter receives the entire remainder, colons included. Too few segments fail before optional parameters are considered. So an id built with a trailing `:` needs a matching optional last parameter, and adding an optional last parameter to a live handler means every id must send the (possibly empty) segment. Messages already sent keep their old ids. `InteractionHandler` logs a `CustomIdMismatch` warning for shape mismatches at runtime.
- **Paginators** (Fergun.Interactive.NetCord): before an in-place edit replaces a message that has a live paginator, call `CancelPaginator(messageId)`, or the old paginator's timeout re-renders its stale page over the new content. A paginator sent with `InteractionCallbackType.DeferredModifyMessage` edits the original response; `DeferredMessage` sends a new message.
- **Components V2 attachments**: file names are sanitized at send time (dots in the base name become `_`), after the builder already wrote `attachment://name` into the gallery. Keep attachment base names free of dots, spaces and quotes.
- **Guild-only slash commands** declare it on the attribute (`Contexts = [InteractionContextType.Guild]`, `IntegrationTypes = [ApplicationIntegrationType.GuildInstall]`), not with the runtime `[GuildOnly]` check that text commands use.

## Adding or changing a command

1. Put the logic in a builder and call it from both the slash and the text command. Register new services in `Startup.cs`.
2. **Slash command budget: 98 of Discord's 100 top-level slots are used.** Prefer a subcommand or a new option over a new top-level command. Never register an alias as a second command. A rename gets a new command id and breaks existing `</command:id>` mentions.
3. **Help catalog.** `HelpService` builds it from text command attributes (`[CommandCategories]`, minus `[ExcludeFromHelp]`) plus the reflected slash commands, matched by name. A slash command whose name differs from its text command is mapped in `SlashToTextOverrides`. One with no text counterpart needs a `SlashOnlyCategories` entry, or `HelpServiceTests` fails.
4. **Slash descriptions are translated.** Changing a `[SlashCommand]` or `[SlashCommandParameter]` description, or adding or removing a parameter, means regenerating the English file and updating all 11 translated files in the same change (see Localization).
5. **Usage logging.** Every command ends with `LogCommandUsedAsync`, which writes `user_interactions`. `command_name` there is read by rate limits, recap stats and hint tracking, so never remap it: slash logs the slash name and text logs the first alias, and `Resources/SlashCommandCanonicalNames.cs` pins the two together after a rename. A component that runs another command's builder is registered in `Resources/FlowCommands.cs`.
6. The public docs at fm.bot (`../docs` in the workspace) list every command with its options and examples. Mention it when a command's name, options or behaviour changed so the page can be updated.

## Conventions

- **NetCord**, not Discord.Net. When unsure about a NetCord API, read the source at `P:\NetCord` (Windows) / `/Users/thom/projects/NetCord` (macOS) or its docs rather than guessing from Discord.Net habits.
- `LangVersion` is `latest` in the bot. Nullable reference types are **off** in every project except `FMBot.Tests`, so `?` annotations are informational. `userSettings` and similar lookups can be null: a user may not have linked Last.fm.
- EF Core for writes and complex queries, Dapper for hot reads. Database columns are `snake_case`. Before writing SQL, read the schema reference named in `../CLAUDE.md` if it is available; `user_plays` has billions of rows and every query on it must filter by `user_id`.
- Last.fm rate-limits hard. Cache, and never fan out requests for many users at once.
- Guild-specific features need the guild id from context. Commands also run in DMs and as a user-installed app, where there is no guild.
- Match the surrounding code: low comment density, tuning numbers written inline where they are used.
- Structured logging through Serilog (Seq sink). Background jobs are Hangfire, scheduled in `TimerService`.

## Tests

NUnit 4, no mocking library. Tests are pure-function or file-based (`LocalizationTests`, `HelpServiceTests`, parsing and ranking tests). The default is no new test. Add one for a bug that was actually fixed, for parsing, math or ranking logic with non-obvious edge cases, or for a cross-file invariant nothing else enforces. Don't add tests that restate the implementation or only check your own change, and don't add test-only seams to production code. Extend an existing test file rather than creating one.

## Localization

User-facing strings are localized through JSON files managed in Weblate, with 12 locales. The full rules (code API, key and plural conventions, protected terms, never-localize list, translation style) are in the `localization` skill at `.claude/skills/localization/SKILL.md`. Read it before adding or migrating user-facing strings, editing locale files, writing translations, or changing any slash command or parameter description. Never skip these:

- A new `en.json` key must be seeded into all 11 other locale files in the same change (`EveryEnglishKeyExistsInEveryLocale` fails otherwise)
- Editing a `[SlashCommand]`/`[SlashCommandParameter]` description or adding/removing a parameter means updating all 11 translated `SlashCommandLocalizations` files in the same change. No test catches drift there. Never add `name` keys
- Full-sentence keys only. Never concatenate translated fragments into a sentence
- After touching locale files or adding `Localize` calls, run `dotnet test ./src/FMBot.Tests/FMBot.Tests.csproj`

## Branches

`main` is production, `dev` is active development and the beta bot. PRs go against `dev`.
