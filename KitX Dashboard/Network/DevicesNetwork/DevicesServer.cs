using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Device;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Serilog;

namespace KitX.Dashboard.Network.DevicesNetwork;

public class DevicesServer : ConfigFetcher
{
    private static DevicesServer? _devicesServer;

    public static DevicesServer Instance => _devicesServer ??= new();

    private readonly int port = AppConfig.Web.UserSpecifiedDevicesServerPort ?? 0;

    private IHost? _host;

    private List<string> SignedDeviceTokens { get; } = [];

    public async Task<DevicesServer> RunAsync()
    {
        var host = CreateHostBuilder([]).Build();

        _host = host;

        new Thread(host.Run).Start();

        var addresses = host.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;

        while (addresses is null || addresses.Count == 0)
        {
            await Task.Delay(500);

            addresses = host.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
        }

        if (addresses is not null && addresses.Count != 0)
            EventService.Invoke(
                nameof(EventService.DevicesServerPortChanged),
                [new Uri(addresses.First()).Port]
            );

        return this;
    }

    public async Task<DevicesServer> CloseAsync()
    {
        if (_host is not null)
            await _host.StopAsync();

        return this;
    }

    private IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
                webBuilder.UseUrls($"http://0.0.0.0:{port}");
            })
            ;

    internal bool IsDeviceTokenExist(string token) => SignedDeviceTokens.Contains(token);

    internal void AddDeviceToken(string token) => SignedDeviceTokens.Add(token);

    public void RequireAcceptDeviceKey(string verifyCodeSHA1, int port, string deviceKey)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var window = new ExchangeDeviceKeyWindow();

            ViewInstances.ShowWindow(
                window.OnVerificationCodeEntered(async code =>
                {
                    if (verifyCodeSHA1.Equals(SecurityManager.GetSHA1(code)))
                    {
                        var deviceKeyDecrypted = SecurityManager.AesDecrypt(deviceKey, code);

                        var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

                        if (deviceKeyInstance is null) await window.OnErrorDecodeAsync();
                        else
                        {
                            var sender = deviceKeyInstance.Device;

                            var url = $"http://{sender.IPv4}:{port}/Api/Device/ExchangeKeyBack";

                            if (SecurityManager.Instance.LocalDeviceKey is null)
                            {
                                await window.OnErrorDecodeAsync();

                                return;
                            }

                            var currentKey = new DeviceKey()
                            {
                                Device = SecurityManager.Instance.LocalDeviceKey.Device,
                                RsaPrivateKeyD = SecurityManager.Instance.LocalDeviceKey.RsaPrivateKeyD,
                                RsaPrivateKeyModulus = SecurityManager.Instance.LocalDeviceKey.RsaPrivateKeyModulus,
                            };

                            if (currentKey is null)
                            {
                                await window.OnErrorDecodeAsync();

                                return;
                            }

                            var currentKeyJson = JsonSerializer.Serialize(currentKey);

                            var currentKeyEncrypted = SecurityManager.AesEncrypt(currentKeyJson, code);

                            using var http = new HttpClient();

                            var response = await http.PostAsync(url, new StringContent(currentKeyEncrypted));

                            if (response.IsSuccessStatusCode)
                                SecurityManager.Instance.AddDeviceKey(deviceKeyInstance);
                            else await window.OnErrorDecodeAsync();
                        }
                    }
                    else
                    {
                        await window.OnErrorDecodeAsync();
                    }
                }),
                ViewInstances.MainWindow,
                false,
                true
            );
        });
    }
}

public class Startup
{
    private readonly List<string> apiVersions = [.. typeof(DevicesServerApiVersions).GetEnumNames()];

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            apiVersions.ForEach(version =>
            {
                options.SwaggerDoc(version, new OpenApiInfo
                {
                    Title = "KitX Dashboard DevicesServer API",
                    Version = version,
                    Description = $"Version: {version}"
                });
            });
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseSwagger();

        app.UseSwaggerUI(options =>
        {
            apiVersions.ForEach(version =>
            {
                options.SwaggerEndpoint($"/swagger/{version}/swagger.json", version);
            });
        });

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}

public enum DevicesServerApiVersions
{
    V1 = 1,
}
