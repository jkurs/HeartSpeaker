using Microsoft.Playwright;

namespace HeartSpeak.Providers;

public class PulsoidProvider : IHeartRateProvider
{
    private readonly IElementHandle _heartRateElement;
    private int _lastHeartRate = 0;

    private PulsoidProvider(IElementHandle heartRateElement)
    {
        _heartRateElement = heartRateElement;
    }

    public static async Task<PulsoidProvider?> TryCreateAsync()
    {
        IPlaywright playwright = null;
        IBrowser browser = null;

        try
        {
            Console.WriteLine("Checking for dependencies...");
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            Console.WriteLine("Dependencies installed!");
        }
        catch (PlaywrightException)
        {
            Console.WriteLine("Browser launch failed — Playwright browser binaries may not be installed.");
            Console.Write("Would you like to install them now? (Y/N): ");
            string input = Console.ReadLine()?.Trim().ToLower();

            if (input == "y")
            {
                Console.WriteLine("Installing Playwright browsers...");
                var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });

                if (exitCode != 0)
                {
                    Console.WriteLine("Installation failed. Exiting...");
                    return null;
                }

                Console.WriteLine("Installation complete. Relaunching browser...");
                playwright = await Playwright.CreateAsync();
                browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
                Console.Clear();
            }
            else
            {
                Console.WriteLine("Cannot proceed without browser binaries. Exiting...");
                return null;
            }
        }

        string historyPath = "overlay_history.txt";
        List<string> savedUrls = new();
        string url = "";

        if (File.Exists(historyPath))
        {
            savedUrls = File.ReadAllLines(historyPath).Where(line => line.StartsWith("https://")).Distinct().ToList();
        }

        if (savedUrls.Any())
        {
            Console.WriteLine("Select a previously used Pulsoid overlay URL or enter a new one:");
            for (int i = 0; i < savedUrls.Count; i++)
            {
                Console.WriteLine($"[{i + 1}] {savedUrls[i]}");
            }
            Console.Write("Enter number or paste new URL: ");
            string input = Console.ReadLine()?.Trim();
            Console.Clear();
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= savedUrls.Count)
            {
                url = savedUrls[choice - 1];
            }
            else
            {
                url = input;
                if (!savedUrls.Contains(url) && url.StartsWith("https://"))
                    File.AppendAllLines(historyPath, new[] { url });
            }
        }
        else
        {
            Console.Write("Enter your Pulsoid overlay URL: ");
            url = Console.ReadLine()?.Trim();
            Console.Clear();
            if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("https://"))
                File.WriteAllLines(historyPath, new[] { url });
        }

        if (!url.StartsWith("https://"))
        {
            Console.WriteLine("Error: URL must begin with https://");
            return null;
        }

        var page = await browser.NewPageAsync();
        await page.GotoAsync(url);

        Console.WriteLine("Waiting for heart rate data...");
        IElementHandle heartRateElement = null;

        while (heartRateElement == null)
        {
            try
            {
                heartRateElement = await page.QuerySelectorAsync("#heartRate");

                if (heartRateElement == null)
                {
                    Console.WriteLine("Heart rate element not found yet. Retrying...");
                    await Task.Delay(5000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                await Task.Delay(5000);
            }
        }

        return new PulsoidProvider(heartRateElement);
    }

    public async Task<int?> GetCurrentHeartRateAsync()
    {
        var bpmText = await _heartRateElement.InnerTextAsync();
        if (int.TryParse(bpmText, out int currentHeartRate))
        {
            return currentHeartRate;
        }

        return null;
    }
}