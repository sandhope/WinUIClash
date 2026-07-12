using Microsoft.Extensions.Hosting;
using WinUIClash.HelperService;

// Windows Service 入口：以 SYSTEM 权限运行，提供 HTTP API 控制 mihomo 核心进程
var builder = Host.CreateApplicationBuilder();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "WinUIClashHelperService";
});

builder.Services.AddHostedService<HelperHostedService>();

var host = builder.Build();
host.Run();

