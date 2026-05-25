using System;
using System.Collections.Generic;

namespace FUSE.Cache
{
    public abstract class FuseRuntimeCacheBase<TCache, TValue>
        where TCache : FuseRuntimeCacheBase<TCache, TValue>
        where TValue : class
    {
        // Case-insensitive keys so legacy mod data that references identifiers
        // in lowercase ('s2', 'sctc', 'alarka-branch') still resolves to the
        // game's canonical capitalized identifiers ('S2', 'SCTC', etc.). Every
        // other identifier lookup across FUSE uses StringComparer.OrdinalIgnoreCase;
        // the cache default used to be the only outlier and caused phantom
        // placeholder map features that broke prerequisite chains.
        protected readonly Dictionary<string, TValue> Items =
            new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);

        protected FuseRuntimeCacheBase()
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
