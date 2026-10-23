using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

class Job
{
    public int Id;
    public string Url;
    public string Name;
    public bool Transcript;
    public string State = "queued";   // queued | running | done | failed
    public string Status = "Waiting...";
    public int Progress = -1;         // -1 = indeterminate
    public string VideoPath;
    public string TranscriptPath;
}

class ZoomDownloader : Form
{
    static readonly string DefaultSaveDir = Directory.Exists(@"D:\")
        ? @"D:\HIT\Zoom Recordings"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HIT Recordings");
    static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string DataDir = Path.Combine(AppDir, "browser-data");
    static readonly string SettingsFile = Path.Combine(AppDir, "save-folder.txt");

    // The UI page lives in "ui"; Zoom itself is loaded in "zoom", which sits behind the UI
    // and is only brought to the front when the user has to sign in.
    WebView2 ui, zoom;
    Panel zoomPanel;
    Label zoomHint;
    Button zoomBack;

    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    readonly List<Job> jobs = new List<Job>();
    readonly Queue<Job> pending = new Queue<Job>();
    int nextId = 1;
    bool working;
    string saveDir;
    string lastClipboard;

    TaskCompletionSource<string> playInfoTcs;
    TaskCompletionSource<Dictionary<string, object>> fetchTcs;
    bool loginCancelled;

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new ZoomDownloader());
    }

    ZoomDownloader()
    {
        Text = "HIT Recordings";
        ClientSize = new Size(940, 720);
        MinimumSize = new Size(620, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(17, 19, 24);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        saveDir = LoadSaveDir();

        zoomPanel = new Panel { Dock = DockStyle.Fill };
        var bar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(37, 99, 235) };
        zoomHint = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0)
        };
        zoomBack = new Button
        {
            Dock = DockStyle.Right, Width = 150, Text = "Back to app", FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White, BackColor = Color.FromArgb(29, 78, 216), Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        zoomBack.FlatAppearance.BorderSize = 0;
        zoomBack.Click += delegate { if (working) loginCancelled = true; HideZoom(); };
        bar.Controls.Add(zoomHint);
        bar.Controls.Add(zoomBack);
        zoom = new WebView2 { Dock = DockStyle.Fill };
        zoomPanel.Controls.Add(zoom);
        zoomPanel.Controls.Add(bar);

        ui = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(zoomPanel);
        Controls.Add(ui);
        ui.BringToFront();

        Load += async delegate { await InitAsync(); };
        Activated += delegate { CheckClipboard(false); };
    }

    async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, DataDir);
            await ui.EnsureCoreWebView2Async(env);
            await zoom.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't start the built-in browser (Microsoft Edge WebView2).\n\n" + ex.Message, Text);
            Close();
            return;
        }

        var u = ui.CoreWebView2;
        u.Settings.AreDefaultContextMenusEnabled = false;
        u.Settings.IsZoomControlEnabled = false;
        u.SetVirtualHostNameToFolderMapping("app.local", AppDir, CoreWebView2HostResourceAccessKind.DenyCors);
        u.WebMessageReceived += OnUiMessage;
        u.Navigate("https://app.local/ui.html");

        var z = zoom.CoreWebView2;
        z.IsMuted = true;
        z.WebResourceResponseReceived += OnZoomResponse;
        z.WebMessageReceived += OnZoomMessage;
        z.NewWindowRequested += (s, e) => { e.Handled = true; z.Navigate(e.Uri); };
    }

    // ---------- UI messages ----------

    void Send(object message)
    {
        if (ui.CoreWebView2 != null) ui.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));
    }

    void SendJob(Job j)
    {
        Send(new
        {
            type = "job", id = j.Id, name = j.Name, url = j.Url, state = j.State, status = j.Status, progress = j.Progress,
            hasVideo = j.VideoPath != null, hasTranscript = j.TranscriptPath != null
        });
    }

    void Update(Job j, string state, string status, int progress)
    {
        j.State = state;
        j.Status = status;
        j.Progress = progress;
        SendJob(j);
    }

    void Log(Job j, string line)
    {
        Send(new { type = "log", id = j.Id, line = line });
    }

    void OnUiMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
        string type = Str(m, "type");
        Job job = FindJob(m);

        switch (type)
        {
            case "ready":
                Send(new { type = "settings", saveDir = saveDir });
                foreach (var j in jobs) SendJob(j);
                CheckClipboard(false);
                break;
            case "paste":
                CheckClipboard(true);
                break;
            case "add":
                string url = Str(m, "url").Trim();
                if (url.Length == 0) return;
                var nj = new Job { Id = nextId++, Url = url, Name = Str(m, "name").Trim(), Transcript = m.ContainsKey("transcript") && (bool)m["transcript"] };
                jobs.Add(nj);
                SendJob(nj);
                pending.Enqueue(nj);
                Pump();
                break;
            case "retry":
                if (job != null && job.State == "failed")
                {
                    Update(job, "queued", "Waiting...", -1);
                    pending.Enqueue(job);
                    Pump();
                }
                break;
            case "remove":
                if (job != null && (job.State == "done" || job.State == "failed")) jobs.Remove(job);
                break;
            case "chooseFolder":
                ChooseFolder();
                break;
            case "openFolder":
                Directory.CreateDirectory(saveDir);
                Process.Start("explorer.exe", "\"" + saveDir + "\"");
                break;
            case "openFile":
                string path = job == null ? null : (Str(m, "which") == "transcript" ? job.TranscriptPath : job.VideoPath);
                if (path != null && File.Exists(path)) Process.Start(path);
                break;
            case "showFile":
                string p = job == null ? null : (job.VideoPath ?? job.TranscriptPath);
                if (p != null && File.Exists(p)) Process.Start("explorer.exe", "/select,\"" + p + "\"");
                break;
        }
    }

    Job FindJob(Dictionary<string, object> m)
    {
        if (!m.ContainsKey("id") || m["id"] == null) return null;
        int id = Convert.ToInt32(m["id"]);
        return jobs.Find(j => j.Id == id);
    }

    void CheckClipboard(bool force)
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            string t = Clipboard.GetText().Trim();
            if (t.IndexOf("zoom.us/rec/", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (!force && t == lastClipboard) return;
            lastClipboard = t;
            Send(new { type = "clipboard", url = t, force = force });
        }
        catch { }
    }

    void ChooseFolder()
    {
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = "Choose where to save the recordings";
            if (Directory.Exists(saveDir)) dlg.SelectedPath = saveDir;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            saveDir = dlg.SelectedPath;
            try { File.WriteAllText(SettingsFile, saveDir, Encoding.UTF8); } catch { }
            Send(new { type = "settings", saveDir = saveDir });
        }
    }

    static string LoadSaveDir()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                string dir = File.ReadAllText(SettingsFile, Encoding.UTF8).Trim();
                if (dir.Length > 0) return dir;
            }
        }
        catch { }
        return DefaultSaveDir;
    }

    // ---------- Zoom browser ----------

    void ShowZoom(string hint)
    {
        zoomHint.Text = hint;
        zoomPanel.BringToFront();
    }

    void HideZoom()
    {
        ui.BringToFront();
    }

    async void OnZoomResponse(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var tcs = playInfoTcs;
        if (tcs == null || tcs.Task.IsCompleted) return;
        if (e.Request.Uri.IndexOf("/play/info", StringComparison.OrdinalIgnoreCase) < 0) return;
        try
        {
            var stream = await e.Response.GetContentAsync();
            if (stream == null) return;
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string body = reader.ReadToEnd();
                // Without a valid session Zoom answers this call too, just without the video URL.
                if (body.Contains("viewMp4Url")) tcs.TrySetResult(body);
            }
        }
        catch { }
    }

    void OnZoomMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var tcs = fetchTcs;
        if (tcs == null) return;
        try { tcs.TrySetResult(json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson)); } catch { }
    }

    // ---------- Jobs ----------

    async void Pump()
    {
        if (working) return;
        working = true;
        while (pending.Count > 0)
        {
            var job = pending.Dequeue();
            try { await RunJob(job); }
            catch (Exception ex) { Log(job, "Error: " + ex.Message); Update(job, "failed", "Something went wrong - see details.", 0); }
        }
        working = false;
        HideZoom();
        if (zoom.CoreWebView2 != null) zoom.CoreWebView2.Navigate("about:blank");
    }

    async Task RunJob(Job job)
    {
        Update(job, "running", "Opening the recording...", -1);
        Log(job, "Opening " + job.Url);

        // 1. Load the recording page in the built-in browser. If HIT/Zoom wants a sign-in, show it to the user.
        playInfoTcs = new TaskCompletionSource<string>();
        loginCancelled = false;
        zoom.CoreWebView2.Navigate(job.Url);
        var clock = Stopwatch.StartNew();
        bool shown = false;
        while (!playInfoTcs.Task.IsCompleted)
        {
            await Task.Delay(400);
            if (loginCancelled)
            {
                Update(job, "failed", "Cancelled - press Retry after signing in.", 0);
                return;
            }
            string src = zoom.Source == null ? "" : zoom.Source.ToString();
            bool onRecording = src.IndexOf("/rec/", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!shown && ((!onRecording && clock.Elapsed.TotalSeconds > 2) || clock.Elapsed.TotalSeconds > 15))
            {
                shown = true;
                ShowZoom("Sign in to HIT Zoom (only needed once). The download starts by itself right after.");
                Update(job, "running", "Waiting for you to sign in...", -1);
                Log(job, "Zoom needs a sign-in; showing the browser.");
            }
            if (clock.Elapsed.TotalMinutes > 10)
            {
                Update(job, "failed", "Timed out waiting for Zoom.", 0);
                return;
            }
        }
        HideZoom();

        var info = json.Deserialize<Dictionary<string, object>>(playInfoTcs.Task.Result);
        var result = info.ContainsKey("result") ? info["result"] as Dictionary<string, object> : null;
        string mp4Url = Str(result, "viewMp4Url");
        string transcriptUrl = Str(result, "transcriptUrl");
        var meet = result != null && result.ContainsKey("meet") ? result["meet"] as Dictionary<string, object> : null;
        string topic = Str(meet, "topic");

        string baseName = SafeName(job.Name.Length > 0 ? job.Name : (topic.Length > 0 ? topic : "Zoom recording"));
        Directory.CreateDirectory(saveDir);
        string name = baseName;
        for (int i = 2; File.Exists(Path.Combine(saveDir, name + ".mp4")) || File.Exists(TranscriptFile(name)); i++)
            name = baseName + " (" + i + ")";
        job.Name = name;
        Log(job, "Recording: " + (topic.Length > 0 ? topic : "(no title)") + " -> saving as \"" + name + "\"");

        // 2. Transcript: fetch it from inside the Zoom page so it uses the same session.
        if (job.Transcript)
        {
            if (transcriptUrl.Length == 0)
            {
                Log(job, "This recording has no audio transcript.");
            }
            else
            {
                Update(job, "running", "Getting the transcript...", -1);
                string vtt = await FetchInZoomPage(transcriptUrl);
                if (vtt != null && vtt.Contains("-->"))
                {
                    job.TranscriptPath = TranscriptFile(name);
                    File.WriteAllText(job.TranscriptPath, VttToText(vtt, name + " " + TranscriptSuffix), new UTF8Encoding(true));
                    Log(job, "Transcript saved: " + job.TranscriptPath);
                }
                else
                {
                    Log(job, "Couldn't download the transcript.");
                }
            }
        }

        // 3. Video: hand the browser's session to yt-dlp.
        string ytdlp = FindYtDlp();
        if (ytdlp == null)
        {
            Update(job, "running", "Installing yt-dlp (first time only)...", -1);
            await RunTool(job, "winget", "install --id yt-dlp.yt-dlp -e --silent --accept-source-agreements --accept-package-agreements");
            ytdlp = FindYtDlp();
        }
        if (ytdlp == null)
        {
            Log(job, "yt-dlp not found. Install it with: winget install yt-dlp.yt-dlp");
            Update(job, "failed", "yt-dlp is not installed.", 0);
            return;
        }
        string cookieFile = Path.Combine(DataDir, "zoom-cookies.txt");
        await WriteCookies(cookieFile);
        string agent = zoom.CoreWebView2.Settings.UserAgent;
        string pageUrl = zoom.Source.ToString();
        string referer = new Uri(pageUrl).GetLeftPart(UriPartial.Authority) + "/";
        string output = Path.Combine(saveDir, name.Replace("%", "%%") + ".%(ext)s");
        string common = "--cookies " + Q(cookieFile) + " --user-agent " + Q(agent) + " --referer " + Q(referer)
                      + " --encoding utf-8 --newline --no-mtime --no-playlist -o " + Q(output);

        Update(job, "running", "Downloading video...", 0);
        bool ok = false;
        if (mp4Url.Length > 0)
            ok = await RunTool(job, ytdlp, common + " " + Q(mp4Url));
        if (!ok)
        {
            Log(job, "Trying the Zoom page link instead...");
            ok = await RunTool(job, ytdlp, common + " " + Q(job.Url));
        }
        try { File.Delete(cookieFile); } catch { }

        if (ok)
        {
            foreach (string f in Directory.GetFiles(saveDir, name + ".*"))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".mp4" || ext == ".m4a" || ext == ".webm" || ext == ".mkv") job.VideoPath = f;
            }
            Update(job, "done", job.TranscriptPath != null ? "Video and transcript saved" : "Video saved", 100);
        }
        else
        {
            Update(job, "failed", job.TranscriptPath != null ? "Transcript saved, video failed" : "Download failed - see details.", 0);
        }
    }

    // "הרצאה 1" -> "הרצאה 1 תמלול.txt"
    const string TranscriptSuffix = "תמלול";

    string TranscriptFile(string name)
    {
        return Path.Combine(saveDir, name + " " + TranscriptSuffix + ".txt");
    }

    async Task<string> FetchInZoomPage(string url)
    {
        fetchTcs = new TaskCompletionSource<Dictionary<string, object>>();
        string script = "(async()=>{try{const r=await fetch(" + json.Serialize(url) + ",{credentials:'include'});"
                      + "const t=await r.text();chrome.webview.postMessage({type:'fetch',ok:r.ok,text:t});}"
                      + "catch(e){chrome.webview.postMessage({type:'fetch',ok:false,text:String(e)});}})();";
        await zoom.CoreWebView2.ExecuteScriptAsync(script);
        var done = await Task.WhenAny(fetchTcs.Task, Task.Delay(60000));
        if (done != fetchTcs.Task) return null;
        var m = fetchTcs.Task.Result;
        return m.ContainsKey("ok") && (bool)m["ok"] ? Str(m, "text") : null;
    }

    async Task WriteCookies(string file)
    {
        var cookies = await zoom.CoreWebView2.CookieManager.GetCookiesAsync(null);
        var sb = new StringBuilder("# Netscape HTTP Cookie File\n");
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var c in cookies)
        {
            if (c.Domain.IndexOf("zoom.us", StringComparison.OrdinalIgnoreCase) < 0) continue;
            DateTime expires = c.IsSession ? DateTime.UtcNow.AddDays(1) : c.Expires.ToUniversalTime();
            sb.Append(c.Domain).Append('\t')
              .Append(c.Domain.StartsWith(".") ? "TRUE" : "FALSE").Append('\t')
              .Append(c.Path).Append('\t')
              .Append(c.IsSecure ? "TRUE" : "FALSE").Append('\t')
              .Append((long)(expires - epoch).TotalSeconds).Append('\t')
              .Append(c.Name).Append('\t')
              .Append(c.Value).Append('\n');
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
    }

    Task<bool> RunTool(Job job, string exe, string args)
    {
        var tcs = new TaskCompletionSource<bool>();
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (s, e) => { if (e.Data != null) BeginInvoke((MethodInvoker)delegate { HandleLine(job, e.Data); }); };
        p.ErrorDataReceived += (s, e) => { if (e.Data != null) BeginInvoke((MethodInvoker)delegate { Log(job, e.Data); }); };
        p.Exited += delegate
        {
            p.WaitForExit();
            int code = p.ExitCode;
            p.Dispose();
            BeginInvoke((MethodInvoker)delegate { tcs.TrySetResult(code == 0); });
        };
        try
        {
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Log(job, "Couldn't start " + Path.GetFileName(exe) + ": " + ex.Message);
            tcs.TrySetResult(false);
        }
        return tcs.Task;
    }

    static readonly Regex ProgressLine = new Regex(@"^\[download\]\s+([\d.]+)%\s+of\s+~?\s*([\d.]+\S+)(?:.*?at\s+(\S+))?(?:.*?ETA\s+(\S+))?");

    void HandleLine(Job job, string line)
    {
        var m = ProgressLine.Match(line);
        if (m.Success)
        {
            double pct;
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out pct))
            {
                string status = "Downloading video - " + m.Groups[1].Value + "% of " + m.Groups[2].Value;
                if (m.Groups[3].Success && !m.Groups[3].Value.StartsWith("Unknown")) status += " - " + m.Groups[3].Value;
                if (m.Groups[4].Success && !m.Groups[4].Value.StartsWith("Unknown")) status += " - " + m.Groups[4].Value + " left";
                Update(job, "running", status, (int)pct);
                if (pct < 100) return;
            }
        }
        Log(job, line);
    }

    // ---------- Helpers ----------

    static string VttToText(string vtt, string title)
    {
        // Zoom transcripts are WebVTT cues of "Speaker: text"; merge them into readable paragraphs
        // that start a new line when the speaker changes or a minute has passed.
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

    static string FindYtDlp()
    {
        string local = Path.Combine(AppDir, "yt-dlp.exe");
        if (File.Exists(local)) return local;

        string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages");
        if (Directory.Exists(packages))
        {
            foreach (string dir in Directory.GetDirectories(packages, "yt-dlp.yt-dlp_*"))
            {
                string exe = Path.Combine(dir, "yt-dlp.exe");
                if (File.Exists(exe)) return exe;
            }
        }

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string exe = Path.Combine(dir.Trim(), "yt-dlp.exe");
                if (File.Exists(exe)) return exe;
            }
            catch { }
        }
        return null;
    }

    static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length > 0 ? name : "Zoom recording";
    }

    static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
    }

    static string Q(string s)
    {
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
