using System.Collections.Generic;

namespace Orchestrion.Persistence;

public class LocalPlaylist
{
    public string Name { get; set; } = "";
    public List<string> Files { get; set; } = new();
    public int CurrentIndex { get; set; } = -1;

    public LocalPlaylist() { }
    public LocalPlaylist(string name) { Name = name; }

    public void Add(string path) => Files.Add(path);
    public void AddRange(IEnumerable<string> paths) => Files.AddRange(paths);
    public void RemoveAt(int index) => Files.RemoveAt(index);
}
