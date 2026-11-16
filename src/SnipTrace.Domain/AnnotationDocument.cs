namespace SnipTrace.Domain;

/// <summary>
/// 标注文档：持有标注列表、序号状态与撤销栈。领域层不依赖 WPF。
/// </summary>
public sealed class AnnotationDocument
{
    public const int UndoCapacity = 100;

    private readonly List<Annotation> _annotations = new();
    private readonly List<State> _undo = new();
    private readonly List<State> _redo = new();
    private State? _pending;
    private int _nextNumber = 1;
    private int _zCounter;

    public AnnotationDocument(int imageWidth, int imageHeight)
    {
        ImageWidth = Math.Max(1, imageWidth);
        ImageHeight = Math.Max(1, imageHeight);
    }

    public int ImageWidth { get; private set; }

    public int ImageHeight { get; private set; }

    /// <summary>调整画布尺寸（覆盖层改选区大小时同步），已有标注坐标不变。</summary>
    public void ResizeCanvas(int width, int height)
    {
        int newWidth = Math.Max(1, width);
        int newHeight = Math.Max(1, height);
        if (newWidth == ImageWidth && newHeight == ImageHeight) return;
        ImageWidth = newWidth;
        ImageHeight = newHeight;
        RaiseChanged();
    }

    public IReadOnlyList<Annotation> Annotations => _annotations;

    public int NextNumber => _nextNumber;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? Changed;

    /// <summary>按 z 序渲染顺序返回标注。</summary>
    public IEnumerable<Annotation> InRenderOrder => _annotations.OrderBy(a => a.ZIndex);

    public void BeginEdit()
    {
        _pending ??= Capture();
    }

    public void EndEdit(bool commit = true)
    {
        if (_pending is null) return;
        if (commit)
        {
            _undo.Add(_pending);
            if (_undo.Count > UndoCapacity) _undo.RemoveAt(0);
            _redo.Clear();
        }

        _pending = null;
        RaiseChanged();
    }

    public T Add<T>(T annotation) where T : Annotation
    {
        BeginEdit();
        annotation.ZIndex = ++_zCounter;
        _annotations.Add(annotation);
        if (annotation is NumberBadgeAnnotation badge) _nextNumber = Math.Max(_nextNumber, badge.Number + 1);
        EndEdit();
        return annotation;
    }

    /// <summary>交互过程中插入标注，不单独产生撤销记录（由外层 BeginEdit/EndEdit 统一提交）。</summary>
    public void InsertWithoutHistory(Annotation annotation)
    {
        annotation.ZIndex = ++_zCounter;
        _annotations.Add(annotation);
        if (annotation is NumberBadgeAnnotation badge) _nextNumber = Math.Max(_nextNumber, badge.Number + 1);
        RaiseChanged();
    }

    /// <summary>交互过程中移除标注，不产生撤销记录。</summary>
    public bool RemoveWithoutHistory(Annotation annotation)
    {
        bool removed = _annotations.Remove(annotation);
        if (removed)
        {
            SyncSequence();
            _zCounter = _annotations.Count == 0 ? 0 : _annotations.Max(a => a.ZIndex);
            RaiseChanged();
        }

        return removed;
    }

    public bool Remove(Annotation annotation)
    {
        if (!_annotations.Contains(annotation)) return false;
        BeginEdit();
        _annotations.Remove(annotation);
        SyncSequence();
        _zCounter = _annotations.Count == 0 ? 0 : _annotations.Max(a => a.ZIndex);
        EndEdit();
        return true;
    }

    public bool RemoveRange(IEnumerable<Annotation> annotations)
    {
        var items = annotations.Distinct().Where(_annotations.Contains).ToList();
        if (items.Count == 0) return false;
        BeginEdit();
        foreach (var item in items) _annotations.Remove(item);
        SyncSequence();
        _zCounter = _annotations.Count == 0 ? 0 : _annotations.Max(a => a.ZIndex);
        EndEdit();
        return true;
    }

    public void Clear()
    {
        if (_annotations.Count == 0) return;
        BeginEdit();
        _annotations.Clear();
        _nextNumber = 1;
        _zCounter = 0;
        EndEdit();
    }

    /// <summary>从 1 重新开始编号，不影响已有标签。</summary>
    public void ResetNumbering()
    {
        BeginEdit();
        _nextNumber = 1;
        EndEdit();
    }

    /// <summary>按从上到下、从左到右的阅读顺序重排编号。</summary>
    public void RenumberByReadingOrder()
    {
        var badges = _annotations.OfType<NumberBadgeAnnotation>()
            .OrderBy(b => b.Bounds.Top)
            .ThenBy(b => b.Bounds.Left)
            .ToList();
        if (badges.Count == 0)
        {
            ResetNumbering();
            return;
        }

        BeginEdit();
        for (int i = 0; i < badges.Count; i++) badges[i].Number = i + 1;
        _nextNumber = badges.Count + 1;
        EndEdit();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(Capture());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        RaiseChanged();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(Capture());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        RaiseChanged();
    }

    public Annotation? HitTest(PixelPoint point, double extraTolerance = 0)
        => _annotations.OrderByDescending(a => a.ZIndex)
            .FirstOrDefault(a => a.HitTestPoint(point, extraTolerance));

    public IEnumerable<NumberBadgeAnnotation> Badges => _annotations.OfType<NumberBadgeAnnotation>();

    private void SyncSequence()
    {
        int max = _annotations.OfType<NumberBadgeAnnotation>().Select(b => b.Number).DefaultIfEmpty(0).Max();
        _nextNumber = Math.Max(1, max + 1);
    }

    private State Capture()
        => new(_annotations.Select(a => a.Clone()).ToList(), _nextNumber, _zCounter);

    private void Restore(State state)
    {
        _annotations.Clear();
        foreach (var item in state.Items) _annotations.Add(item.Clone());
        _nextNumber = state.NextNumber;
        _zCounter = state.ZCounter;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed record State(List<Annotation> Items, int NextNumber, int ZCounter);
}
