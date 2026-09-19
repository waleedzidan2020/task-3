using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GameRouteOptimizer.Models;

namespace GameRouteOptimizer.Services;

public sealed class SingBoxService
{
    private const string AutoGroupTag = "game-auto";
    private const string ClashController = "127.0.0.1:9097";

    private readonly string _runtimeDir;
    private readonly HttpClient _http = new();
    private readonly Dictionary<string, string> _tagToNodeName = new(StringComparer.OrdinalIgnoreCase);
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };
    public string LastEngineError { get; private set; } = "";

    public SingBoxService()
    {
        _runtimeDir = Path.Combine(AppContext.BaseDirectory, "runtime");
        Directory.CreateDirectory(_runtimeDir);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GameRouteOptimizer/2.0");
    }

    public async Task<string> EnsureBinaryAsync(CancellationToken ct = default)
    {
        var exe = Path.Combine(_runtimeDir, "sing-box.exe");
        if (File.Exists(exe))
            return exe;

        var json = await _http.GetStringAsync(
            "https://api.github.com/repos/SagerNet/sing-box/releases/latest",
            ct);

        using var doc = JsonDocument.Parse(json);

        var asset = doc.RootElement
            .GetProperty("assets")
            .EnumerateArray()
            .FirstOrDefault(a =>
            {
                var name = a.GetProperty("name").GetString() ?? "";
                return name.Contains("windows-amd64", StringComparison.OrdinalIgnoreCase)
                       && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            });

        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException(
                "Could not find a Windows amd64 sing-box release.");

        var url = asset.GetProperty("browser_download_url").GetString()
                  ?? throw new InvalidOperationException(
                      "Missing sing-box download URL.");

        var zipPath = Path.Combine(_runtimeDir, "sing-box.zip");

        await using (var input = await _http.GetStreamAsync(url, ct))
        await using (var output = File.Create(zipPath))
            await input.CopyToAsync(output, ct);

        var extractDir = Path.Combine(_runtimeDir, "extract");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);

        ZipFile.ExtractToDirectory(zipPath, extractDir, true);

        var found = Directory
                        .GetFiles(
                            extractDir,
                            "sing-box.exe",
                            SearchOption.AllDirectories)
                        .FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        "sing-box.exe was not found in the downloaded archive.");

        File.Copy(found, exe, true);
        File.Delete(zipPath);
        Directory.Delete(extractDir, true);

        return exe;
    }

    public async Task StartMultiRouteAsync(
        IReadOnlyCollection<RouteNode> nodes,
        string processName,
        CancellationToken ct = default)
    {
        Stop();

        var configured = nodes
            .Where(n => n.IsConfigured)
            .ToList();

        if (configured.Count == 0)
            throw new InvalidOperationException(
                "No configured relay nodes were found.");

        _tagToNodeName.Clear();

        var exe = await EnsureBinaryAsync(ct);
        var configPath = Path.Combine(_runtimeDir, "config.json");
        var config = BuildMultiRouteConfig(configured, processName);

        await File.WriteAllTextAsync(
            configPath,
            config,
            new UTF8Encoding(false),
            ct);

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

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stderr = new StringBuilder();
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (stderr)
                {
                    if (stderr.Length < 12000)
                        stderr.AppendLine(e.Data);
                }
            }
        };

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start sing-box.");

        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();

        await Task.Delay(900, ct);

        if (_process.HasExited)
        {
            lock (stderr)
                LastEngineError = stderr.ToString().Trim();

            var details = string.IsNullOrWhiteSpace(LastEngineError)
                ? "sing-box exited during startup."
                : LastEngineError;

            Stop();
            throw new InvalidOperationException(details);
        }

        LastEngineError = "";
    }

    public async Task<string?> GetActiveRelayNameAsync(
        CancellationToken ct = default)
    {
        if (!IsRunning)
            return null;

        try
        {
            using var response = await _http.GetAsync(
                $"http://{ClashController}/proxies/{AutoGroupTag}",
                ct);

            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("now", out var nowElement))
                return null;

            var activeTag = nowElement.GetString();
            if (string.IsNullOrWhiteSpace(activeTag))
                return null;

            return _tagToNodeName.TryGetValue(activeTag, out var nodeName)
                ? nodeName
                : activeTag;
        }
        catch
        {
            return null;
        }
    }

    public void Stop()
    {
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort shutdown.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private string BuildMultiRouteConfig(
        IReadOnlyList<RouteNode> nodes,
        string processName)
    {
        var outbounds = new List<object>();
        var relayTags = new List<string>();

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var tag = $"relay-{i + 1}";

            relayTags.Add(tag);
            _tagToNodeName[tag] = node.Name;

            var outbound = new Dictionary<string, object?>
            {
                ["type"] = "socks",
                ["tag"] = tag,
                ["server"] = node.Host,
                ["server_port"] = node.Port,
                ["version"] = "5",
                ["network"] = new[] { "tcp", "udp" }
            };

            if (!string.IsNullOrWhiteSpace(node.Username))
                outbound["username"] = node.Username;

            if (!string.IsNullOrWhiteSpace(node.Password))
                outbound["password"] = node.Password;

            outbounds.Add(outbound);
        }

        outbounds.Add(new Dictionary<string, object?>
        {
            ["type"] = "urltest",
            ["tag"] = AutoGroupTag,
            ["outbounds"] = relayTags,
            ["url"] = "https://www.gstatic.com/generate_204",
            ["interval"] = "5s",
            ["tolerance"] = 15,
            ["idle_timeout"] = "30s",
            ["interrupt_exist_connections"] = true
        });

        outbounds.Add(new { type = "direct", tag = "direct" });
        outbounds.Add(new { type = "block", tag = "block" });

        var cfg = new
        {
            log = new
            {
                level = "info",
                timestamp = true
            },
            experimental = new
            {
                clash_api = new
                {
                    external_controller = ClashController
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
            outbounds,
            route = new
            {
                auto_detect_interface = true,
                rules = new object[]
                {
                    new
                    {
                        process_name = new[] { processName },
                        action = "route",
                        outbound = AutoGroupTag
                    }
                },
                final = "direct"
            }
        };

        return JsonSerializer.Serialize(
            cfg,
            new JsonSerializerOptions { WriteIndented = true });
    }
}
