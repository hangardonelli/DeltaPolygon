namespace DeltaPolygon.Utilities;

/// <summary>
/// LRU cache manager for polygon reconstructions
/// Implements Least Recently Used (LRU) replacement policy
/// </summary>
public class CacheManager<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;
    private readonly LinkedList<CacheItem> _accessOrder;
    private readonly int _maxSize;
    private readonly object _lock = new();

    public CacheManager(int maxSize = 100)
    {
        if (maxSize <= 0)
        {
            throw new ArgumentException("The maximum cache size must be greater than 0", nameof(maxSize));
        }

        _maxSize = maxSize;
        _cache = new Dictionary<TKey, LinkedListNode<CacheItem>>();
        _accessOrder = new LinkedList<CacheItem>();
    }

    /// <summary>
    /// Gets a value from the cache
    /// </summary>
    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                // Move to end (most recent)
                _accessOrder.Remove(node);
                _accessOrder.AddLast(node);
                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    /// <summary>
    /// Adds or updates a value in the cache
    /// </summary>
    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var existingNode))
            {
                // Update existing value
                existingNode.Value.Value = value;
                _accessOrder.Remove(existingNode);
                _accessOrder.AddLast(existingNode);
            }
            else
            {
                // Add new
                if (_cache.Count >= _maxSize)
                {
                    // Remove the least recently used (LRU)
                    var lru = _accessOrder.First;
                    if (lru != null)
                    {
                        _cache.Remove(lru.Value.Key);
                        _accessOrder.RemoveFirst();
                    }
                }

                var newNode = new LinkedListNode<CacheItem>(new CacheItem { Key = key, Value = value });
                _cache[key] = newNode;
                _accessOrder.AddLast(newNode);
            }
        }
    }

    /// <summary>
    /// Removes a value from the cache
    /// </summary>
    public bool Remove(TKey key)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _cache.Remove(key);
                _accessOrder.Remove(node);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Clears the entire cache
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _accessOrder.Clear();
        }
    }

    /// <summary>
    /// Gets the number of elements in the cache
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }

    private class CacheItem
    {
        public TKey Key { get; set; } = default!;
        public TValue Value { get; set; } = default!;
    }
}
