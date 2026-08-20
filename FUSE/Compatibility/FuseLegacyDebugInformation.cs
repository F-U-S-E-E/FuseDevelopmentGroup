using System;
using System.Collections.Generic;
using System.Text;
using FUSE.Infrastructure;
using GalaSoft.MvvmLight.Messaging;
using Railloader.Events;

namespace FUSE.Compatibility
{
    internal static class FuseLegacyDebugInformation
    {
        internal const int MaximumLines = 1000;
        internal const int MaximumCharacters = 131072;
        private const int MaximumLineCharacters = 4096;
        private static readonly char[] NewLineSeparator = { '\n' };

        internal static IReadOnlyList<string> Collect()
        {
            return Collect(Messenger.Default);
        }

        internal static IReadOnlyList<string> Collect(IMessenger messenger)
        {
            return Collect(message => messenger?.Send(message));
        }

        internal static IReadOnlyList<string> Collect(
            Action<WillCopyDebugInformation> dispatch)
        {
            var lines = new List<string>();
            var characterCount = 0;
            var truncated = false;

            void AppendLine(string value)
            {
                if (truncated || value == null)
                {
                    return;
                }

                var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
                foreach (var candidate in normalized.Split(NewLineSeparator, StringSplitOptions.None))
                {
                    var line = candidate.Length > MaximumLineCharacters
                        ? candidate.Substring(0, MaximumLineCharacters) + " …"
                        : candidate;
                    if (lines.Count >= MaximumLines
                        || characterCount + line.Length > MaximumCharacters)
                    {
                        truncated = true;
                        break;
                    }

                    lines.Add(line);
                    characterCount += line.Length;
                }
            }

            try
            {
                dispatch?.Invoke(new WillCopyDebugInformation(AppendLine));
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE contained a legacy debug-report listener failure: " +
                    ex.GetBaseException().Message);
            }

            if (truncated)
            {
                lines.Add(
                    $"[FUSE truncated legacy debug information at {MaximumLines} lines or " +
                    $"{MaximumCharacters} characters.]");
            }

            return lines;
        }

        internal static string AppendToReport(string report)
        {
            var lines = Collect();
            if (lines.Count == 0)
            {
                return report ?? string.Empty;
            }

            var builder = new StringBuilder(report ?? string.Empty);
            if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
            {
                builder.AppendLine();
            }

            builder.AppendLine("Legacy mod diagnostic contributions:");
            foreach (var line in lines)
            {
                builder.Append("  ").AppendLine(line);
            }

            return builder.ToString();
        }
    }
}
