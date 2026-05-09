using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ASCOM.OnStepX.Hardware.Transport;

namespace ASCOM.OnStepX.Services
{
    // Polls https://github.com/MichaelLevAstro/OnStepXAscomDriver releases and
    // chains a silent Inno installer + hub relaunch when the user accepts.
    //
    // Lifecycle:
    //   CheckLatest    → fetch /releases/latest, parse, version compare
    //   DownloadAsync  → stream installer asset to %TEMP% with progress
    //   LaunchInstaller→ write %TEMP% bridge .cmd, start it hidden, shut down hub
    //
    // No throw to caller — failures return null / false and log via TransportLogger.
    internal static class UpdateService
    {
        private const string OwnerRepo  = "MichaelLevAstro/OnStepXAscomDriver";
        private const string LatestUrl  = "https://api.github.com/repos/" + OwnerRepo + "/releases/latest";
        private const string AssetGlob  = "OnStepX-Setup-"; // prefix; suffix is "{version}.exe"

        private static readonly HttpClient _http = CreateClient();

        public static Version Current
        {
            get
            {
                try { return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0); }
                catch { return new Version(0, 0, 0); }
            }
        }

        // Snapshot returned by CheckLatest. Null = no usable release.
        public sealed class UpdateInfo
        {
            public Version Latest;
            public string  TagName;     // raw tag, e.g. "v0.6.0"
            public string  DisplayName; // release "name" field, falls back to TagName
            public string  Body;        // release notes (markdown)
            public string  HtmlUrl;     // release page on github.com
            public string  AssetUrl;    // direct installer download (may be null)
            public string  AssetName;
            public bool    IsNewerThanCurrent;
        }

        public static async Task<UpdateInfo> CheckLatestAsync(CancellationToken ct)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, LatestUrl))
                {
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                    using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            TransportLogger.Note("Update check HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                            return null;
                        }
                        using (var s = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            var ser = new DataContractJsonSerializer(typeof(GitHubReleaseDto));
                            var dto = ser.ReadObject(s) as GitHubReleaseDto;
                            return BuildInfo(dto);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                TransportLogger.Note("Update check failed: " + ex.Message);
                return null;
            }
        }

        private static UpdateInfo BuildInfo(GitHubReleaseDto dto)
        {
            if (dto == null) return null;
            if (dto.draft || dto.prerelease) return null; // /releases/latest already filters; belt + suspenders

            if (!TryParseTag(dto.tag_name, out var latest)) return null;

            string assetUrl = null, assetName = null;
            if (dto.assets != null)
            {
                foreach (var a in dto.assets)
                {
                    if (a == null || string.IsNullOrEmpty(a.name)) continue;
                    if (a.name.StartsWith(AssetGlob, StringComparison.OrdinalIgnoreCase)
                        && a.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = a.browser_download_url;
                        assetName = a.name;
                        break;
                    }
                }
            }

            return new UpdateInfo
            {
                Latest = latest,
                TagName = dto.tag_name ?? "",
                DisplayName = string.IsNullOrEmpty(dto.name) ? (dto.tag_name ?? "") : dto.name,
                Body = dto.body ?? "",
                HtmlUrl = dto.html_url ?? ("https://github.com/" + OwnerRepo + "/releases/latest"),
                AssetUrl = assetUrl,
                AssetName = assetName,
                IsNewerThanCurrent = latest > NormalizeForCompare(Current),
            };
        }

        // Tag form expected: "v1.2.3" or "1.2.3" (optionally trailing ".4"). Anything else → reject.
        private static bool TryParseTag(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag)) return false;
            var t = tag.Trim();
            if (t.Length > 0 && (t[0] == 'v' || t[0] == 'V')) t = t.Substring(1);
            return Version.TryParse(t, out version);
        }

        // Drop revision so a 0.5.0.0 assembly compares cleanly against a 0.5.0 tag.
        private static Version NormalizeForCompare(Version v)
        {
            if (v == null) return new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        // Stream install asset to %TEMP%, reporting 0..100 to the progress callback.
        // Returns the local path on success, null on failure.
        public static async Task<string> DownloadInstallerAsync(string url, string assetName, Action<int> progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string dest = Path.Combine(Path.GetTempPath(), string.IsNullOrEmpty(assetName) ? "OnStepX-Setup.exe" : SanitizeFileName(assetName));
            try
            {
                using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        TransportLogger.Note("Update download HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                        return null;
                    }
                    long? total = resp.Content.Headers.ContentLength;
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        var buf = new byte[81920];
                        long copied = 0;
                        int lastReported = -1;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            copied += n;
                            if (progress != null && total.HasValue && total.Value > 0)
                            {
                                int pct = (int)((copied * 100L) / total.Value);
                                if (pct != lastReported)
                                {
                                    lastReported = pct;
                                    try { progress(pct); } catch { }
                                }
                            }
                        }
                        if (progress != null) try { progress(100); } catch { }
                    }
                }
                return dest;
            }
            catch (OperationCanceledException)
            {
                TryDelete(dest);
                throw;
            }
            catch (Exception ex)
            {
                TransportLogger.Note("Update download failed: " + ex.Message);
                TryDelete(dest);
                return null;
            }
        }

        // Writes a bridge .cmd that runs the silent Inno install and then relaunches
        // this hub from its current install path. Returns true on successful spawn —
        // caller is expected to immediately Application.Current.Shutdown().
        public static bool LaunchInstallerAndScheduleRestart(string installerPath)
        {
            try
            {
                if (string.IsNullOrEmpty(installerPath) || !File.Exists(installerPath))
                {
                    TransportLogger.Note("Update install: installer path missing");
                    return false;
                }

                string hubExe = null;
                try { hubExe = Process.GetCurrentProcess().MainModule?.FileName; } catch { }
                if (string.IsNullOrEmpty(hubExe))
                    hubExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OnStepX.Hub.exe");

                string cmdPath = Path.Combine(Path.GetTempPath(), "onstepx-update.cmd");
                string script =
                    "@echo off\r\n" +
                    "rem OnStepX auto-update bridge — generated " + DateTime.Now.ToString("u") + "\r\n" +
                    "\"" + installerPath + "\" /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS\r\n" +
                    "start \"\" \"" + hubExe + "\"\r\n";
                File.WriteAllText(cmdPath, script, new UTF8Encoding(false));

                var psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmdPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                    WindowStyle     = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath(),
                };
                Process.Start(psi);
                TransportLogger.Note("Update install scheduled: " + installerPath);
                return true;
            }
            catch (Exception ex)
            {
                TransportLogger.Note("Update install spawn failed: " + ex.Message);
                return false;
            }
        }

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            // GitHub API requires a User-Agent.
            string ver;
            try { ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"; }
            catch { ver = "0.0.0"; }
            c.DefaultRequestHeaders.UserAgent.ParseAdd("OnStepX-Hub/" + ver);
            return c;
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name) sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            return sb.ToString();
        }
    }
}
