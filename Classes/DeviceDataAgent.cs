using System.Diagnostics;
using System.Management;
using System.Text.Json;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

public class DeviceDataAgent
{
    public int DataCollectionInterval = 60000;
    public string EndpointBase { get; set; } = "https://your.domain.com/";
    public string AuthorizationToken { get; set; } = "";

    public DeviceDataAgent(Config config)
    {
        EndpointBase = config.EndpointUrl;
        AuthorizationToken = config.AuthorizationToken;
        DataCollectionInterval = config.DataCollectionInterval;
    }

    public async void Initiate()
    {
        var deviceId = await GetDeviceIdAsync();
        var azureAdDeviceId = await GetAzureAdDeviceId();
        var computerName = Environment.MachineName;
        var lastBootUpTime = await GetLastBootUpTimeAsync();
        var totalPhysicalMemory = await GetTotalPhysicalMemoryAsync();

        var baseProcessData = new BaseProcessData()
        {
            DeviceId = deviceId,
            AzureAdDeviceId = azureAdDeviceId,
            ComputerName = computerName,
            LastBootUpTime = lastBootUpTime,
            TotalPhysicalMemory = totalPhysicalMemory
        };
        
        HttpClient httpClient = new();
        httpClient.DefaultRequestHeaders.Add("Authorization", AuthorizationToken);
        httpClient.DefaultRequestHeaders.Add("DeviceId", deviceId);
        
        Dictionary<int, Process> previousProcesses = GetCurrentProcesses();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            try {
                string processCreationQuery = "SELECT * FROM Win32_ProcessStartTrace";
                ManagementEventWatcher processCreationWatcher = new ManagementEventWatcher(processCreationQuery);
                processCreationWatcher.EventArrived += (sender, e) => ProcessCreationWatcher_EventArrived(sender, e, baseProcessData, httpClient);
                processCreationWatcher.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Unable to start process creation watcher: {ex.Message}");
            }
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            _ = WatchProcessesAsync(previousProcesses, baseProcessData, httpClient);
        }

