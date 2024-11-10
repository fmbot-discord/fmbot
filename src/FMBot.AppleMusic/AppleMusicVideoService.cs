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

    public static async Task<Stream> ConvertM3U8ToWebPAsync(string m3u8Url)
    {
        var webpStream = new MemoryStream();

        var ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hwaccel auto " +
                            $"-i \"{m3u8Url}\" " +
                            "-vf \"fps=fps=20\" " +
                            "-lossless 0 " +
                            "-compression_level 5 " +
                            "-loop 1 " +
                            "-f webp pipe:1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            ffmpegProcess.Start();

            var outputTask = ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(webpStream);

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

            webpStream.Position = 0;
            return webpStream;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during M3U8 to WebP conversion");
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
