using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using GameRouteOptimizer.Models;
using GameRouteOptimizer.Services;

namespace GameRouteOptimizer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<RouteNode> _nodes = new();
    private readonly NetworkProbeService _probe = new();
    private readonly SingBoxService _singBox = new();

    private CancellationTokenSource? _monitorCts;

    public MainWindow()
    {
        InitializeComponent();
        NodesGrid.ItemsSource = _nodes;

        Loaded += (_, _) => ReloadNodes();
        Closed += (_, _) =>
        {
            StopMonitor();
            _singBox.Stop();
        };
    }

    private string ResolveNodesPath()
    {
        var raw = NodesFileBox.Text.Trim();
        return Path.IsPathRooted(raw)
            ? raw
            : Path.Combine(AppContext.BaseDirectory, raw);
    }

    private void ReloadNodes()
    {
        try
        {
            var path = ResolveNodesPath();

            if (!File.Exists(path))
            {
                StatusText.Text = $"Nodes file not found: {path}";
                return;
            }

            var items = JsonSerializer.Deserialize<List<RouteNode>>(
                            File.ReadAllText(path),
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            })
                        ?? new();

            _nodes.Clear();

            foreach (var item in items)
                _nodes.Add(item);

            StatusText.Text =
                $"Loaded {_nodes.Count(n => n.IsConfigured)} configured relay(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Load error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task TestAllAsync(
        int attempts = 4,
        CancellationToken ct = default)
    {
        var configured = _nodes
            .Where(n => n.IsConfigured)
            .ToList();

        if (configured.Count == 0)
        {
            StatusText.Text = "No configured relay nodes.";
            return;
        }

        StatusText.Text = "Testing all relay paths in parallel...";

        await _probe.ProbeAllAsync(
            configured,
            attempts,
            timeoutMs: 1500,
            ct);

        UpdateRoles(await _singBox.GetActiveRelayNameAsync(ct));
        NodesGrid.Items.Refresh();

        var best = configured
            .Where(n => !double.IsInfinity(n.Score))
            .OrderBy(n => n.Score)
            .FirstOrDefault();

        StatusText.Text = best is null
            ? "All configured relays are unreachable."
            : $"Best local measurement: {best.Name} ({best.LatencyMs:0} ms, loss {best.LossPercent:0.0}%).";
    }

    private void UpdateRoles(string? activeRelay)
    {
        var ordered = _nodes
            .Where(n => n.IsConfigured && !double.IsInfinity(n.Score))
            .OrderBy(n => n.Score)
            .ToList();

        var backup = ordered
            .FirstOrDefault(n =>
                !string.Equals(
                    n.Name,
                    activeRelay,
                    StringComparison.OrdinalIgnoreCase));

        foreach (var node in _nodes)
        {
            if (string.Equals(
                    node.Name,
                    activeRelay,
                    StringComparison.OrdinalIgnoreCase))
            {
                node.Role = "ACTIVE";
            }
            else if (backup is not null &&
                     ReferenceEquals(node, backup))
            {
                node.Role = "BACKUP";
            }
            else
            {
                node.Role = "";
            }
        }

        ActiveRelayText.Text =
            string.IsNullOrWhiteSpace(activeRelay)
                ? "Auto-selecting…"
                : activeRelay;

        BackupRelayText.Text =
            backup?.Name ?? "—";
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var configured = _nodes
                    .Where(n => n.IsConfigured)
                    .ToList();

                await _probe.ProbeAllAsync(
                    configured,
                    attempts: 3,
                    timeoutMs: 1200,
                    ct);

                var active = await _singBox.GetActiveRelayNameAsync(ct);

                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateRoles(active);
                    NodesGrid.Items.Refresh();

                    EngineStateText.Text =
                        _singBox.IsRunning
                            ? "Running / auto-routing"
                            : "Stopped";

                    StatusText.Text =
                        string.IsNullOrWhiteSpace(active)
                            ? "Engine is running; waiting for URLTest to select a relay."
                            : $"Game traffic is currently using {active}.";
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text =
                        $"Monitoring warning: {ex.Message}";
                });
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void StartMonitor()
    {
        StopMonitor();

        _monitorCts = new CancellationTokenSource();
        _ = MonitorLoopAsync(_monitorCts.Token);
    }

    private void StopMonitor()
    {
        if (_monitorCts is null)
            return;

        _monitorCts.Cancel();
        _monitorCts.Dispose();
        _monitorCts = null;
    }

    private void ReloadNodes_Click(
        object sender,
        RoutedEventArgs e) => ReloadNodes();

    private async void TestAll_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await TestAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Probe error");
        }
    }

    private async void StartRoute_Click(
        object sender,
        RoutedEventArgs e)
    {
        var processName = ProcessNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(processName))
        {
            MessageBox.Show(
                "Enter the game executable name, for example: game.exe");
            return;
        }

        var configured = _nodes
            .Where(n => n.IsConfigured)
            .ToList();

        if (configured.Count == 0)
        {
            MessageBox.Show(
                "Add at least one real VPS/relay to nodes.json first.");
            return;
        }

        try
        {
            StopMonitor();
            _singBox.Stop();

            StatusText.Text =
                $"Preparing {configured.Count} relay path(s)...";
            EngineStateText.Text = "Starting…";
            ActiveRelayText.Text = "Auto-selecting…";
            BackupRelayText.Text = "—";

            await _singBox.StartMultiRouteAsync(
                configured,
                processName);

            EngineStateText.Text = "Running / auto-routing";
            StatusText.Text =
                "Multi-route engine started. sing-box is measuring and selecting the relay path.";

            StartMonitor();
        }
        catch (Exception ex)
        {
            EngineStateText.Text = "Failed";
            StatusText.Text = "Failed to start multi-route engine.";

            MessageBox.Show(
                ex.ToString(),
                "Routing error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StopRoute_Click(
        object sender,
        RoutedEventArgs e)
    {
        StopMonitor();
        _singBox.Stop();

        foreach (var node in _nodes)
            node.Role = "";

        NodesGrid.Items.Refresh();
        EngineStateText.Text = "Stopped";
        ActiveRelayText.Text = "—";
        BackupRelayText.Text = "—";
        StatusText.Text = "Multi-route engine stopped.";
    }
}
