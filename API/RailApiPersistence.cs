using System;
using System.Collections.Generic;
using RAIL.Authoring;
using RAIL.Infrastructure;

namespace RAIL.API
{
    public sealed class RailObjectSaveOptions
    {
        public string PackageId { get; set; }
        public bool SaveImmediately { get; set; }
        public bool QueueAutosave { get; set; }
        public string Reason { get; set; }

        public static RailObjectSaveOptions ManualSave(string packageId, string reason = null)
        {
            return new RailObjectSaveOptions
            {
                PackageId = packageId,
                SaveImmediately = true,
                Reason = reason ?? "manual API save"
            };
        }

        public static RailObjectSaveOptions Autosave(string packageId, string reason = null)
        {
            return new RailObjectSaveOptions
            {
                PackageId = packageId,
                QueueAutosave = true,
                Reason = reason ?? "API autosave"
            };
        }
    }

    public static class RailApiPersistence
    {
        [ThreadStatic]
        private static Stack<RailObjectSaveOptions> _saveScopes;

        [ThreadStatic]
        private static int _recordingSuppressed;

        private static Stack<RailObjectSaveOptions> SaveScopes =>
            _saveScopes ?? (_saveScopes = new Stack<RailObjectSaveOptions>());

        public static IDisposable Begin(RailObjectSaveOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            SaveScopes.Push(options);
            return new SaveScope(options);
        }

        public static IDisposable BeginManualSave(string packageId, string reason = null)
        {
            return Begin(RailObjectSaveOptions.ManualSave(packageId, reason));
        }

        public static IDisposable BeginAutosave(string packageId, string reason = null)
        {
            return Begin(RailObjectSaveOptions.Autosave(packageId, reason));
        }

        public static IDisposable SuppressRecording()
        {
            _recordingSuppressed++;
            return new SuppressionScope();
        }

        public static void RecordDefinition<T>(string kind, string id, T definition)
            where T : class
        {
            if (_recordingSuppressed > 0 || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id) || definition == null)
            {
                return;
            }

            RailRuntimeDefinitionCache.Store(kind, id, definition);

            var options = CurrentOptions;
            if (options == null || (!options.SaveImmediately && !options.QueueAutosave))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(options.PackageId))
            {
                RailLog.Warning($"RAIL API persistence skipped '{kind}' '{id}' because no package id was provided.");
                return;
            }

            if (options.SaveImmediately)
            {
                RailAuthoringPersistenceService.SaveDefinitionObject(
                    options.PackageId,
                    kind,
                    id,
                    definition,
                    options.Reason ?? "manual API save");
            }

            if (options.QueueAutosave)
            {
                RailAuthoringPersistenceService.QueueDefinitionAutosave(
                    options.PackageId,
                    kind,
                    id,
                    definition,
                    options.Reason ?? "API autosave");
            }
        }

        private static RailObjectSaveOptions CurrentOptions
        {
            get
            {
                return SaveScopes.Count > 0 ? SaveScopes.Peek() : null;
            }
        }

        private sealed class SaveScope : IDisposable
        {
            private readonly RailObjectSaveOptions _options;
            private bool _disposed;

            public SaveScope(RailObjectSaveOptions options)
            {
                _options = options;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (SaveScopes.Count == 0)
                {
                    return;
                }

                var current = SaveScopes.Pop();
                if (!ReferenceEquals(current, _options))
                {
                    RailLog.Warning("RAIL API persistence save scope was disposed out of order; later API saves may use the wrong package context.");
                }
            }
        }

        private sealed class SuppressionScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _recordingSuppressed = Math.Max(0, _recordingSuppressed - 1);
            }
        }
    }
}
