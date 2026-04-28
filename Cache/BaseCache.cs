using System.Collections.Generic;

namespace RAIL.Cache
{
    public abstract class BaseCache<TCache, TValue>
        where TCache : BaseCache<TCache, TValue>
        where TValue : class
    {
        protected readonly Dictionary<string, TValue> Items = new Dictionary<string, TValue>();

        protected BaseCache()
        {
            Instance = (TCache)this;
        }

        public static TCache Instance { get; private set; }

        public int Count => Items.Count;

        public TValue this[string id]
        {
            get
            {
                TryGetValue(id, out var value);
                return value;
            }
            set => Items[id] = value;
        }

        public IEnumerable<string> Ids => Items.Keys;

        public IEnumerable<TValue> Values => Items.Values;

        public bool TryGetValue(string id, out TValue value)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                value = null;
                return false;
            }

            return Items.TryGetValue(id, out value);
        }

        public void Set(string id, TValue value)
        {
            if (!string.IsNullOrWhiteSpace(id) && value != null)
            {
                Items[id] = value;
            }
        }

        public bool Remove(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && Items.Remove(id);
        }

        public virtual void Clear()
        {
            Items.Clear();
        }

        public abstract void Rebuild();
    }
}
