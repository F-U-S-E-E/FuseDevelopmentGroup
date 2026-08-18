using System;
using System.Collections.Generic;
using System.Linq;

namespace FUSE.Runtime.API
{
    internal static class FusePassengerStopNeighborResolver
    {
        internal static T[] Resolve<T>(
            IEnumerable<string> neighborIds,
            T source,
            IEnumerable<T> candidates,
            Func<T, string> identifierSelector,
            Func<T, string> timetableCodeSelector)
            where T : class
        {
            if (neighborIds == null || candidates == null)
            {
                return Array.Empty<T>();
            }

            if (identifierSelector == null)
            {
                throw new ArgumentNullException(nameof(identifierSelector));
            }

            if (timetableCodeSelector == null)
            {
                throw new ArgumentNullException(nameof(timetableCodeSelector));
            }

            var requestedIds = new HashSet<string>(
                neighborIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (requestedIds.Count == 0)
            {
                return Array.Empty<T>();
            }

            return candidates
                .Where(candidate =>
                    candidate != null &&
                    !ReferenceEquals(candidate, source) &&
                    (Matches(requestedIds, identifierSelector(candidate)) ||
                     Matches(requestedIds, timetableCodeSelector(candidate))))
                .ToArray();
        }

        internal static T[] ResolveIncoming<T>(
            T source,
            IEnumerable<T> candidates,
            Func<T, IEnumerable<string>> neighborIdsSelector,
            Func<T, string> identifierSelector,
            Func<T, string> timetableCodeSelector)
            where T : class
        {
            if (source == null || candidates == null)
            {
                return Array.Empty<T>();
            }

            if (neighborIdsSelector == null)
            {
                throw new ArgumentNullException(nameof(neighborIdsSelector));
            }

            if (identifierSelector == null)
            {
                throw new ArgumentNullException(nameof(identifierSelector));
            }

            if (timetableCodeSelector == null)
            {
                throw new ArgumentNullException(nameof(timetableCodeSelector));
            }

            var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(sourceIds, identifierSelector(source));
            AddIfPresent(sourceIds, timetableCodeSelector(source));
            if (sourceIds.Count == 0)
            {
                return Array.Empty<T>();
            }

            return candidates
                .Where(candidate =>
                    candidate != null &&
                    !ReferenceEquals(candidate, source) &&
                    (neighborIdsSelector(candidate) ?? Enumerable.Empty<string>())
                    .Any(neighborId => !string.IsNullOrWhiteSpace(neighborId) && sourceIds.Contains(neighborId.Trim())))
                .ToArray();
        }

        private static void AddIfPresent(HashSet<string> ids, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ids.Add(value.Trim());
            }
        }

        private static bool Matches(HashSet<string> requestedIds, string candidateId)
        {
            return !string.IsNullOrWhiteSpace(candidateId) &&
                   requestedIds.Contains(candidateId.Trim());
        }
    }
}
