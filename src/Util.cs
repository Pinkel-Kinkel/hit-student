using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Small helpers shared by the rest of the app (C# 5: no "using static", so they live on the form class).
partial class HitStudent
{
    public static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
    }

    public static bool Bool(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v;
    }

    public static int Int(Dictionary<string, object> d, string key)
    {
        object v;
        int n;
        return d != null && d.TryGetValue(key, out v) && v != null && int.TryParse(v.ToString(), out n) ? n : 0;
    }

    public static Dictionary<string, object> Dict(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
    }

    // Quotes one command-line argument the way CommandLineToArgvW reads it back.
    public static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in s ?? "")
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { sb.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
            sb.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }

    public static string SafeName(string name)
    {
        name = name ?? "";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = Regex.Replace(name, @"\s+", " ").Trim().Trim('.').Trim();
        if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$", RegexOptions.IgnoreCase)) name = "_" + name;
        if (name.Length > 120) name = name.Substring(0, 120).Trim();
        return name.Length > 0 ? name : "Untitled";
    }

    public static string Trunc(string s, int max)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    public static bool IsInside(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
        string full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        string r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        return full.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return "";
        byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public static string Unprotect(string blob)
    {
        try
        {
            if (string.IsNullOrEmpty(blob)) return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(blob), null, DataProtectionScope.CurrentUser));
        }
        catch { return ""; }
    }

    // Runs a hidden process and returns its exit code and output.
    public static Task<Tuple<int, string>> Exec(string exe, string args, string cwd = null, int timeoutMs = 600000, Dictionary<string, string> env = null)
    {
        var tcs = new TaskCompletionSource<Tuple<int, string>>();
        var output = new StringBuilder();
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            if (cwd != null) psi.WorkingDirectory = cwd;
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.Exited += delegate
            {
                p.WaitForExit();
                int code = p.ExitCode;
                p.Dispose();
                lock (output) tcs.TrySetResult(Tuple.Create(code, output.ToString()));
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            Task.Delay(timeoutMs).ContinueWith(_ =>
            {
                if (tcs.Task.IsCompleted) return;
                try { p.Kill(); } catch { }
                lock (output) tcs.TrySetResult(Tuple.Create(-2, output.ToString()));
            });
        }
        catch (Exception ex) { tcs.TrySetResult(Tuple.Create(-1, ex.Message)); }
        return tcs.Task;
    }

    public static void KillTree(Process p)
    {
        try { Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { CreateNoWindow = true, UseShellExecute = false }); } catch { }
    }

    public static string FindOnPath(params string[] names)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            foreach (string n in names)
            {
                try
                {
                    string exe = Path.Combine(dir.Trim(), n);
                    if (File.Exists(exe)) return exe;
                }
                catch { }
            }
        }
        return null;
    }

    // Normalises a name for loose matching ("אינפי 2 - תשפ\"ו" ~ "Infi 2").
    public static string Loose(string s)
    {
        s = Regex.Replace((s ?? "").ToLowerInvariant(), "[\"'`׳״()\\[\\]{}.,:;_\\-–]", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    // Zoom transcripts are WebVTT cues of "Speaker: text"; merge them into readable paragraphs
    // that start a new line when the speaker changes or a minute has passed.
    public static string VttToText(string vtt, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(new string('=', Math.Min(60, Math.Max(10, title.Length))));
        string speaker = null;
        double paragraphStart = -1000;
        string[] lines = vtt.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int arrow = lines[i].IndexOf("-->");
            if (arrow < 0) continue;
            string stamp = lines[i].Substring(0, arrow).Trim();
            var text = new StringBuilder();
            while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0)
                text.Append(text.Length > 0 ? " " : "").Append(lines[++i].Trim());
            if (text.Length == 0) continue;

            string cue = text.ToString();
            string who = null;
            var sm = Regex.Match(cue, @"^([^:]{1,40}):\s+(.*)$");
            if (sm.Success) { who = sm.Groups[1].Value.Trim(); cue = sm.Groups[2].Value; }

            double seconds = StampSeconds(stamp);
            if (who != speaker || seconds - paragraphStart >= 60)
            {
                sb.AppendLine().AppendLine();
                sb.Append('[').Append(stamp.Split('.')[0]).Append("] ");
                if (who != null) sb.Append(who).Append(": ");
                speaker = who;
                paragraphStart = seconds;
            }
            else sb.Append(' ');
            sb.Append(cue);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    static double StampSeconds(string stamp)
    {
        double total = 0;
        foreach (string part in stamp.Split(':'))
        {
            double v;
            double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            total = total * 60 + v;
        }
        return total;
    }
}
