using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HeartSpeak.Profiles
{
    public class ProfileManager
    {
        public string ProfileName { get; private set; } = "";
        public bool SimpleMode { get; private set; } = false;
        public int MaxHeartRate { get; private set; } = 180;
        public string Provider { get; private set; } = "pulsoid";
        public string OverlayUrl { get; private set; } = "";
        public string FilePath { get; private set; } = "";

        private static readonly string ProfileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");

        public async Task InitializeAsync()
        {
            Directory.CreateDirectory(ProfileDir);
            var files = Directory.GetFiles(ProfileDir, "*Profile.ini");

            string path = "";

            if (files.Length == 0)
            {
                Console.WriteLine("No profiles detected.");
                Console.Write("Create one now by entering a name for this profile: ");
                ProfileName = Console.ReadLine()?.Trim();

                path = Path.Combine(ProfileDir, $"{ProfileName}_Profile.ini");
                await CreateNewProfileAsync(path);
            }
            else
            {
                Console.WriteLine("Existing Profiles:");
                for (int i = 0; i < files.Length; i++)
                    Console.WriteLine($"{i + 1}: {Path.GetFileNameWithoutExtension(files[i]).Replace("_Profile", "")}");

                Console.Write("Select profile number or enter new name: ");
                var input = Console.ReadLine()?.Trim();

                if (int.TryParse(input, out int index) && index >= 1 && index <= files.Length)
                {
                    path = files[index - 1];
                    ProfileName = Path.GetFileNameWithoutExtension(path).Replace("Profile", "");
                    Load(path);
                }
                else
                {
                    ProfileName = input;
                    path = Path.Combine(ProfileDir, $"{ProfileName}Profile.ini");

                    if (!File.Exists(path))
                        await CreateNewProfileAsync(path);
                    else
                        Load(path);
                }
            }
        }

        private async Task CreateNewProfileAsync(string path)
        {
            Console.Write("Enable Simple Mode? (Y/N): ");
            SimpleMode = Console.ReadLine()?.Trim().ToLower() == "y";

            Console.Write("Enter your maximum heart rate: ");
            MaxHeartRate = int.TryParse(Console.ReadLine(), out int hr) ? hr : 180;

            Console.Write("Select heart rate provider (pulsoid/file): ");
            Provider = Console.ReadLine()?.Trim().ToLower() ?? "pulsoid";

            if (Provider == "pulsoid")
            {
                Console.Write("Enter your Pulsoid overlay URL: ");
                OverlayUrl = Console.ReadLine()?.Trim();
            }
            else if (Provider == "file")
            {
                Console.Write("Enter full path to your heart rate file: ");
                FilePath = Console.ReadLine()?.Trim();
            }

            var lines = new List<string>
            {
                $"SimpleMode={(SimpleMode ? "true" : "false")}",
                $"MaxHeartRate={MaxHeartRate}",
                $"Provider={Provider}",
                $"OverlayUrl={OverlayUrl}",
                $"FilePath={FilePath}"
            };

            await File.WriteAllLinesAsync(path, lines);
        }

        private void Load(string path)
        {
            var config = File.ReadAllLines(path)
                .Select(line => line.Split('='))
                .Where(parts => parts.Length == 2)
                .ToDictionary(x => x[0], x => x[1]);

            SimpleMode = config.TryGetValue("SimpleMode", out var sm) && sm == "true";
            MaxHeartRate = config.TryGetValue("MaxHeartRate", out var hr) && int.TryParse(hr, out var val) ? val : 180;
            Provider = config.TryGetValue("Provider", out var pr) ? pr : "pulsoid";
            OverlayUrl = config.TryGetValue("OverlayUrl", out var url) ? url : "";
            FilePath = config.TryGetValue("FilePath", out var pathVal) ? pathVal : "";
        }
    }
}