# Device Data Ingestion Client
This is a small project which can be used with this minimal web api project: [IntuneDeviceDataIngestionAPI](https://github.com/1-chris/IntuneDeviceDataIngestionAPI).

This project retrieves device statistics such as CPU, Memory, Disk, etc. and sends it to an API endpoint. 

It also retrieves Azure AD device ids / Intune device ids and sends it to the API endpoint.

------------------
## Features
* Runs as a Windows Service or Linux cron job
* Runs on Windows 10 or later and Linux
* Signals sent: 
  * Device Hostname
  * Total and Free Memory
  * CPU Percentage
  * Free Disk Space
  * Network
  * Azure AD Device Id
  * Intune Device Id
  * ICMP Response Time
  * Process Count
  * Uptime
  * New Processes
    * New processes are sent independently from the above signals, which are all batched into regular single requests.

### Features to be implemented:
* Network usage / network socket information signals

------------------
## To build
Requirements:
1. .NET 7.0 SDK. Other versions may work.
2. Windows 10 or later. This may not build on Linux.
3. Change the placeholder API endpoint and authentication token in code (to be reimplemented)
4. Run the below commands in the project directory.
5. You can now deploy the executables to hosts using your favourite method.

The below commands will build the project and output single, portable executable files.

### Build for Windows
```dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true```

### Build for Linux 
```dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true```

------------------

## To install
### Windows 
```.\IntuneDeviceDataIngestionClient.exe install https://your-endpoint.com/ your-auth-token```

This will install a Windows Service. The service is automatically started after installing.

### Linux
```./IntuneDeviceDataIngestionClient install https://your-endpoint.com/ your-auth-token```

This will add a cron job which runs on every startup. It automatically starts running after installation.


-----------------

## Build .intunewin package
Obtain IntuneWinAppUtil from: https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool.

Then run:

```.\IntuneWinAppUtil.exe -c C:\intune\IntuneDeviceDataIngestionClient\ -s C:\intune\IntuneDeviceDataIngestionClient\IntuneDeviceDataIngestionClient.exe -o C:\intune\output\```
