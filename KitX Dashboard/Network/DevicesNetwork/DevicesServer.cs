using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Services;
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

    private Dictionary<DeviceLocator, string> SignedDeviceTokens { get; } = [];

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

    internal bool IsDeviceTokenExist(string token) => SignedDeviceTokens.ContainsValue(token);

    internal bool IsDeviceSignedIn(DeviceLocator locator) => SignedDeviceTokens.ContainsKey(locator);

    internal void AddDeviceToken(DeviceLocator locator, string token) => SignedDeviceTokens.Add(locator, token);

    internal string SignInDevice(DeviceLocator locator)
    {
        var token = Guid.NewGuid().ToString();

        while (SignedDeviceTokens.ContainsValue(token))
            token = Guid.NewGuid().ToString();

        if (SignedDeviceTokens.TryAdd(locator, token) == false)
            SignedDeviceTokens[locator] = token;

        return token;
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
