using System;
using System.Collections.Generic;
using System.Diagnostics;
using FUSE.Infrastructure;

namespace FUSE.Loading
{
    public sealed class FuseApplyReport
    {
        private const int MaxLoggedItemsPerBucket = 60;

        public FuseApplyReport(string definitionId, string reason, bool isReapply)
        {
            DefinitionId = definitionId ?? string.Empty;
            Reason = reason ?? string.Empty;
            IsReapply = isReapply;
        }

        public string DefinitionId { get; }
        public string Reason { get; }
        public bool IsReapply { get; }
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> CreatedObjects { get; } = new List<string>();
        public List<string> UpdatedObjects { get; } = new List<string>();
        public List<string> RemovedObjects { get; } = new List<string>();
        public List<string> SkippedObjects { get; } = new List<string>();
        public List<string> PostBindValidationResults { get; } = new List<string>();

        public bool IsFatal { get; private set; }
        public string FatalReason { get; private set; } = string.Empty;
        public bool HasErrors => Errors.Count > 0;

        public void MarkFatal(string phase, string kind, string id, string message)
        {
            if (!IsFatal)
            {
                IsFatal = true;
                FatalReason = message ?? string.Empty;
            }

            Errors.Add(Format(phase, kind, id) + $" error='{message ?? string.Empty}' fatal=true");
        }

        public void LogSummary()
        {
            FuseLog.Info(
                $"FUSE apply report package='{DefinitionId}' operation='apply' reason='{Reason}' reapply={IsReapply} " +
                $"created={CreatedObjects.Count} updated={UpdatedObjects.Count} removed={RemovedObjects.Count} " +
                $"skipped={SkippedObjects.Count} warnings={Warnings.Count} errors={Errors.Count} " +
                $"postBind={PostBindValidationResults.Count} fatal={IsFatal} fatalReason='{FatalReason}'.");

            LogBucket("warning", Warnings, FuseLog.Warning);
            LogBucket("error", Errors, FuseLog.Error);
            LogBucket("created", CreatedObjects, FuseLog.Info);
            LogBucket("updated", UpdatedObjects, FuseLog.Info);
            LogBucket("removed", RemovedObjects, FuseLog.Info);
            LogBucket("skipped", SkippedObjects, FuseLog.Info);
            LogBucket("post-bind", PostBindValidationResults, FuseLog.Info);
        }

        private void LogBucket(string label, IList<string> items, Action<string> logger)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            var count = Math.Min(items.Count, MaxLoggedItemsPerBucket);
            for (var index = 0; index < count; index++)
            {
                logger($"FUSE apply report {label}: package='{DefinitionId}' operation='apply' {items[index]}");
            }

            if (items.Count > count)
            {
                logger($"FUSE apply report {label}: package='{DefinitionId}' operation='apply' ... {items.Count - count} more item(s) omitted from log.");
            }
        }

        private static string Format(string phase, string kind, string id)
        {
            return $"phase='{phase ?? string.Empty}' kind='{kind ?? string.Empty}' id='{id ?? string.Empty}'";
        }
    }

    public sealed class FuseApplyTransaction
    {
        public FuseApplyTransaction(string definitionId, string reason, bool isReapply)
        {
            Report = new FuseApplyReport(definitionId, reason, isReapply);
        }

        public FuseApplyReport Report { get; }
        public string CurrentPhase { get; private set; } = string.Empty;

        public void RunPhase(string phase, Action action)
        {
            if (Report.IsFatal)
            {
                Skipped("phase", phase, "transaction already fatal");
                return;
            }

            var previousPhase = CurrentPhase;
            CurrentPhase = string.IsNullOrWhiteSpace(phase) ? "unknown" : phase;
            var stopwatch = Stopwatch.StartNew();
            FuseLog.Info($"FUSE apply phase package='{Report.DefinitionId}' operation='{CurrentPhase}' started.");
            try
            {
                action?.Invoke();
                stopwatch.Stop();
                FuseLog.Info($"FUSE apply phase package='{Report.DefinitionId}' operation='{CurrentPhase}' completed in {stopwatch.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Fatal("phase", CurrentPhase, ex.Message);
                FuseLog.Exception($"FUSE apply phase package='{Report.DefinitionId}' operation='{CurrentPhase}' failed after {stopwatch.ElapsedMilliseconds} ms", ex);
            }
            finally
            {
                CurrentPhase = previousPhase;
            }
        }

        public bool TryApply(string kind, string id, bool existsBeforeApply, Action action)
        {
            try
            {
                action?.Invoke();
                if (existsBeforeApply)
                {
                    Updated(kind, id);
                }
                else
                {
                    Created(kind, id);
                }

                return true;
            }
            catch (Exception ex)
            {
                Error(kind, id, ex.Message);
                FuseLog.Exception($"FUSE apply failed package='{Report.DefinitionId}' operation='{CurrentPhase}' kind='{kind}' id='{id}'", ex);
                return false;
            }
        }

        public bool TryRemove(string kind, string id, Action action)
        {
            try
            {
                action?.Invoke();
                Removed(kind, id);
                return true;
            }
            catch (Exception ex)
            {
                Error(kind, id, ex.Message);
                FuseLog.Exception($"FUSE remove failed package='{Report.DefinitionId}' operation='{CurrentPhase}' kind='{kind}' id='{id}'", ex);
                return false;
            }
        }

        public void Created(string kind, string id)
        {
            Report.CreatedObjects.Add(Format(kind, id));
        }

        public void Updated(string kind, string id)
        {
            Report.UpdatedObjects.Add(Format(kind, id));
        }

        public void Removed(string kind, string id)
        {
            Report.RemovedObjects.Add(Format(kind, id));
        }

        public void Skipped(string kind, string id, string reason)
        {
            Report.SkippedObjects.Add($"{Format(kind, id)} reason='{reason ?? string.Empty}'");
        }

        public void Warning(string kind, string id, string message)
        {
            Report.Warnings.Add($"{Format(kind, id)} warning='{message ?? string.Empty}'");
        }

        public void Error(string kind, string id, string message)
        {
            Report.Errors.Add($"{Format(kind, id)} error='{message ?? string.Empty}'");
        }

        public void Fatal(string kind, string id, string message)
        {
            Report.MarkFatal(CurrentPhase, kind, id, message);
        }

        public void PostBind(string kind, string id, string message)
        {
            Report.PostBindValidationResults.Add($"{Format(kind, id)} {message ?? string.Empty}");
        }

        private string Format(string kind, string id)
        {
            return $"phase='{CurrentPhase}' kind='{kind ?? string.Empty}' id='{id ?? string.Empty}'";
        }
    }
}
