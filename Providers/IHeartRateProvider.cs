namespace HeartSpeak.Providers;

public interface IHeartRateProvider
{
    public Task<int?> GetCurrentHeartRateAsync();
}