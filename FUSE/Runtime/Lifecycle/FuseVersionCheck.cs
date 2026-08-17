using System;
using System.Collections;
using Fuse.Core.Versioning;
using FUSE.Infrastructure;
using UI.Common;
using UnityEngine;
using UnityEngine.Networking;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Warns a player who is running an out-of-date FUSE build. On startup it
    /// asks GitHub — the canonical version authority — for the newest STABLE
    /// mod release and compares it against the running version. If the player is
    /// behind, it surfaces a non-blocking notice: a single toast on the next map
    /// load, plus a line in the FUSE Status panel with a link to the right place
    /// to update (Nexus for a Nexus-stamped install, GitHub otherwise).
    ///
    /// Nothing here blocks the game. Every failure mode — no network, a 403, a
    /// malformed response, a version that will not parse — is logged and dropped;
    /// a player is only ever told they are behind when a newer stable release is
    /// confirmed. Selection and comparison live in the game-free
    /// <see cref="FuseReleaseSelection"/> (unit-tested in FUSE.Core.Tests); this
    /// type is the thin Unity glue that fetches, parses, and presents.
    /// </summary>
    internal static class FuseVersionCheck
    {
        // The source Info.json is pinned at 0.0.0 so an unstamped local/debug
        // build is recognizable in UMM (see docs/RELEASING.md). Nagging a
        // developer that their working copy is "outdated" would be wrong, so the
        // check never runs for it.
        private const string DevBuildVersion = "0.0.0";

        // Unauthenticated GitHub REST. The repo is public, so this needs no
        // token; the previous private repo answered anonymous release queries
        // with 404, which is exactly why this feature could not ship before.
        // The feed is newest-first, so the current stable mod-v release is always
        // near the top; per_page=100 (GitHub's max) is far more than enough head-
        // room even with the external-editor and tools lanes interleaved, so the
        // newest stable can't be pushed off the single page we fetch.
        private static readonly string ReleasesApiUrl =
            "https://api.github.com/repos/" + FuseInstallSource.RepositoryOwner +
            "/" + FuseInstallSource.RepositoryName + "/releases?per_page=100";

        private static readonly object Gate = new object();

        // Supersession guard: each check gets a generation token; a response only
        // commits its result while its generation is still the newest. This makes
        // overlapping checks (a slow startup check plus a manual /fuse.update, or
        // repeated /fuse.update calls) safe — an older response that finishes out
        // of order is discarded instead of clobbering the newer result. Touched
        // only on the Unity main thread, always while holding Gate.
        private static readonly FuseGenerationGate Generation = new FuseGenerationGate();

        private static GameObject _host;
        private static bool _started;
        private static bool _checkComplete;
        private static bool _outdated;
        private static bool _toastPending;
        private static bool _toastShown;
        // Set once the first map has loaded — the point the toast UI is alive.
        // The toast is only ever presented while this is true, so a check that
        // completes at the splash/menu can never burn the one-shot on a no-op.
        private static bool _mapActive;
        private static string _currentVersionText;
        private static string _latestVersionText;
        private static string _latestTag;
        private static FuseInstallChannel _channel = FuseInstallChannel.Unknown;

        internal static bool CheckComplete { get { lock (Gate) { return _checkComplete; } } }

        internal static bool UpdateAvailable { get { lock (Gate) { return _outdated; } } }

        internal static string CurrentVersionText { get { lock (Gate) { return _currentVersionText; } } }

        internal static string LatestVersionText { get { lock (Gate) { return _latestVersionText; } } }

        internal static FuseInstallChannel Channel { get { lock (Gate) { return _channel; } } }

        /// <summary>The place to send the player to update, resolved from the install channel.</summary>
        internal static string ResolveUpdateUrl()
        {
            lock (Gate)
            {
                return _channel == FuseInstallChannel.Nexus
                    ? FuseInstallSource.NexusModUrl
                    : FuseInstallSource.GitHubReleaseTagUrl(_latestTag);
            }
        }

        /// <summary>One-line summary for the Status panel / console, or null when up to date or unchecked.</summary>
        internal static string DescribeUpdateNotice()
        {
            lock (Gate)
            {
                if (!_outdated)
                {
                    return null;
                }

                var where = _channel == FuseInstallChannel.Nexus ? "Nexus" : "GitHub";
                return $"Update available: FUSE {_latestVersionText} (you have {_currentVersionText}). Get it from {where}.";
            }
        }

        /// <summary>Starts the one-shot startup check. A no-op if already started or a dev build.</summary>
        internal static void Start(string modPath, string currentVersion) =>
            BeginCheck(modPath, currentVersion, force: false);

        /// <summary>Re-runs the check on demand (used by the console command).</summary>
        internal static void RequestRecheck(string modPath, string currentVersion) =>
            BeginCheck(modPath, currentVersion, force: true);

        private static void BeginCheck(string modPath, string currentVersion, bool force)
        {
            if (_started && !force)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(currentVersion) ||
                string.Equals(currentVersion.Trim(), DevBuildVersion, StringComparison.Ordinal))
            {
                FuseLog.Info("FUSE update check skipped: running a local/development build (version 0.0.0).");
                return;
            }

            _started = true;
            int generation;
            string versionToCheck;
            lock (Gate)
            {
                // Bump the generation so any check still in flight is superseded:
                // its response will be discarded when it lands.
                generation = Generation.Begin();
                _currentVersionText = currentVersion.Trim();
                versionToCheck = _currentVersionText;
                _channel = FuseInstallSource.ReadChannel(modPath);
                if (force)
                {
                    _checkComplete = false;
                    _outdated = false;
                    _toastPending = false;
                    _toastShown = false;
                    _latestVersionText = null;
                    _latestTag = null;
                }
            }

            var runner = EnsureHost();
            if (runner == null)
            {
                return;
            }

            FuseLog.Info($"FUSE update check started against {ReleasesApiUrl}.");
            runner.Begin(versionToCheck, generation);
        }

        /// <summary>
        /// Called from the map-load lifecycle (a point where the toast UI is
        /// guaranteed alive) to flush a pending "you're outdated" toast.
        /// </summary>
        internal static void NotifyMapDidLoad()
        {
            lock (Gate)
            {
                _mapActive = true;
            }

            TryShowToast();
        }

        /// <summary>
        /// Called when the active map unloads. Clears the "UI is alive" flag so a
        /// response that lands back at the main menu never presents a toast
        /// outside a live map; a still-pending toast simply waits for the next
        /// map load.
        /// </summary>
        internal static void NotifyMapWillUnload()
        {
            lock (Gate)
            {
                _mapActive = false;
            }
        }

        internal static void Shutdown()
        {
            if (_host != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(_host);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE version-check host shutdown failed", ex);
                }

                _host = null;
            }

            lock (Gate)
            {
                _started = false;
                _checkComplete = false;
                _outdated = false;
                _toastPending = false;
                _toastShown = false;
                _mapActive = false;
                _currentVersionText = null;
                _latestVersionText = null;
                _latestTag = null;
                _channel = FuseInstallChannel.Unknown;
                Generation.Reset();
            }
        }

        private static FuseVersionCheckRunner EnsureHost()
        {
            if (_host != null)
            {
                return _host.GetComponent<FuseVersionCheckRunner>();
            }

            GameObject host = null;
            try
            {
                host = new GameObject("FUSE.VersionCheck");
                host.hideFlags = HideFlags.HideAndDontSave;
                var runner = host.AddComponent<FuseVersionCheckRunner>();
                UnityEngine.Object.DontDestroyOnLoad(host);
                _host = host;
                return runner;
            }
            catch (Exception ex)
            {
                if (host != null)
                {
                    UnityEngine.Object.Destroy(host);
                }

                FuseLog.Exception("FUSE version-check host creation failed", ex);
                return null;
            }
        }

        // Parses the check result (no coroutine/yield here, so it can guard
        // everything in a try/catch) and records the outcome — unless a newer
        // check has superseded this one, in which case the result is discarded.
        private static void Evaluate(string currentVersion, string body, int generation)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(body))
                {
                    FuseLog.Info("FUSE update check: empty response from GitHub; skipping.");
                    return;
                }

                if (!FuseSemVer.TryParse(currentVersion, out var current))
                {
                    FuseLog.Info($"FUSE update check: running version '{currentVersion}' is not a comparable release version; skipping.");
                    return;
                }

                if (!FuseGitHubReleaseParser.TryParse(body, out var releases))
                {
                    FuseLog.Info("FUSE update check: could not parse the GitHub releases response; skipping.");
                    return;
                }

                if (!FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out var latestTag))
                {
                    FuseLog.Info("FUSE update check: no stable mod release found in the releases feed; skipping.");
                    return;
                }

                var outdated = FuseReleaseSelection.IsOutdated(current, latest);
                bool committed;
                lock (Gate)
                {
                    // Discard the result if a newer check started while this
                    // request was in flight — its response is the authority now.
                    if (!Generation.IsCurrent(generation))
                    {
                        committed = false;
                    }
                    else
                    {
                        _checkComplete = true;
                        _latestVersionText = latest.ToString();
                        _latestTag = latestTag;
                        _outdated = outdated;
                        _toastPending = outdated;
                        committed = true;
                    }
                }

                if (!committed)
                {
                    FuseLog.Info("FUSE update check: a newer check superseded this response; discarding it.");
                    return;
                }

                if (outdated)
                {
                    FuseLog.Warning(
                        $"FUSE is out of date: running {current}, latest stable is {latest} ({latestTag}). " +
                        $"Update from {ResolveUpdateUrl()}.");
                    // Shows now only if a map is already loaded (e.g. a manual
                    // re-check mid-session); otherwise it is a no-op and the next
                    // map load flushes it via NotifyMapDidLoad.
                    TryShowToast();
                }
                else
                {
                    FuseLog.Info($"FUSE is up to date: running {current}, latest stable is {latest}.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE update check failed while evaluating releases: {ex.GetBaseException().Message}");
            }
        }

        private static void TryShowToast()
        {
            bool present;
            string current, latest;
            FuseInstallChannel channel;
            lock (Gate)
            {
                present = _toastPending && !_toastShown && _outdated && _mapActive;
                current = _currentVersionText;
                latest = _latestVersionText;
                channel = _channel;
            }

            if (!present)
            {
                return;
            }

            try
            {
                var where = channel == FuseInstallChannel.Nexus ? "Nexus" : "GitHub";
                Toast.Present(
                    $"FUSE {latest} is available (you have {current}). " +
                    $"Update from {where} — open the FUSE window for the link.");
                lock (Gate)
                {
                    _toastShown = true;
                    _toastPending = false;
                }

                FuseLog.Info("FUSE update-available toast presented.");
            }
            catch (Exception ex)
            {
                // The toast surface may not be alive yet (splash / main menu).
                // Leave the toast pending so the next map load presents it.
                FuseLog.Info($"FUSE update toast deferred until UI is ready: {ex.GetBaseException().Message}");
            }
        }

        private sealed class FuseVersionCheckRunner : MonoBehaviour
        {
            internal void Begin(string currentVersion, int generation)
            {
                StartCoroutine(Run(currentVersion, generation));
            }

            // Static: the coroutine touches only static state (unlike a Unity
            // message such as Update, which must stay an instance method). Keeps
            // CA1822 quiet without a suppression.
            private static IEnumerator Run(string currentVersion, int generation)
            {
                // `using` compiles to try/finally (no catch), which is the only
                // shape C# allows a `yield return` to live inside. The request is
                // always disposed; the parse below runs after the yield with its
                // own try/catch.
                using (var request = UnityWebRequest.Get(ReleasesApiUrl))
                {
                    // GitHub rejects requests with no User-Agent (HTTP 403).
                    request.SetRequestHeader("User-Agent", "FUSE-Mod-UpdateCheck");
                    request.SetRequestHeader("Accept", "application/vnd.github+json");
                    request.timeout = 15;

                    yield return request.SendWebRequest();

                    if (request.isNetworkError || request.isHttpError)
                    {
                        FuseLog.Info(
                            $"FUSE update check skipped (could not reach GitHub): {request.error} " +
                            $"[HTTP {request.responseCode}].");
                    }
                    else
                    {
                        string body = null;
                        try
                        {
                            body = request.downloadHandler != null ? request.downloadHandler.text : null;
                        }
                        catch (Exception ex)
                        {
                            FuseLog.Warning($"FUSE update check could not read the GitHub response: {ex.GetBaseException().Message}");
                        }

                        Evaluate(currentVersion, body, generation);
                    }
                }
            }
        }
    }
}
