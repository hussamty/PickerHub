namespace PickerHub;

// A bounded transaction history; entries are accepted only after validation and scheduling persistence.
internal sealed class EditHistory
{
    readonly List<string> undo = [], redo = [];
    const int MaxEntries = 40, MaxBytes = 8 * 1024 * 1024;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    static void Push(List<string> list, string value)
    {
        if ((long)value.Length * 2 > MaxBytes) { list.Clear(); return; }
        list.Add(value);
        while (list.Count > MaxEntries || (list.Count > 1 && list.Sum(s => (long)s.Length * 2) > MaxBytes)) list.RemoveAt(0);
    }
    public void Commit(string before, string after) { if (before == after) return; Push(undo, before); redo.Clear(); }
    public string Travel(string current, bool forward, Action<string> persist)
    {
        var source = forward ? redo : undo; var target = forward ? undo : redo;
        if (source.Count == 0) return current;
        string next = source[^1]; persist(next); source.RemoveAt(source.Count - 1); Push(target, current); return next;
    }
    public void Clear() { undo.Clear(); redo.Clear(); }
}
