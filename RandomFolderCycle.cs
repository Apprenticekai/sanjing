namespace MultiFrameViewer;

internal sealed class RandomFolderCycle
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, HashSet<string>> _visitedByPool =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _lastSelected;

    public void RecordDisplayed(string poolKey, string folderPath)
    {
        lock (_syncRoot)
        {
            GetVisited(poolKey).Add(folderPath);
        }
    }

    public string? Pick(
        string poolKey,
        IReadOnlyList<string> candidates,
        string currentFolder)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        lock (_syncRoot)
        {
            var visited = GetVisited(poolKey);
            visited.Add(currentFolder);

            var remaining = candidates
                .Where(path => !string.Equals(path, currentFolder, StringComparison.OrdinalIgnoreCase))
                .Where(path => !visited.Contains(path))
                .ToList();

            if (remaining.Count == 0)
            {
                visited.Clear();
                visited.Add(currentFolder);
                remaining = candidates
                    .Where(path => !string.Equals(path, currentFolder, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (remaining.Count == 0)
            {
                return null;
            }

            if (remaining.Count > 1 && _lastSelected is not null)
            {
                var withoutImmediateRepeat = remaining
                    .Where(path => !string.Equals(path, _lastSelected, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (withoutImmediateRepeat.Count > 0)
                {
                    remaining = withoutImmediateRepeat;
                }
            }

            _lastSelected = remaining[Random.Shared.Next(remaining.Count)];
            return _lastSelected;
        }
    }

    public void Forget(string folderPath)
    {
        lock (_syncRoot)
        {
            foreach (var visited in _visitedByPool.Values)
            {
                visited.Remove(folderPath);
            }
            if (string.Equals(_lastSelected, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                _lastSelected = null;
            }
        }
    }

    private HashSet<string> GetVisited(string poolKey)
    {
        if (!_visitedByPool.TryGetValue(poolKey, out var visited))
        {
            visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _visitedByPool[poolKey] = visited;
        }

        return visited;
    }
}
