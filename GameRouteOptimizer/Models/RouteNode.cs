namespace GameRouteOptimizer.Models;

public sealed class RouteNode
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1080;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public double LatencyMs { get; set; } = double.PositiveInfinity;
    public double JitterMs { get; set; } = double.PositiveInfinity;
    public double LossPercent { get; set; } = 100;
    public double Score { get; set; } = double.PositiveInfinity;

    public string Status => double.IsInfinity(Score) ? "Not tested" : $"{LatencyMs:0} ms | jitter {JitterMs:0.0} | loss {LossPercent:0}%";
}