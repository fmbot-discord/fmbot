using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;
using Serilog;
using SkiaSharp;

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
        return lines.LastOrDefault(line =>
            line.Trim().StartsWith("https://") && line.EndsWith(".m3u8") && line.Contains("960x960"));
    }

    private static string ModifyUrl(string url)
    {
        return url.Replace(".m3u8", "-.mp4");
    }

    public static async Task<Stream> ConvertM3U8ToGifAsync(string m3u8Url)
    {
        var gifStream = new MemoryStream();

        var ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-i \"{m3u8Url}\" -map 0:6 -vf \"fps=9,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -t 12 -f gif pipe:1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            ffmpegProcess.Start();

            await ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(gifStream);

            await ffmpegProcess.WaitForExitAsync();

            if (ffmpegProcess.ExitCode != 0)
            {
                throw new Exception(
                    $"FFmpeg failed with exit code: {ffmpegProcess.ExitCode}");
            }

            gifStream.Position = 0;
            return gifStream;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during M3U8 to GIF conversion");
            throw;
        }
        finally
        {
            if (!ffmpegProcess.HasExited)
            {
                ffmpegProcess.Kill();
            }

            ffmpegProcess.Dispose();
        }
    }
}
