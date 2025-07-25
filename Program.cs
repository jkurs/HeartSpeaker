using Microsoft.Playwright;
using System.Collections.Generic;
using System.Speech.Synthesis;

class Program
{
    static Random rand = new();
    static Dictionary<string, DateTime> zoneStartTimes = new();
    static DateTime lastDecreaseSpoken = DateTime.MinValue;
    static bool isSimpleMode = false;

    static async Task Main()
    {
        Console.WriteLine("Welcome to HeartSpeak!");

        Console.Write("Enable Simple Mode? (Y/N): ");
        isSimpleMode = Console.ReadLine()?.Trim().ToLower() == "y";
        Console.Clear();

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
                    return;
                }

                Console.WriteLine("Installation complete. Relaunching browser...");
                playwright = await Playwright.CreateAsync();
                browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
                Console.Clear();
            }
            else
            {
                Console.WriteLine("Cannot proceed without browser binaries. Exiting...");
                return;
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
            return;
        }

        Console.Write("Enter your maximum heart rate (e.g. 190): ");
        if (!int.TryParse(Console.ReadLine(), out int maxHeartRate))
        {
            Console.WriteLine("Invalid heart rate. Exiting...");
            return;
        }
        Console.Clear();
        var synth = new SpeechSynthesizer();
        synth.SetOutputToDefaultAudioDevice();
        synth.Volume = 100;

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

        Console.WriteLine("Heart rate data detected. Starting session.");
        Console.Clear();
        int lastBpm = -1;
        int sessionMax = -1;
        int sessionMin = -1;
        string previousZone = "";
        zoneStartTimes["Resting"] = DateTime.UtcNow;
        zoneStartTimes["Zone 1"] = 0;
        zoneStartTimes["Zone 2"] = 0;
        zoneStartTimes["Zone 3"] = 0;
        zoneStartTimes["Zone 4"] = 0;
        zoneStartTimes["Zone 5"] = 0;

        while (true)
        {
            try
            {
                string bpmText = await page.InnerTextAsync("#heartRate");

                if (int.TryParse(bpmText, out int currentBpm))
                {                    
                    sessionMax = sessionMax == -1 ? currentBpm : Math.Max(sessionMax, currentBpm);
                    sessionMin = sessionMin == -1 ? currentBpm : Math.Min(sessionMin, currentBpm);

                    string currentZone = GetZone(currentBpm, maxHeartRate);
                    int delta = lastBpm == -1 ? 0 : Math.Abs(currentBpm - lastBpm);
                    int threshold = GetDynamicThreshold(currentBpm, maxHeartRate);
                    bool bpmChangedThisTick = false;
                    bool zoneChanged = currentZone != previousZone;

                    string reflection = "";
                    string insight = "";

                    if (lastBpm == -1)
                    {
                        currentZone = GetZone(currentBpm, maxHeartRate);
                        previousZone = GetZone(currentBpm, maxHeartRate);
                    }

                    if (zoneChanged && !string.IsNullOrEmpty(previousZone) && zoneStartTimes.ContainsKey(previousZone) || lastBpm == -1)
                    {
                        TimeSpan duration = DateTime.UtcNow - zoneStartTimes[previousZone];
                        if (duration.TotalMinutes >= 2)
                        {
                            reflection = BuildNarration(previousZone, "reflection", currentBpm, duration, sessionMax, sessionMin);
                        }
                        zoneStartTimes[currentZone] = DateTime.UtcNow;
                        insight = BuildNarration(currentZone, "insight", currentBpm, null, sessionMax, sessionMin);
                    }

                    if (lastBpm == -1 || delta >= threshold)
                    {
                        bpmChangedThisTick = true;

                        string changeDirection = lastBpm == -1 ? "start"
                        : currentBpm > lastBpm ? "increase"
                        : "decrease";

                        TimeSpan dynamicCooldown = GetZoneCooldown(currentZone);
                        bool isAllowedToSpeak = changeDirection != "decrease"
                            || DateTime.UtcNow - lastDecreaseSpoken > dynamicCooldown;

                        if (isAllowedToSpeak)
                        {
                            string changeMessage = BuildNarration(currentZone, "change", currentBpm, null, sessionMax, sessionMin, changeDirection);
                            string combined = $"{changeMessage}";

                            if (!string.IsNullOrWhiteSpace(reflection)) combined += $" {reflection}";
                            else if (!string.IsNullOrWhiteSpace(insight)) combined += $" {insight}";

                            synth.Speak(combined);
                            Console.WriteLine(combined);

                            if (changeDirection == "decrease" && dynamicCooldown > TimeSpan.Zero)
                                lastDecreaseSpoken = DateTime.UtcNow;

                            lastBpm = currentBpm;
                        }
                    }
                    else if (zoneChanged)
                    {
                        string solo = !string.IsNullOrWhiteSpace(reflection) ? reflection : insight;
                        if (!string.IsNullOrWhiteSpace(solo))
                        {
                            synth.Speak(solo);
                            Console.WriteLine(solo);
                        }
                    }

                    previousZone = currentZone;
                }

                await Task.Delay(3000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                await Task.Delay(5000);
            }
        }
    }

    static string[] adjectivesLow = {
    "peaceful", "gentle", "restorative", "calm", "quiet", "relaxed", "tranquil",
    "soothing", "mellow", "light", "breezy", "soft", "reassuring", "cozy",
    "serene", "easygoing", "untroubled", "low-key", "rejuvenating",
    "composed", "grounded", "content", "still", "unhurried", "centered",
    "placid", "meditative", "languid", "cool", "fresh", "easy", "floaty"
};

    static string[] adjectivesMid = {
    "focused", "steady", "persistent", "active", "motivated", "fluid", "resilient",
    "determined", "stable", "attentive", "driven", "smooth", "disciplined",
    "engaged", "controlled", "reliable", "composed", "in rhythm",
    "alert", "ready", "coordinated", "confident", "functional", "collected",
    "centered", "intentional", "calculated", "on pace", "sharpened", "present"
};

    static string[] adjectivesHigh = {
    "bold", "intense", "explosive", "electric", "relentless", "unstoppable", "vigorous",
    "charged", "fiery", "aggressive", "wild", "ferocious", "powerful",
    "energetic", "dominant", "overdrive", "fearless", "adrenalized", "raw",
    "extreme", "fast", "full-throttle", "pulsing", "high-voltage", "raging",
    "amped", "cranked", "revved", "volatile", "storming", "hardcore", "surging"
};

    static string[] adverbs = {
    "smoothly", "deliberately", "rapidly", "quietly", "noticeably", "strongly", "suddenly",
    "effortlessly", "intensely", "gradually", "swiftly", "gently", "sharply", "lightly",
    "urgently", "stealthily", "confidently", "boldly", "efficiently",
    "predictably", "forcefully", "clearly", "markedly", "sensibly", "naturally",
    "steadily", "briskly", "softly", "assertively", "purposefully", "intuitively"
};

    static string[] pronouns = {
    "Your heart", "This rhythm", "That BPM", "Your pulse", "The flow",
    "The tempo", "Your energy", "This cadence", "The beat", "Pulse reading", "Your engine",
    "This heartbeat", "Your system", "This wave", "Your signal",
    "Your cardio", "Your drive", "The momentum", "The zone", "Your core"
};

    static string[] verbsUp = {
    "is rising", "is surging", "just accelerated", "is climbing", "is charging upward",
    "just spiked", "picked up", "is ramping", "is rocketing", "moved upward",
    "is intensifying", "stepped up", "is accelerating", "leapt forward", "jumped in tempo",
    "got louder", "is revving", "amped up", "turned up", "burst forward", "is ascending"
};

    static string[] verbsDown = {
    "is falling", "just dropped", "is easing off", "is relaxing", "slowed gently",
    "declined softly", "settled down", "is cooling", "just softened", "is lowering",
    "dipped calmly", "backed off", "stepped back", "is de-escalating", "winding down",
    "toned down", "receded", "got quieter", "let go", "calmed", "is descending"
};

    static string[] verbsSteady = {
    "is cruising", "is holding steady", "is pacing smoothly", "is flowing consistently",
    "is sustaining rhythm", "is staying on track", "is gliding", "is performing smoothly",
    "is level", "is maintaining cadence", "is in control", "is moving reliably",
    "is operating smoothly", "is balanced", "is consistent",
    "is locked in", "is tuned", "is calibrated", "is maintaining stride", "is synced up"
};

    static string GetZone(int bpm, int max)
    {
        double intensity = (double)bpm / max;

        if (intensity < 0.5) return "Resting";
        if (intensity < 0.6) return "Zone 1";
        if (intensity < 0.7) return "Zone 2";
        if (intensity < 0.8) return "Zone 3";
        if (intensity < 0.9) return "Zone 4";
        return "Zone 5";
    }

    static int GetDynamicThreshold(int bpm, int max)
    {
        double intensity = (double)bpm / max;
        if (intensity < 0.5) return 30;
        if (intensity < 0.6) return 25;
        if (intensity < 0.7) return 20;
        if (intensity < 0.8) return 15;
        if (intensity < 0.9) return 5;
        return 2;
    }

    static string[] GetAdjectivePool(string zone) => zone switch
    {
        "Resting" => adjectivesLow,
        "Zone 1" => adjectivesLow,
        "Zone 2" => adjectivesMid,
        "Zone 3" => adjectivesMid,
        "Zone 4" => adjectivesHigh,
        "Zone 5" => adjectivesHigh,
        _ => adjectivesMid
    };

    static string[] GetVerbPool(string context) => context switch
    {
        "increase" => verbsUp,
        "decrease" => verbsDown,
        "start" => verbsSteady,
        "change" => verbsSteady,
        "insight" => verbsSteady,
        _ => verbsSteady
    };

    static string BuildNarration(string zone, string context, int bpm, TimeSpan? duration, int sessionMax, int sessionMin, string changeDirection = "")
    {
        if (isSimpleMode)
        {
            return context switch
            {
                "change" => GetChangeIntro(bpm, changeDirection),
                "insight" => $"Entered {zone}.",
                "reflection" => duration.HasValue
                    ? $"Time spent in {zone}: {(int)duration.Value.TotalMinutes} minutes."
                    : $"Exited {zone}.",

                _ => $"Heart rate is {bpm}. Current zone is {zone}."
            };
        }

        string pronoun = Pick(pronouns);
        string adjective = Pick(GetAdjectivePool(zone));
        string adverb = Pick(adverbs);
        string verb = Pick(GetVerbPool(context));
        string durationText = duration.HasValue ? $"{(int)duration.Value.TotalMinutes} minutes" : "";

        string[] templates = context switch
        {
            "change" => new[]
            {
                $"{GetChangeIntro(bpm, changeDirection)} {pronoun} {verb} {adverb}, feeling {adjective}.",
                $"{GetChangeIntro(bpm, changeDirection)} {pronoun} is {adjective} and {verb} and {adverb}.",
                $"{GetChangeIntro(bpm, changeDirection)} {pronoun} {verb}, energy feels {adjective}.",
                $"{GetChangeIntro(bpm, changeDirection)} {pronoun} {verb} and {adverb} — now {adjective}."
            },
            "insight" => new[]
            {
                $"Entering {zone} — tone is {adjective}.",
                $"You’ve shifted zones — {zone} feels {adjective}.",
                $"Now in {zone} — staying {adjective}."
            },
            "reflection" => new[]
            {
                $"{zone} lasted {durationText} — effort was {adjective}.",
                $"After {durationText} in {zone}, you’re feeling {adjective}.",
                $"{durationText} spent in {zone} — solid pacing."
            },
            _ => new[] { $"Heart rate is {bpm}. Currently in {zone} zone."
            }
         };

        return Pick(templates);
    }

    static string Pick(string[] options) => options[rand.Next(options.Length)];

    static string GetChangeIntro(int bpm, string direction)
    {
        return direction switch
        {
            "increase" => $"Heart rate has increased to {bpm}.",
            "decrease" => $"Heart rate has decreased to {bpm}.",
            _ => $"Heart rate is {bpm}."
        };
    }

    static TimeSpan GetZoneCooldown(string zone) => zone switch
    {
        "Resting" => TimeSpan.FromMinutes(2),
        "Zone 1" => TimeSpan.FromMinutes(1),
        "Zone 2" => TimeSpan.FromSeconds(45),
        "Zone 3" => TimeSpan.FromSeconds(30),
        "Zone 4" => TimeSpan.FromSeconds(15),
        "Zone 5" => TimeSpan.FromSeconds(3),
        _ => TimeSpan.Zero
    };
}
