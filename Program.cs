using Microsoft.Playwright;
using System.Collections.Generic;
using System.Speech.Synthesis;
using HeartSpeak.Providers;
using HeartSpeak.Profiles;

class Program
{
    static Random rand = new();
    static Dictionary<string, DateTime> zoneStartTimes = new();
    static DateTime lastDecreaseSpoken = DateTime.MinValue;
    static DateTime lastInsightSpoken = DateTime.MinValue;
    static readonly TimeSpan insightCooldown = TimeSpan.FromSeconds(10);

    static bool isSimpleMode = false;

    static async Task Main()
    {
        Console.WriteLine("Welcome to HeartSpeak!");

        var profileManager = new ProfileManager();
        await profileManager.InitializeAsync();

        isSimpleMode = profileManager.SimpleMode;
        var maxHeartRate = profileManager.MaxHeartRate;
        var selectedProvider = profileManager.Provider;
        var overlayUrl = profileManager.OverlayUrl;
        var filePath = profileManager.FilePath;

        Console.Clear();

        IHeartRateProvider? provider = null;
        switch (selectedProvider)
        {
            case "pulsoid":
                provider = await PulsoidProvider.TryCreateAsync(overlayUrl);
                if (provider is null)
                    Console.WriteLine("An error occurred while initializing the Pulsoid provider.");
                break;

            case "file":
                provider = FileProvider.TryCreate(filePath);
                if (provider is null)
                    Console.WriteLine("An error occurred while initializing the file provider.");
                break;

            default:
                Console.WriteLine("Invalid provider specified in profile.");
                return;
        }

        if (provider is null) return;
        Console.Clear();

        var synth = new SpeechSynthesizer();
        synth.SetOutputToDefaultAudioDevice();
        synth.Volume = 100;

        Console.WriteLine($"Profile loaded: {profileManager.ProfileName}");
        Console.WriteLine("Heart rate data detected. Starting session.");
        Console.Clear();

        int lastBpm = -1;
        int sessionMax = -1;
        int sessionMin = -1;
        string previousZone = "";
        string[] zones = { "Resting", "Zone 1", "Zone 2", "Zone 3", "Zone 4", "Zone 5" };

        foreach (var zone in zones)
            zoneStartTimes[zone] = DateTime.MinValue;

        while (true)
        {
            try
            {
                int? possibleCurrentBpm = null;

                try
                {
                    possibleCurrentBpm = await provider.GetCurrentHeartRateAsync();
                }
                catch (Exception ex)
                {
                    // No matter how the provider works or what happens,
                    // we shouldn't crash the whole program if HR can't be read.
                    Console.WriteLine($"Error reading heart rate: {ex}");
                }

                if (possibleCurrentBpm != null)
                {
                    var currentBpm = possibleCurrentBpm.Value;
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
                        previousZone = currentZone;

                        zoneStartTimes[currentZone] = DateTime.UtcNow;
                    }

                    if (zoneChanged && !string.IsNullOrEmpty(previousZone) && zoneStartTimes.ContainsKey(previousZone) || lastBpm == -1)
                    {
                        TimeSpan duration = DateTime.UtcNow - zoneStartTimes[previousZone];
                        if (duration.TotalMinutes >= 2)
                        {
                            reflection = BuildNarration(previousZone, "reflection", currentBpm, duration, sessionMax, sessionMin);
                        }
                        zoneStartTimes[currentZone] = DateTime.UtcNow;

                        bool insightCooldownActive = DateTime.UtcNow - lastInsightSpoken > insightCooldown;

                        if (insightCooldownActive)
                        {
                            insight = BuildNarration(currentZone, "insight", currentBpm, null, sessionMax, sessionMin, previousZone: previousZone);
                            lastInsightSpoken = DateTime.UtcNow;
                        }

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
                        bool canSpeakInsight = DateTime.UtcNow - lastInsightSpoken > insightCooldown;
                        string solo = !string.IsNullOrWhiteSpace(reflection) ? reflection : (canSpeakInsight ? insight : "");

                        if (!string.IsNullOrWhiteSpace(solo))
                        {
                            synth.Speak(solo);
                            Console.WriteLine(solo);

                            if (solo == insight)
                                lastInsightSpoken = DateTime.UtcNow;
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

        double percentChange =
            intensity < 0.5 ? 0.20 :
            intensity < 0.6 ? 0.15 :
            intensity < 0.7 ? 0.12 :
            intensity < 0.8 ? 0.09 :
            intensity < 0.9 ? 0.05 :
            0.025;

        return (int)Math.Max(2, Math.Round(percentChange * max));
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

    static string? BuildNarration(string zone, string context, int bpm, TimeSpan? duration, int sessionMax, int sessionMin, string changeDirection = "", string previousZone = "")
    {
        if (isSimpleMode)
        {
            return context switch
            {
                "change" => GetChangeIntro(bpm, changeDirection),

                "insight" => ZoneRank(zone) > ZoneRank(previousZone) || ZoneRank(zone) == 0
                    ? $"Entered {zone}."
                    : "",

                "reflection" => duration.HasValue
                    ? $"Time spent in {zone}: {(int)duration.Value.TotalMinutes} minutes."
                    : $"Exited {zone}.",

                _ => $"Heart rate is {bpm}. Current zone is {zone}."
            };
        }

        string pronoun = Pick(pronouns)!;
        string adjective = Pick(GetAdjectivePool(zone))!;
        string adverb = Pick(adverbs)!;
        string verb = Pick(GetVerbPool(context))!;
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

            "insight" => ZoneRank(zone) > ZoneRank(previousZone) || ZoneRank(zone) == 0
                ? new[]
                {
                    $"Entering {zone} — tone is {adjective}.",
                    $"You’ve shifted zones — {zone} feels {adjective}.",
                    $"Now in {zone} — staying {adjective}."
                }
                : Array.Empty<string>(),

            "reflection" => new[]
            {
                $"{zone} lasted {durationText} — effort was {adjective}.",
                $"After {durationText} in {zone}, you’re feeling {adjective}.",
                $"{durationText} spent in {zone} — solid pacing."
            },

            _ => new[] { $"Heart rate is {bpm}. Currently in {zone} zone." }
        };

        return Pick(templates);
    }

    static string? Pick(string[] options) => options.Length != 0 ? options[rand.Next(options.Length)] : null;

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
        "Zone 4" => TimeSpan.FromSeconds(10),
        "Zone 5" => TimeSpan.FromSeconds(3),
        _ => TimeSpan.Zero
    };

    static int ZoneRank(string zone) => zone switch
    {
        "Resting" => 0,
        "Zone 1" => 1,
        "Zone 2" => 2,
        "Zone 3" => 3,
        "Zone 4" => 4,
        "Zone 5" => 5,
        _ => -1
    };
}