        while (true)
        {
            var freeMemory = GetFreePhysicalMemoryAsync();
            var cpuLoad = GetCpuLoadAsync();
            var pingMs = GetPingMsAsync();
            var freeStorage = GetFreeStorageAsync();
            var processCount = Process.GetProcesses().Length;
            var uptimeTotalDays = (DateTime.Now - lastBootUpTime).TotalDays;

            var data = new
            {
                deviceId,
                azureAdDeviceId,
                ComputerName = computerName,
                LastBootUpTime = lastBootUpTime,
                UptimeTotalDays = uptimeTotalDays,
                FreePhysicalMemoryMB = await freeMemory,
                TotalPhysicalMemoryMB = totalPhysicalMemory,
                CpuLoad = await cpuLoad,
                ProcessCount = processCount,
                FreeStorage = await freeStorage,
                PingMs = await pingMs,
                OSEnvironment = RuntimeInformation.OSDescription,
            };

            var response = PostDataAsync(EndpointBase+"DeviceDataEndpoint", data, httpClient);
            await Task.Delay(DataCollectionInterval);
        }
    }   
    public async Task WatchProcessesAsync(Dictionary<int, Process> previousProcesses, BaseProcessData baseProcessData, HttpClient httpClient)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));

            var currentProcesses = GetCurrentProcesses();
            var newProcesses = currentProcesses.Keys.Except(previousProcesses.Keys);

            foreach (var processId in newProcesses)
            {
                Process process = currentProcesses[processId];
                var data = new
                {
                    deviceId = baseProcessData.DeviceId,
                    azureAdDeviceId = baseProcessData.AzureAdDeviceId,
                    ComputerName = baseProcessData.ComputerName,
                    ProcessName = process.ProcessName,
                    ProcessId = process.Id,
                    OSEnvironment = RuntimeInformation.OSDescription,
                };

                var response = PostDataAsync(EndpointBase+"DeviceDataEndpointProcess", data, httpClient);
            }

            previousProcesses = currentProcesses;
        }
    }
    public static Dictionary<int, Process> GetCurrentProcesses()
    {
        return Process.GetProcesses().ToDictionary(p => p.Id);
    }
    public void ProcessCreationWatcher_EventArrived(object sender, EventArrivedEventArgs e, BaseProcessData baseProcessData, HttpClient httpClient)
    {
        var data = new 
        {
            deviceId = baseProcessData.DeviceId,
            azureAdDeviceId = baseProcessData.AzureAdDeviceId,
            ComputerName = baseProcessData.ComputerName,
            ProcessName = e.NewEvent.Properties["ProcessName"].Value.ToString(),
            ProcessId = (uint)e.NewEvent.Properties["ProcessID"].Value,
            OSEnvironment = RuntimeInformation.OSDescription,
        };

        var response = PostDataAsync(EndpointBase+"DeviceDataEndpointProcess", data, httpClient);
    }
    public static async Task<string> GetDeviceIdAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Provisioning\Diagnostics\Autopilot\EstablishedCorrelations", false);
            var value = await Task.FromResult(key?.GetValue("EntDMID", "00000000-0000-0000-0000-000000000000") as string);
            return value ?? "00000000-0000-0000-0000-000000000000";
        }
        // TODO: implement for Linux
        return "00000000-0000-0000-0000-000000000000";
    }
    public static async Task<HttpResponseMessage> PostDataAsync(string endpoint, object data, HttpClient httpClient)
    {
        var jsonData = JsonSerializer.Serialize(data);
        Console.WriteLine($"Sending data {jsonData}");

        var response = await httpClient.PostAsync(endpoint, new StringContent(jsonData, System.Text.Encoding.UTF8, "application/json"));
        return response;
    }
    public static async Task<string> GetAzureAdDeviceId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            var startInfo = new ProcessStartInfo("dsregcmd", "/status")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            var match = Regex.Match(output, @"DeviceId\s*:\s*(?<id>[^ ]+)");

            return match.Success ? match.Groups["id"].Value.Trim() : "00000000-0000-0000-0000-000000000000";
        }
        
        // TODO: implement for Linux
        return "00000000-0000-0000-0000-000000000000";
    }
    public static async Task<DateTime> GetLastBootUpTimeAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            using var enumerator = searcher.Get().GetEnumerator();
            enumerator.MoveNext();
            var managementLastBootUpTime = enumerator.Current["LastBootUpTime"].ToString();
            if (managementLastBootUpTime is null)
                throw new Exception("Could not get last boot up time.");

            var lastBootUpTime = ManagementDateTimeConverter.ToDateTime(managementLastBootUpTime);
            return await Task.FromResult(lastBootUpTime);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var uptimeString = await File.ReadAllTextAsync("/proc/uptime");
            var uptimeSeconds = double.Parse(uptimeString.Split()[0]);
            var lastBootUpTime = DateTime.UtcNow - TimeSpan.FromSeconds(uptimeSeconds);
            return lastBootUpTime;
        }

        throw new NotSupportedException("The current operating system is not supported.");
    }
    public static async Task<double> GetFreePhysicalMemoryAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
            using var enumerator = searcher.Get().GetEnumerator();
            enumerator.MoveNext();
            double freeMemory = Convert.ToDouble(enumerator.Current["FreePhysicalMemory"]) / 1024;
            return freeMemory;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var startInfo = new ProcessStartInfo("free", "-m")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            var match = Regex.Match(output, @"Mem:\s+\d+\s+\d+\s+(?<free>\d+)");
            return match.Success ? double.Parse(match.Groups["free"].Value) : 0;
        }
        
        throw new NotSupportedException("The current operating system is not supported.");
    }
    public static async Task<double> GetTotalPhysicalMemoryAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            using var enumerator = searcher.Get().GetEnumerator();
            enumerator.MoveNext();
            double totalMemory = Convert.ToDouble(enumerator.Current["TotalVisibleMemorySize"]) / 1024;
            return totalMemory;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var startInfo = new ProcessStartInfo("free", "-m")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            var match = Regex.Match(output, @"Mem:\s+(?<total>\d+)\s+\d+\s+\d+");
            return match.Success ? double.Parse(match.Groups["total"].Value) : 0;
        }
        
        throw new NotSupportedException("The current operating system is not supported.");
    }
    public static async Task<double> GetCpuLoadAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
            using var enumerator = searcher.Get().GetEnumerator();
            enumerator.MoveNext();
            double cpuLoad = Convert.ToDouble(enumerator.Current["LoadPercentage"]);
            return cpuLoad;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var startInfo = new ProcessStartInfo("top", "-bn1")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            var match = Regex.Match(output, @"%Cpu\(s\):\s+(?<load>[0-9.]+)\s+us");
            return match.Success ? double.Parse(match.Groups["load"].Value) : 0;
        }

        throw new NotSupportedException("The current operating system is not supported.");
    }
    public static async Task<long> GetPingMsAsync()
    {
        using var ping = new System.Net.NetworkInformation.Ping();
        var reply = await ping.SendPingAsync("1.1.1.1");
        return reply.RoundtripTime;
    }
    public static Task<long> GetFreeStorageAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var driveInfo = new DriveInfo("C");
            return Task.FromResult(driveInfo.AvailableFreeSpace / 1024 / 1024);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var startInfo = new ProcessStartInfo("df", "-m --output=avail /")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var match = Regex.Match(output, @"\n(?<free>\d+)");
            return Task.FromResult(match.Success ? long.Parse(match.Groups["free"].Value) : 0);
        }

        throw new NotSupportedException("The current operating system is not supported.");
    }
}

