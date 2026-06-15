namespace AltRunSharp
{
    public interface IConfigService
    {
        AppConfig LoadConfig();
        void SaveConfig(AppConfig config);
    }
}
