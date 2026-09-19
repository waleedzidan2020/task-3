using System.Diagnostics;
using System.Net.Sockets;
using GameRouteOptimizer.Models;

namespace GameRouteOptimizer.Services;

public sealed class NetworkProbeService
{
    public async Task ProbeAsync(RouteNode node, int attempts = 6, int timeoutMs = 1800, CancellationToken ct = default)
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
                if (winner == connectTask && tcp.Connected)
                {
                    await connectTask;
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            catch
            {
                // Counted as loss.
            }

            if (i < attempts - 1)
                await Task.Delay(120, ct);
        }

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

        // Prefer low latency, then low jitter, heavily penalize loss.
        node.Score = node.LatencyMs + (node.JitterMs * 2.0) + (node.LossPercent * 12.0);
    }
}