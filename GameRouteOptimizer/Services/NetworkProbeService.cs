using System.Diagnostics;
using System.Net.Sockets;
using GameRouteOptimizer.Models;

namespace GameRouteOptimizer.Services;

public sealed class NetworkProbeService
{
    public async Task ProbeAllAsync(
        IReadOnlyCollection<RouteNode> nodes,
        int attempts = 4,
        int timeoutMs = 1500,
        CancellationToken ct = default)
    {
        var tasks = nodes
            .Where(n => n.IsConfigured)
            .Select(n => ProbeAsync(n, attempts, timeoutMs, ct));

        await Task.WhenAll(tasks);
    }

    public async Task ProbeAsync(
        RouteNode node,
        int attempts = 6,
        int timeoutMs = 1800,
        CancellationToken ct = default)
    {
        var samples = new List<double>();

        for (var i = 0; i < attempts; i++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var tcp = new TcpClient();
                var sw = Stopwatch.StartNew();
                var connectTask = tcp.ConnectAsync(node.Host, node.Port, ct).AsTask();
                var winner = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct));

                if (winner == connectTask)
                {
                    await connectTask;
                    if (tcp.Connected)
                    {
                        sw.Stop();
                        samples.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
            }
            catch
            {
                // A failed/timeout connection counts as loss.
            }

            if (i < attempts - 1)
                await Task.Delay(100, ct);
        }

        node.LastTestUtc = DateTime.UtcNow;
        node.LossPercent = 100.0 * (attempts - samples.Count) / attempts;

        if (samples.Count == 0)
        {
            node.LatencyMs = double.PositiveInfinity;
            node.JitterMs = double.PositiveInfinity;
            node.Score = double.PositiveInfinity;
            return;
        }

        node.LatencyMs = samples.Average();

        if (samples.Count > 1)
        {
            var diffs = new List<double>();
            for (var i = 1; i < samples.Count; i++)
                diffs.Add(Math.Abs(samples[i] - samples[i - 1]));

            node.JitterMs = diffs.Average();
        }
        else
        {
            node.JitterMs = 0;
        }

        // Loss is intentionally expensive for real-time gaming.
        node.Score =
            node.LatencyMs +
            (node.JitterMs * 2.0) +
            (node.LossPercent * 12.0);
    }
}
