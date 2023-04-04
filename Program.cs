using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Length > 0)
{
    if (args[0] == "uninstall")
    {
        Installer.Uninstall();
        return;
    }

    if (args[0] == "install" && args.Length == 3)
    {
        Installer.Install(args[1], args[2]);
        return;
    }

    throw new Exception("Invalid arguments.");
}

var builder = new HostBuilder()
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<DeviceDataService>();
    });

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    builder.UseWindowsService();
}

await builder.Build().RunAsync();
