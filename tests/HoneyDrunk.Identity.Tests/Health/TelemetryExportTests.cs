using HoneyDrunk.Identity.Api.Configuration;
using HoneyDrunk.Telemetry.Abstractions.Conventions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit.Abstractions;

namespace HoneyDrunk.Identity.Tests.Health;

/// <summary>Exercises actual protobuf exports through the application's registration.</summary>
public sealed class TelemetryExportTests(ITestOutputHelper output)
{
    /// <summary>All signals reach their distinct Pulse path with HoneyDrunk resource identity.</summary>
    /// <param name="basePath">Collector path with or without a trailing separator.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData("otlp")]
    [InlineData("otlp/")]
    public async Task ExportsAllSignalsToDistinctHttpPaths(string basePath)
    {
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var received = new ConcurrentDictionary<string, byte[]>();
        var receive = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    using var body = new MemoryStream();
                    await context.Request.InputStream.CopyToAsync(body);
                    received[context.Request.Url!.AbsolutePath] = body.ToArray();
                    context.Response.ContentType = "application/x-protobuf";
                    context.Response.ContentLength64 = 0;
                    context.Response.Close();
                }
            }
            catch (HttpListenerException error) when (!listener.IsListening)
            {
                output.WriteLine($"Receiver stopped during test cleanup: {error.Message}");
            }
            catch (ObjectDisposedException error) when (!listener.IsListening)
            {
                output.WriteLine($"Receiver disposed during test cleanup: {error.Message}");
            }
        });
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = $"http://localhost:{port}/{basePath}",
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddIdentityTelemetry(config, "verification");
            using var provider = services.BuildServiceProvider();
            var traces = provider.GetRequiredService<TracerProvider>();
            var metrics = provider.GetRequiredService<MeterProvider>();
            var log = provider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityExportVerification");
            using var source = new ActivitySource("HoneyDrunk.Data");
            using (var activity = source.StartActivity("export-verification"))
                Assert.NotNull(activity);
            using var meter = new Meter(TelemetryNames.GetMeterName("honeydrunk-identity"));
            meter.CreateCounter<long>("identity.verification").Add(1);
            LoggerMessage.Define(LogLevel.Information, new EventId(1), "Identity export verification")(log, null);
            Assert.True(traces.ForceFlush());
            Assert.True(metrics.ForceFlush());
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!received.ContainsKey("/otlp/v1/logs") && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            foreach (var signal in new[] { "traces", "metrics", "logs" })
            {
                Assert.True(received.TryGetValue($"/otlp/v1/{signal}", out var payload), $"Missing {signal} export");
                Assert.Contains("honeydrunk-identity", Encoding.UTF8.GetString(payload!));
            }

            Assert.DoesNotContain("/otlp/", received.Keys);
        }
        finally
        {
            listener.Stop();
            await receive;
        }
    }
}
