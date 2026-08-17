using System.Collections.Generic;

namespace FUSE.Patches
{
    /// <summary>
    /// Tracks the one currently valid deferred-load token for each Unity instance id.
    /// Kept free of Unity types so cancellation and supersession semantics can be
    /// covered by the normal net48 test suite.
    /// </summary>
    internal sealed class FuseSceneryPendingLoadTokens
    {
        private readonly Dictionary<int, long> _tokens = new Dictionary<int, long>();
        private long _nextToken;

        internal int Count => _tokens.Count;

        internal bool Contains(int instanceId)
        {
            return _tokens.ContainsKey(instanceId);
        }

        internal bool IsCurrent(int instanceId, long token)
        {
            return _tokens.TryGetValue(instanceId, out var currentToken) &&
                   currentToken == token;
        }

        internal long Issue(int instanceId)
        {
            var token = unchecked(++_nextToken);
            if (token == 0)
            {
                token = unchecked(++_nextToken);
            }

            _tokens[instanceId] = token;
            return token;
        }

        internal void Invalidate(int instanceId)
        {
            _tokens.Remove(instanceId);
        }

        internal bool TryConsume(int instanceId, long token)
        {
            if (!_tokens.TryGetValue(instanceId, out var currentToken) ||
                currentToken != token)
            {
                return false;
            }

            _tokens.Remove(instanceId);
            return true;
        }

        internal void Clear()
        {
            _tokens.Clear();
            _nextToken = 0;
        }
    }
}
