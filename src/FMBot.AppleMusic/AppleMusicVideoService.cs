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

    public async Task<string> GetVideoUrlFromM3U8(string m3u8Url)
    {
        var content = await _httpClient.GetStringAsync(m3u8Url);
        var lines = content.Split('\n');
        return lines.LastOrDefault(line =>
            line.Trim().StartsWith("https://") && line.EndsWith(".m3u8") && line.Contains("486x486"));
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
                    $"-hwaccel auto -i \"{m3u8Url}\" -vf \"fps=10,format=rgb24,colorspace=bt709:iall=bt601-6-625:fast=1,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle\" -t 15 -f gif pipe:1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            ffmpegProcess.Start();

            var outputTask = ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(gifStream);

            var errorBuilder = new StringBuilder();
            var errorTask = Task.Run(async () =>
            {
                while (await ffmpegProcess.StandardError.ReadLineAsync() is { } line)
                {
                    errorBuilder.AppendLine(line);
                    if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            });

            await Task.WhenAny(outputTask, errorTask);

            await ffmpegProcess.WaitForExitAsync();

            if (errorBuilder.Length > 0)
            {
                Log.Debug(
                    $"FFmpeg output: {errorBuilder}");
            }

            if (ffmpegProcess.ExitCode != 0)
            {
                Log.Warning(
                    $"FFmpeg failed with exit code: {ffmpegProcess.ExitCode}. Error output: {errorBuilder}");
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
