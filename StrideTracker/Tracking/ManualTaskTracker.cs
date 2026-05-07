using System.Text.Json;

namespace StrideTracker.Tracking;

public sealed class ManualTaskTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly Dictionary<string, TaskNode> _nodesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TimeSpan> _ownDurationsById = new(StringComparer.OrdinalIgnoreCase);

    private string? _activeNodeId;
    private DateTimeOffset? _lastTickUtc;

    public IReadOnlyCollection<TaskNode> Nodes => _nodesById.Values;
    public string? ActiveNodeId => _activeNodeId;

    public ManualTaskTracker()
    {
        LoadDefaultTree();
    }

    public void Start(string nodeId, DateTimeOffset nowUtc)
    {
        if (!_nodesById.ContainsKey(nodeId))
        {
            return;
        }

        Tick(nowUtc);
        _activeNodeId = nodeId;
        _lastTickUtc = nowUtc;
    }

    public void Stop(DateTimeOffset nowUtc)
    {
        Tick(nowUtc);
        _activeNodeId = null;
        _lastTickUtc = null;
    }

    public void Tick(DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(_activeNodeId) || _lastTickUtc is null)
        {
            _lastTickUtc = nowUtc;
            return;
        }

        var elapsed = nowUtc - _lastTickUtc.Value;
        if (elapsed <= TimeSpan.Zero)
        {
            _lastTickUtc = nowUtc;
            return;
        }

        if (_ownDurationsById.TryGetValue(_activeNodeId, out var current))
        {
            _ownDurationsById[_activeNodeId] = current + elapsed;
        }
        else
        {
            _ownDurationsById[_activeNodeId] = elapsed;
        }

        _lastTickUtc = nowUtc;
    }

    public TaskNode? GetNode(string nodeId)
    {
        return _nodesById.TryGetValue(nodeId, out var node) ? node : null;
    }

    public IReadOnlyList<TaskNode> GetChildren(string? parentId)
    {
        return _nodesById.Values
            .Where(node => string.Equals(node.ParentId, parentId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.SortOrder)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public TimeSpan GetOwnDuration(string nodeId)
    {
        return _ownDurationsById.TryGetValue(nodeId, out var duration) ? duration : TimeSpan.Zero;
    }

    public TimeSpan GetTotalDuration(string nodeId)
    {
        var total = GetOwnDuration(nodeId);
        foreach (var child in GetChildren(nodeId))
        {
            total += GetTotalDuration(child.Id);
        }

        return total;
    }

    public void SaveState(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var state = new ManualTaskTrackerState(
            Nodes: _nodesById.Values.OrderBy(x => x.SortOrder).ToArray(),
            OwnDurations: _ownDurationsById.ToDictionary(x => x.Key, x => x.Value.TotalSeconds),
            ActiveNodeId: _activeNodeId);

        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    public void LoadState(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<ManualTaskTrackerState>(File.ReadAllText(path));
            if (state?.Nodes is null || state.Nodes.Length == 0)
            {
                return;
            }

            _nodesById.Clear();
            _ownDurationsById.Clear();
            foreach (var node in state.Nodes)
            {
                _nodesById[node.Id] = node;
            }

            if (state.OwnDurations is not null)
            {
                foreach (var (id, seconds) in state.OwnDurations)
                {
                    if (!_nodesById.ContainsKey(id) || seconds <= 0)
                    {
                        continue;
                    }

                    _ownDurationsById[id] = TimeSpan.FromSeconds(seconds);
                }
            }

            _activeNodeId = null;
            _lastTickUtc = null;
        }
        catch
        {
            // Ignore invalid task state.
        }
    }

    private void LoadDefaultTree()
    {
        _nodesById.Clear();

        AddNode(new TaskNode("work", "Работа", null, true, 1));
        AddNode(new TaskNode("project-x", "Проект X", "work", true, 1));
        AddNode(new TaskNode("db-refactor", "Рефакторинг базы", "project-x", false, 1));
        AddNode(new TaskNode("auth-bugfix", "Фикс бага авторизации", "project-x", false, 2));
        AddNode(new TaskNode("crm-order", "Порядок в CRM", "work", false, 2));
        AddNode(new TaskNode("client-call", "Созвон с заказчиком", "work", false, 3));

        AddNode(new TaskNode("personal", "Личное", null, true, 2));
        AddNode(new TaskNode("reading", "Чтение", "personal", false, 1));
        AddNode(new TaskNode("gym", "Спортзал", "personal", false, 2));
    }

    private void AddNode(TaskNode node)
    {
        _nodesById[node.Id] = node;
    }

    public sealed record TaskNode(string Id, string Name, string? ParentId, bool IsGroup, int SortOrder);

    private sealed record ManualTaskTrackerState(TaskNode[] Nodes, Dictionary<string, double> OwnDurations, string? ActiveNodeId);
}
