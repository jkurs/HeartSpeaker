namespace HeartSpeak.Providers;

public class FileProvider : IHeartRateProvider
{
    private readonly string _filePath;

    private FileProvider(string filePath)
    {
        _filePath = filePath;
    }

    public static FileProvider TryCreate(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.WriteLine("Error: File path cannot be empty.");
            return null;
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine("Error: File does not exist.");
            return null;
        }

        return new FileProvider(filePath);
    }

    public async Task<int?> GetCurrentHeartRateAsync()
    {
        var currentBpm = await File.ReadAllTextAsync(_filePath);
        if (int.TryParse(currentBpm, out int currentHeartRate))
        {
            return currentHeartRate;
        }

        return null;
    }
}