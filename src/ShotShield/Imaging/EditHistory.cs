namespace ShotShield.Imaging;

public sealed class EditHistory
{
    private const int Limit = 20;
    private readonly List<WriteableBitmap> _undo = new();
    private readonly List<WriteableBitmap> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void PushUndo(WriteableBitmap snapshot)
    {
        _undo.Add(snapshot);
        _redo.Clear();
        if (_undo.Count > Limit)
        {
            _undo.RemoveAt(0);
        }
    }

    public WriteableBitmap? Undo(WriteableBitmap current)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        _redo.Add(ImageUtility.Clone(current));
        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return snapshot;
    }

    public WriteableBitmap? Redo(WriteableBitmap current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.Add(ImageUtility.Clone(current));
        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return snapshot;
    }
}
