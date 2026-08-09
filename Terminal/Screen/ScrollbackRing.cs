namespace CoderCommander.Terminal.Screen;

/// <summary>
/// Fixed-capacity circular buffer of scrolled-off rows. Eviction just advances the head index and
/// lets the row be garbage-collected - no shifting.
/// </summary>
internal sealed class ScrollbackRing
{
    private readonly TerminalRow?[] _rows;
    private int _head; // index of the oldest row
    private int _count;

    public int Capacity { get; }
    public int Count => _count;

    /// <summary>Monotonically increasing count of every row ever pushed, even past eviction -
    /// unlike <see cref="Count"/> (which plateaus at <see cref="Capacity"/> once the ring is
    /// full), this lets a consumer that's scrolled back into history detect "the ring kept
    /// evicting old rows while I was looking at a fixed index" and re-anchor, instead of silently
    /// showing different content at the same index once the ring fills up.</summary>
    public long TotalPushed { get; private set; }

    public ScrollbackRing(int capacity)
    {
        Capacity = Math.Max(1, capacity);
        _rows = new TerminalRow?[Capacity];
    }

    public void Push(TerminalRow row)
    {
        if (_count == Capacity)
        {
            _rows[_head] = row;
            _head = (_head + 1) % Capacity;
        }
        else
        {
            _rows[(_head + _count) % Capacity] = row;
            _count++;
        }
        TotalPushed++;
    }

    /// <summary>0 = oldest row, Count-1 = newest.</summary>
    public TerminalRow Get(int index)
    {
        if (index < 0 || index >= _count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _rows[(_head + index) % Capacity]!;
    }

    public void Clear()
    {
        Array.Clear(_rows);
        _head = 0;
        _count = 0;
        TotalPushed = 0;
    }
}
