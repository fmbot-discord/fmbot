using PuppeteerSharp;

namespace FMBot.Images.Generators;

public class PuppeteerService
{
    public static async Task<Stream> GetPage()
    {
        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions()
        {
            Headless = true
        });

        await using var page = await browser.NewPageAsync();

        await page.GoToAsync("https://www.hardkoded.com");

        return await page.ScreenshotStreamAsync();
    }
}
