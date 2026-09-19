using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using GameRouteOptimizer.Models;

namespace GameRouteOptimizer.Services;

public sealed class SingBoxService
{
    private readonly string _runtimeDir;
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public SingBoxService()
    {
        _runtimeDir = Path.Combine(AppContext.BaseDirectory, "runtime");
        Directory.CreateDirectory(_runtimeDir);
    }

    public async Task<string> EnsureBinaryAsync(CancellationToken ct = default)
    {
        var exe = Path.Combine(_runtimeDir, "sing-box.exe");
        if (File.Exists(exe))
            return exe;

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GameRouteOptimizer/1.0");

        var json = await http.GetStringAsync("https://api.github.com/repos/SagerNet/sing-box/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);

        var asset = doc.RootElement.GetProperty("assets")
            .EnumerateArray()
            .FirstOrDefault(a =>
            {
                var name = a.GetProperty("name").GetString() ?? "";
                return name.Contains("windows-amd64", StringComparison.OrdinalIgnoreCase)
                       && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            });

        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("Could not find a Windows amd64 sing-box release.");

        var url = asset.GetProperty("browser_download_url").GetString()
                  ?? throw new InvalidOperationException("Missing sing-box download URL.");

        var zipPath = Path.Combine(_runtimeDir, "sing-box.zip");
        await using (var input = await http.GetStreamAsync(url, ct))
        await using (var output = File.Create(zipPath))
            await input.CopyToAsync(output, ct);

        var extractDir = Path.Combine(_runtimeDir, "extract");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);

        var found = Directory.GetFiles(extractDir, "sing-box.exe", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidOperationException("sing-box.exe was not found in the downloaded archive.");

        File.Copy(found, exe, true);
        File.Delete(zipPath);
        Directory.Delete(extractDir, true);
        return exe;
    }

    public async Task StartAsync(RouteNode node, string processName, CancellationToken ct = default)
    {
        Stop();

        var exe = await EnsureBinaryAsync(ct);
        var configPath = Path.Combine(_runtimeDir, "config.json");
        await File.WriteAllTextAsync(configPath, BuildConfig(node, processName), ct);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"run -c \"{configPath}\"",
            WorkingDirectory = _runtimeDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start sing-box.");
    }

    public void Stop()
    {
        if (_process is null) return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private static string BuildConfig(RouteNode node, string processName)
    {
        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "socks",
            ["tag"] = "game-route",
            ["server"] = node.Host,
            ["server_port"] = node.Port,
            ["version"] = "5"
        };

        if (!string.IsNullOrWhiteSpace(node.Username))
            outbound["username"] = node.Username;
        if (!string.IsNullOrWhiteSpace(node.Password))
            outbound["password"] = node.Password;

        var cfg = new
        {
            log = new { level = "info", timestamp = true },
            dns = new
            {
                servers = new object[]
                {
                    new { tag = "cloudflare", address = "1.1.1.1", detour = "direct" }
                }
            },
            inbounds = new object[]
            {
                new
                {
                    type = "tun",
                    tag = "tun-in",
                    interface_name = "GameRoute",
                    address = new[] { "172.19.0.1/30" },
                    auto_route = true,
                    strict_route = false,
                    stack = "mixed"
                }
            },
            outbounds = new object[]
            {
                outbound,
                new { type = "direct", tag = "direct" },
                new { type = "block", tag = "block" }
            },
            route = new
            {
                auto_detect_interface = true,
                rules = new object[]
                {
                    new
                    {
                        process_name = new[] { processName },
                        outbound = "game-route"
                    }
                },
                final = "direct"
            }
        };

        return JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
    }
}