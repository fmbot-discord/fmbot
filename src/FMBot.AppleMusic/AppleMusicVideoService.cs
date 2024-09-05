using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
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
        return lines.LastOrDefault(line => line.Trim().StartsWith("https://") && line.EndsWith(".m3u8") && line.Contains("960x960"));
    }

    private static string ModifyUrl(string url)
    {
        return url.Replace(".m3u8", "-.mp4");
    }

public static async Task<Stream> ConvertMp4ToGifAsync(Stream mp4Stream)
{
    var gifStream = new MemoryStream();

    var ffmpegProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-i pipe:0 -vf \"fps=10,scale=320:-1:flags=lanczos\" -t 15 -f gif pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    ffmpegProcess.Start();

    var inputTask = Task.Run(async () =>
    {
        try
        {
            await mp4Stream.CopyToAsync(ffmpegProcess.StandardInput.BaseStream);
            await ffmpegProcess.StandardInput.FlushAsync();
            ffmpegProcess.StandardInput.Close();
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error writing to FFmpeg stdin: {ex.Message}");
        }
    });

    var outputTask = Task.Run(async () =>
    {
        try
        {
            await ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(gifStream);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error reading from FFmpeg stdout: {ex.Message}");
        }
    });

    await Task.WhenAll(inputTask, outputTask);
    await ffmpegProcess.WaitForExitAsync();

    if (ffmpegProcess.ExitCode != 0)
    {
        throw new Exception($"FFmpeg failed with exit code: {ffmpegProcess.ExitCode}");
    }

    gifStream.Position = 0;

    return gifStream;
}
}
