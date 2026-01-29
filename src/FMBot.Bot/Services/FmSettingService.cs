using System;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services;

public class FmSettingService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;

    public FmSettingService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
    }

    public async Task<UserFmSetting> GetOrCreateFmSetting(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var fmSetting = await db.UserFmSettings.FirstOrDefaultAsync(f => f.UserId == userId);

        if (fmSetting == null)
        {
            fmSetting = new UserFmSetting
            {
                UserId = userId,
                EmbedType = FmEmbedType.EmbedMini,
                FooterOptions = FmFooterOption.TotalScrobbles,
                Modified = DateTime.UtcNow
            };
            db.UserFmSettings.Add(fmSetting);
            await db.SaveChangesAsync();
        }

        return fmSetting;
    }

    public async Task SetEmbedType(User user, FmEmbedType embedType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var fmSetting = await GetOrCreateTracked(db, user.UserId);
        fmSetting.EmbedType = embedType;
        fmSetting.Modified = DateTime.UtcNow;
        db.Update(fmSetting);

        await db.SaveChangesAsync();
        RemoveUserFromCache(user);
    }

    public async Task SetAccentColor(User user, FmAccentColor color, string customHex = null)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var fmSetting = await GetOrCreateTracked(db, user.UserId);

        fmSetting.AccentColor = color;
        fmSetting.CustomColor = customHex;
        fmSetting.Modified = DateTime.UtcNow;
        db.Update(fmSetting);

        await db.SaveChangesAsync();
        RemoveUserFromCache(user);
    }

    public async Task SetTextType(User user, FmTextType textType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var fmSetting = await GetOrCreateTracked(db, user.UserId);

        fmSetting.SmallTextType = textType;
        fmSetting.Modified = DateTime.UtcNow;
        db.Update(fmSetting);

        await db.SaveChangesAsync();
        RemoveUserFromCache(user);
    }

    public async Task SetButtons(User user, FmButton buttons)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var fmSetting = await GetOrCreateTracked(db, user.UserId);

        fmSetting.Buttons = buttons;
        fmSetting.Modified = DateTime.UtcNow;
        db.Update(fmSetting);

        await db.SaveChangesAsync();
        RemoveUserFromCache(user);
    }

    public async Task SetPrivateButtonResponse(User user, bool? isPrivate)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var fmSetting = await GetOrCreateTracked(db, user.UserId);

        fmSetting.PrivateButtonResponse = isPrivate;
        fmSetting.Modified = DateTime.UtcNow;
        db.Update(fmSetting);

        await db.SaveChangesAsync();
        RemoveUserFromCache(user);
    }

    public async Task SetFooterOptions(User user, FmFooterOption footerOptions)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var fmSetting = await GetOrCreateTracked(db, user.UserId);
        fmSetting.FooterOptions = footerOptions;
        fmSetting.Modified = DateTime.UtcNow;
        db.Update(fmSetting);

        await db.SaveChangesAsync();
        RemoveUserFromCache(user);
    }

    private async Task<UserFmSetting> GetOrCreateTracked(FMBotDbContext db, int userId)
    {
        var fmSetting = await db.UserFmSettings.FirstOrDefaultAsync(f => f.UserId == userId);

        if (fmSetting == null)
        {
            fmSetting = new UserFmSetting
            {
                UserId = userId,
                EmbedType = FmEmbedType.EmbedMini,
                FooterOptions = FmFooterOption.TotalScrobbles,
                Modified = DateTime.UtcNow
            };
            db.UserFmSettings.Add(fmSetting);
            await db.SaveChangesAsync();
        }

        return fmSetting;
    }

    private void RemoveUserFromCache(User user)
    {
        this._cache.Remove(UserService.UserInternalIdCacheKey(user.UserId));
        this._cache.Remove(UserService.UserDiscordIdCacheKey(user.DiscordUserId));
        this._cache.Remove(UserService.UserLastFmCacheKey(user.UserNameLastFM));
    }
}
