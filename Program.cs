using Microsoft.Playwright;
using System.Collections.Generic;
using System.Speech.Synthesis;

class Program
{
    static Random rand = new();
    static Dictionary<string, DateTime> zoneStartTimes = new();

    static async Task Main()
    {
        Console.WriteLine("Welcome to HeartSpeak!");

        IPlaywright playwright = null;
        IBrowser browser = null;

        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
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
            }
            else
            {
                Console.WriteLine("Cannot proceed without browser binaries. Exiting...");
                return;
            }
        }

        Console.Write("Enter your Pulsoid overlay URL: ");
        string url = Console.ReadLine();

        Console.Write("Enter your maximum heart rate (e.g. 190): ");
        int maxHeartRate = int.Parse(Console.ReadLine());

        var synth = new System.Speech.Synthesis.SpeechSynthesizer();
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
                Console.WriteLine($"Error while checking for heart rate element: {ex.Message}");
                await Task.Delay(5000);
            }
        }

        Console.WriteLine("Heart rate data detected! -- continuing application as normal.");

        int lastBpm = -1;
        int sessionMax = -1;
        int sessionMin = -1;
        string previousZone = "";
        zoneStartTimes["Rest"] = DateTime.UtcNow;

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

                    if (lastBpm == -1 || delta >= threshold)
                    {
                        bpmChangedThisTick = true;

                        string changeType = lastBpm == -1 ? "start" :
                                            currentBpm > lastBpm ? "increase" : "decrease";

                        string message = BuildDynamicPhrase(changeType, currentZone, currentBpm);
                        synth.Speak(message);
                        Console.WriteLine($"{message}");
                        lastBpm = currentBpm;
                    }

                    if (currentZone != previousZone)
                    {
                        if (!bpmChangedThisTick)
                        {
                            DateTime now = DateTime.UtcNow;

                            if (!string.IsNullOrEmpty(previousZone) && zoneStartTimes.ContainsKey(previousZone))
                            {
                                TimeSpan duration = now - zoneStartTimes[previousZone];
                                string reflection = BuildZoneReflection(previousZone, duration);
                                if (!string.IsNullOrWhiteSpace(reflection))
                                {
                                    synth.Speak(reflection);
                                    Console.WriteLine($"Reflection: {reflection}");
                                }
                            }

                            zoneStartTimes[currentZone] = now;

                            string zoneMessage = BuildZoneInsight(currentZone, previousZone, currentBpm, sessionMax, sessionMin);
                            synth.Speak(zoneMessage);
                            Console.WriteLine($"Zone Insight: {zoneMessage}");
                        }

                        previousZone = currentZone;
                    }
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




    // === Vocabulary Pools ===
    static string[] adjectivesLow = { "peaceful", "gentle", "restorative", "calm", "soft", "quiet", "tranquil" };
    static string[] adjectivesMid = { "focused", "steady", "persistent", "active", "motivated", "fluid", "resilient" };
    static string[] adjectivesHigh = { "bold", "intense", "explosive", "electric", "relentless", "unstoppable", "vigorous" };

    static string[] adverbs = { "smoothly", "deliberately", "rapidly", "quietly", "noticeably", "strongly", "suddenly" };
    static string[] pronouns = { "Your heart", "You", "This rhythm", "That BPM", "Your pulse", "The flow" };
    static string[] verbsUp = { "is rising", "is surging", "just accelerated", "is climbing", "is charging upward" };
    static string[] verbsDown = { "is falling", "just dropped", "is easing off", "is relaxing", "slowed gently" };
    static string[] verbsSteady = { "is cruising", "is holding steady", "is pacing smoothly", "is flowing consistently", "is sustaining rhythm" };

    static string GetZone(int bpm, int max)
    {
        double intensity = (double)bpm / max;
        if (intensity < 0.5) return "Rest";
        if (intensity < 0.7) return "Warmup";
        if (intensity < 0.85) return "Active";
        if (intensity < 0.95) return "Intense";
        return "Max";
    }

    static int GetDynamicThreshold(int bpm, int max)
    {
        double intensity = (double)bpm / max;
        if (intensity >= 0.95) return 2;
        if (intensity >= 0.8) return 5;
        if (intensity >= 0.6) return 10;
        return 20;
    }

    static string[] GetAdjectivePool(string zone) => zone switch
    {
        "Rest" => adjectivesLow,
        "Warmup" => adjectivesMid,
        "Active" => adjectivesMid,
        "Intense" => adjectivesHigh,
        "Max" => adjectivesHigh,
        _ => adjectivesMid
    };

    static string BuildDynamicPhrase(string changeType, string zone, int bpm)
    {
        string pronoun = Pick(pronouns);
        string adjective = Pick(GetAdjectivePool(zone));
        string adverb = Pick(adverbs);
        string verb = changeType switch
        {
            "start" => Pick(verbsSteady),
            "increase" => Pick(verbsUp),
            "decrease" => Pick(verbsDown),
            _ => "is adjusting"
        };

        string[] templates = new[]
        {
            $"{pronoun} {verb} {adverb}. BPM is {bpm} — feeling {adjective}.",
            $"{pronoun} is steady and {adjective}. Current BPM is {bpm}.",
            $"At {bpm} BPM, {pronoun} is {adjective} and {verb} {adverb}.",
            $"BPM now reads {bpm}. {pronoun} feels {adjective}, moving {adverb}.",
            $"{pronoun} {verb}. Heart rate at {bpm}. You're feeling {adjective}."
        };

        return Pick(templates);
    }

    static string BuildZoneInsight(string zone, string previous, int bpm, int sessionMax, int sessionMin)
    {
        bool exerted = sessionMax >= 140;
        return zone switch
        {
            "Warmup" when !exerted => Pick(new[]
            {
                "You're just getting started. Let’s build some momentum.",
                "Warming into motion — nice and easy.",
                "Easing into your rhythm — solid foundation forming."
            }),
            "Warmup" => Pick(new[]
            {
                "Easing the pace - a little relaxing.",
                "Sliding into warm-up range — smooth transition.",
                "Are we just getting started? or just now slowing down? - a mild pace"
            }),
            "Active" => Pick(new[]
            {
                "Strong cadence detected — great pacing.",
                "You’re in the zone now — keep cruising.",
                "Solid output — heart’s moving in rhythm."
            }),
            "Intense" => Pick(new[]
            {
                "That’s high effort! Stay focused and sharp.",
                "You're grinding hard — intense output confirmed.",
                "Peak energy phase — heart’s going for it!"
            }),
            "Max" => Pick(new[]
            {
                "Max effort engaged — you’re pushing limits!",
                "Absolute intensity detected — incredible performance.",
                "Full throttle — your heart’s in beast mode!"
            }),
            "Rest" when exerted => Pick(new[]
            {
                "Well-earned breather — recovery in motion.",
                "Your heart’s settling after a solid push.",
                "Rest zone detected — enjoy the calm."
            }),
            "Rest" => Pick(new[]
            {
                "Gentle BPM now — looking nice and relaxed.",
                "Heart rate is soft and easy — smooth rest.",
                "Tranquil rhythm detected — mellow vibes flowing."
            }),
            _ => $"Now in {zone} zone."
        };
    }

    static string BuildZoneReflection(string zone, TimeSpan duration)
    {
        int minutes = (int)duration.TotalMinutes;
        if (minutes < 2) return "";

        return zone switch
        {
            "Warmup" => $"You warmed up for {minutes} minutes — perfect prep.",
            "Active" => $"Held active pace for {minutes} minutes — smooth consistency.",
            "Intense" => $"You crushed {minutes} minutes of intense work — awesome effort!",
            "Max" => $"Max effort held for {minutes} minutes — elite level output.",
            "Rest" => $"You've rested for {minutes} minutes — solid recovery.",
            _ => $"Time spent in {zone} zone: {minutes} minutes."
        };
    }

    static string Pick(string[] options) => options[rand.Next(options.Length)];
}
