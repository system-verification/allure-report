// ----------------------------------------------------------------
// Copyright (c) 2026 System Verification Sweden AB.
// Licensed under the Apache License, Version 2.0
// See LICENSE in the project root for license information.
// ----------------------------------------------------------------

using Allure.GaugePlugin.Configuration;
using Allure.GaugePlugin.Results;
using Allure.GaugePlugin.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading;

var gaugeProjectRoot = Environment.GetEnvironmentVariable("GAUGE_PROJECT_ROOT");
if (!string.IsNullOrEmpty(gaugeProjectRoot))
    Environment.CurrentDirectory = gaugeProjectRoot;

var shutdownCts = new CancellationTokenSource();

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Error);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http2);
    options.Limits.MaxRequestBodySize = long.MaxValue;
});

builder.Services.AddGrpc(options => options.MaxReceiveMessageSize = null);
builder.Services.AddSingleton(shutdownCts);
builder.Services.AddSingleton(AllureOptions.FromEnvironment());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AllureResultWriter>();

var app = builder.Build();

app.MapGrpcService<AllureMessagesHandler>();

await app.StartAsync();

var serverAddresses = app.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>();
if (serverAddresses != null)
{
    foreach (var address in serverAddresses.Addresses)
    {
        var port = new Uri(address).Port;
        Console.WriteLine($"Listening on port:{port}");
    }
}

await app.WaitForShutdownAsync(shutdownCts.Token);
