public class BaseProcessData
{
    public string DeviceId { get; set; } = "";
    public string AzureAdDeviceId { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public DateTime LastBootUpTime { get; set; }
    public double TotalPhysicalMemory { get; set; }
}