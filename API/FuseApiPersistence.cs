using System;
using System.Collections.Generic;
using FUSE.Authoring;
using FUSE.Infrastructure;

namespace FUSE.API
{
    public sealed class FuseObjectSaveOptions
    {
        public string PackageId { get; set; }
        public bool SaveImmediately { get; set; }
        public bool QueueAutosave { get; set; }
        public string Reason { get; set; }

        public static FuseObjectSaveOptions ManualSave(string packageId, string reason = null)
        {
            return new FuseObjectSaveOptions
            {
                PackageId = packageId,
                SaveImmediately = true,
                Reason = reason ?? "manual API save"
            };
        }

        public static FuseObjectSaveOptions Autosave(string packageId, string reason = null)
        {
            return new FuseObjectSaveOptions
            {
                PackageId = packageId,
                QueueAutosave = true,
                Reason = reason ?? "API autosave"
            };
        }
    }

    public static class FuseApiPersistence
    {
        [ThreadStatic]
        private static Stack<FuseObjectSaveOptions> _saveScopes;

        [ThreadStatic]
        private static int _recordingSuppressed;

        private static Stack<FuseObjectSaveOptions> SaveScopes =>
            _saveScopes ?? (_saveScopes = new Stack<FuseObjectSaveOptions>());

        public static IDisposable Begin(FuseObjectSaveOptions options)
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
            return Begin(FuseObjectSaveOptions.ManualSave(packageId, reason));
        }

        public static IDisposable BeginAutosave(string packageId, string reason = null)
        {
            return Begin(FuseObjectSaveOptions.Autosave(packageId, reason));
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

            FuseRuntimeDefinitionCache.Store(kind, id, definition);

            var options = CurrentOptions;
            if (options == null || (!options.SaveImmediately && !options.QueueAutosave))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(options.PackageId))
            {
                FuseLog.Warning($"FUSE API persistence skipped '{kind}' '{id}' because no package id was provided.");
                return;
            }

            if (options.SaveImmediately)
            {
                FuseAuthoringPersistenceService.SaveDefinitionObject(
                    options.PackageId,
                    kind,
                    id,
                    definition,
                    options.Reason ?? "manual API save");
            }

            if (options.QueueAutosave)
            {
                FuseAuthoringPersistenceService.QueueDefinitionAutosave(
                    options.PackageId,
                    kind,
                    id,
                    definition,
                    options.Reason ?? "API autosave");
            }
        }

        private static FuseObjectSaveOptions CurrentOptions
        {
            get
            {
                return SaveScopes.Count > 0 ? SaveScopes.Peek() : null;
            }
        }

        private sealed class SaveScope : IDisposable
        {
            private readonly FuseObjectSaveOptions _options;
            private bool _disposed;

            public SaveScope(FuseObjectSaveOptions options)
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
                    FuseLog.Warning("FUSE API persistence save scope was disposed out of order; later API saves may use the wrong package context.");
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
