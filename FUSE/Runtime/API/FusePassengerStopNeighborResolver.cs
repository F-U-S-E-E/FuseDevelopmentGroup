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

        internal static IReadOnlyDictionary<T, T[]> ResolveUnauthoredBranchNetwork<T>(
            IEnumerable<T> candidates,
            Func<T, string> branchSelector,
            Func<T, IEnumerable<string>> neighborIdsSelector,
            Func<T, string> identifierSelector,
            Func<T, double> xSelector,
            Func<T, double> ySelector,
            Func<T, double> zSelector)
            where T : class
        {
            if (candidates == null)
            {
                return new Dictionary<T, T[]>();
            }

            if (branchSelector == null)
            {
                throw new ArgumentNullException(nameof(branchSelector));
            }

            if (neighborIdsSelector == null)
            {
                throw new ArgumentNullException(nameof(neighborIdsSelector));
            }

            if (identifierSelector == null)
            {
                throw new ArgumentNullException(nameof(identifierSelector));
            }

            if (xSelector == null)
            {
                throw new ArgumentNullException(nameof(xSelector));
            }

            if (ySelector == null)
            {
                throw new ArgumentNullException(nameof(ySelector));
            }

            if (zSelector == null)
            {
                throw new ArgumentNullException(nameof(zSelector));
            }

            var result = new Dictionary<T, T[]>();
            var branchCandidates = candidates
                .Where(candidate => candidate != null)
                .Select(candidate => new BranchCandidate<T>(
                    candidate,
                    branchSelector(candidate)?.Trim(),
                    identifierSelector(candidate)?.Trim(),
                    xSelector(candidate),
                    ySelector(candidate),
                    zSelector(candidate),
                    (neighborIdsSelector(candidate) ?? Enumerable.Empty<string>())
                    .Any(id => !string.IsNullOrWhiteSpace(id))))
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Branch) &&
                    candidate.Branch.IndexOf(':') < 0)
                .ToArray();

            foreach (var branchGroup in branchCandidates.GroupBy(
                         candidate => candidate.Branch,
                         StringComparer.OrdinalIgnoreCase))
            {
                var branchStops = branchGroup
                    .OrderBy(candidate => candidate.Identifier, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (branchStops.Length < 2 ||
                    branchStops.Any(candidate => candidate.HasAuthoredNeighbors) ||
                    branchStops.Any(candidate => !candidate.HasFinitePosition) ||
                    !HasDistinctPositions(branchStops))
                {
                    continue;
                }

                var neighbors = branchStops.ToDictionary(
                    candidate => candidate,
                    candidate => new List<BranchCandidate<T>>());
                var visited = new HashSet<BranchCandidate<T>> { branchStops[0] };
                while (visited.Count < branchStops.Length)
                {
                    BranchCandidate<T> bestFrom = null;
                    BranchCandidate<T> bestTo = null;
                    var bestDistance = double.MaxValue;

                    foreach (var from in visited.OrderBy(
                                 candidate => candidate.Identifier,
                                 StringComparer.OrdinalIgnoreCase))
                    {
                        foreach (var to in branchStops.Where(candidate => !visited.Contains(candidate)))
                        {
                            var distance = SquaredDistance(from, to);
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                bestFrom = from;
                                bestTo = to;
                            }
                        }
                    }

                    if (bestFrom == null || bestTo == null)
                    {
                        break;
                    }

                    neighbors[bestFrom].Add(bestTo);
                    neighbors[bestTo].Add(bestFrom);
                    visited.Add(bestTo);
                }

                if (visited.Count != branchStops.Length)
                {
                    continue;
                }

                foreach (var branchStop in branchStops)
                {
                    result[branchStop.Value] = neighbors[branchStop]
                        .OrderBy(candidate => candidate.Identifier, StringComparer.OrdinalIgnoreCase)
                        .Select(candidate => candidate.Value)
                        .ToArray();
                }
            }

            return result;
        }

        private static bool HasDistinctPositions<T>(IReadOnlyList<BranchCandidate<T>> candidates)
            where T : class
        {
            var first = candidates[0];
            return candidates.Skip(1).Any(candidate => SquaredDistance(first, candidate) > 0.01d);
        }

        private static double SquaredDistance<T>(BranchCandidate<T> first, BranchCandidate<T> second)
            where T : class
        {
            var x = first.X - second.X;
            var y = first.Y - second.Y;
            var z = first.Z - second.Z;
            return x * x + y * y + z * z;
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

        private sealed class BranchCandidate<T>
            where T : class
        {
            internal BranchCandidate(
                T value,
                string branch,
                string identifier,
                double x,
                double y,
                double z,
                bool hasAuthoredNeighbors)
            {
                Value = value;
                Branch = branch;
                Identifier = identifier ?? string.Empty;
                X = x;
                Y = y;
                Z = z;
                HasAuthoredNeighbors = hasAuthoredNeighbors;
            }

            internal T Value { get; }
            internal string Branch { get; }
            internal string Identifier { get; }
            internal double X { get; }
            internal double Y { get; }
            internal double Z { get; }
            internal bool HasAuthoredNeighbors { get; }
            internal bool HasFinitePosition =>
                !double.IsNaN(X) && !double.IsInfinity(X) &&
                !double.IsNaN(Y) && !double.IsInfinity(Y) &&
                !double.IsNaN(Z) && !double.IsInfinity(Z);
        }
    }
}
