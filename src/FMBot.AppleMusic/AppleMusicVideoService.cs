namespace FMBot.AppleMusic;

public class AppleMusicVideoService
{
    private readonly HttpClient _httpClient;

    public AppleMusicVideoService(HttpClient httpClient)
    {
        this._httpClient = httpClient;
    }

    public async Task<string> GetModifiedVideoUrl(string m3u8Url)
    {
        var lastUrl = await GetLastUrlFromM3U8(m3u8Url);
        return ModifyUrl(lastUrl);
    }

    private async Task<string> GetLastUrlFromM3U8(string m3u8Url)
    {
        var content = await _httpClient.GetStringAsync(m3u8Url);
        var lines = content.Split('\n');
        return lines.LastOrDefault(line => line.Trim().StartsWith("https://") && line.EndsWith(".m3u8"));
    }

    private static string ModifyUrl(string url)
    {
        return url.Replace(".m3u8", "-.mp4");
    }
}
