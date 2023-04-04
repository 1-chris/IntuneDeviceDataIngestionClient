using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

class DeviceDataService : BackgroundService
{
    private readonly ManualResetEvent _shutdownEvent = new ManualResetEvent(false);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new Config();
        config = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var configPath = Path.Combine(appDataPath, "DeviceDataIngestionClient", "config.json");
            if (File.Exists(configPath))
            {
                config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath));
            }
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var path = Path.Combine("/etc", "DeviceDataIngestionClient", "config.json");
            if (File.Exists(path))
            {
                config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path));
            }            
        }

        if (config is null)
        {
            throw new Exception($"Create a config.json file in C:\\ProgramData\\DeviceDataIngestionClient\\ or /etc/DeviceDataIngestionClient/ with the following content: {JsonSerializer.Serialize(new Config())}");
        }

        Console.WriteLine("Service has started.");
        var deviceDataService = new DeviceDataAgent(config);
        deviceDataService.Initiate();

        stoppingToken.Register(() =>
        {
            Console.WriteLine("Service has stopped.");
            _shutdownEvent.Set();
        });

        await Task.Run(() => _shutdownEvent.WaitOne());
    }
}