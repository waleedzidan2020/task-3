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

    public MainWindow()
    {
        InitializeComponent();
        NodesGrid.ItemsSource = _nodes;
        Loaded += (_, _) => ReloadNodes();
        Closed += (_, _) => _singBox.Stop();
    }

    private string ResolveNodesPath()
    {
        var raw = NodesFileBox.Text.Trim();
        return Path.IsPathRooted(raw) ? raw : Path.Combine(AppContext.BaseDirectory, raw);
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

            var items = JsonSerializer.Deserialize<List<RouteNode>>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            _nodes.Clear();
            foreach (var item in items)
                _nodes.Add(item);

            StatusText.Text = $"Loaded {_nodes.Count} nodes.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Load error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task TestAllAsync()
    {
        if (_nodes.Count == 0)
        {
            StatusText.Text = "No nodes loaded.";
            return;
        }

        StatusText.Text = "Testing nodes...";
        foreach (var node in _nodes)
        {
            await _probe.ProbeAsync(node);
            NodesGrid.Items.Refresh();
            StatusText.Text = $"Testing {node.Name}: score {(double.IsInfinity(node.Score) ? "unreachable" : node.Score.ToString("0.0"))}";
        }

        var best = _nodes.Where(n => !double.IsInfinity(n.Score)).OrderBy(n => n.Score).FirstOrDefault();
        if (best is not null)
        {
            NodesGrid.SelectedItem = best;
            NodesGrid.ScrollIntoView(best);
            StatusText.Text = $"Best route: {best.Name} ({best.LatencyMs:0} ms, loss {best.LossPercent:0.0}%)";
        }
        else
        {
            StatusText.Text = "All nodes are unreachable.";
        }
    }

    private void ReloadNodes_Click(object sender, RoutedEventArgs e) => ReloadNodes();

    private async void TestAll_Click(object sender, RoutedEventArgs e)
    {
        try { await TestAllAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Probe error"); }
    }

    private async void AutoSelect_Click(object sender, RoutedEventArgs e)
    {
        try { await TestAllAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Auto select error"); }
    }

    private async void StartRoute_Click(object sender, RoutedEventArgs e)
    {
        if (NodesGrid.SelectedItem is not RouteNode node)
        {
            MessageBox.Show("Select a node first.");
            return;
        }

        var processName = ProcessNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            MessageBox.Show("Enter the game executable name, for example: game.exe");
            return;
        }

        try
        {
            StatusText.Text = "Preparing sing-box...";
            await _singBox.StartAsync(node, processName);
            StatusText.Text = $"Routing {processName} through {node.Name}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed to start route.";
            MessageBox.Show(ex.ToString(), "Routing error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopRoute_Click(object sender, RoutedEventArgs e)
    {
        _singBox.Stop();
        StatusText.Text = "Route stopped.";
    }
}