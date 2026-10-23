using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

partial class HitStudent : Form
{
    public const string Version = "2.0.0";
    const string Repo = "Pinkel-Kinkel/hit-recordings";

    static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string WebDir = Path.Combine(AppDir, "web");
    static readonly string DataDir = Path.Combine(AppDir, "browser-data");
    static readonly string SettingsFile = Path.Combine(AppDir, "settings.json");
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static readonly Dictionary<string, string> Sites = new Dictionary<string, string>
    {
        { "moodle", "https://md.hit.ac.il/my/" },
        { "portal", "https://portal.hit.ac.il/" },
        { "cs24", "https://cs24.co.il/drive?d=e47d12" }
    };

    // "ui" is the app itself. "zoom" and "moodle" are hidden browsers that share one sign-in profile and only
    // come to the front when HIT asks the user to sign in. "site" shows Moodle / the portal / CS24 inside the app.
    WebView2 ui, zoom, moodle, site;
    Panel zoomPanel, moodlePanel, sitePanel;
    CoreWebView2Environment env;

    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    readonly Dictionary<string, object> settings = new Dictionary<string, object>();
    readonly bool demo, selfTest;
    readonly string selfTestOut;
    string lastClipboard;
    string siteDownloadCourse, siteDownloadFolder;
    readonly Timer statusTimer = new Timer { Interval = 45000 };

    [STAThread]
    static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new HitStudent(args));
    }

    HitStudent(string[] args)
    {
        demo = args.Contains("--demo");
        selfTest = args.Contains("--selftest");
        int i = Array.IndexOf(args, "--selftest");
        selfTestOut = selfTest && i + 1 < args.Length ? args[i + 1] : Path.Combine(AppDir, "selftest.json");

        Text = "HIT Student";
        ClientSize = new Size(1240, 820);
        MinimumSize = new Size(820, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(14, 16, 22);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        LoadSettings();

        zoom = new WebView2 { Dock = DockStyle.Fill };
        zoomPanel = LoginPanel(zoom, "\u05D4\u05EA\u05D7\u05D1\u05E8 \u05DC-Zoom \u05E2\u05DD \u05D4\u05DE\u05E9\u05EA\u05DE\u05E9 \u05E9\u05DC HIT. \u05D6\u05D4 \u05E7\u05D5\u05E8\u05D4 \u05E4\u05E2\u05DD \u05D0\u05D7\u05EA, \u05D5\u05D4\u05D4\u05D5\u05E8\u05D3\u05D4 \u05EA\u05DE\u05E9\u05D9\u05DA \u05DC\u05D1\u05D3.",
            delegate { if (working) zoomCancelled = true; ui.BringToFront(); });
        moodle = new WebView2 { Dock = DockStyle.Fill };
        moodlePanel = LoginPanel(moodle, "\u05D4\u05EA\u05D7\u05D1\u05E8 \u05E2\u05DD \u05E9\u05DD \u05D4\u05DE\u05E9\u05EA\u05DE\u05E9 \u05D5\u05D4\u05E1\u05D9\u05E1\u05DE\u05D4 \u05E9\u05DC HIT. \u05D6\u05D4 \u05E7\u05D5\u05E8\u05D4 \u05E4\u05E2\u05DD \u05D0\u05D7\u05EA \u05D1\u05DC\u05D1\u05D3.",
            delegate { moodleCancelled = true; ui.BringToFront(); });
        site = new WebView2 { Dock = DockStyle.Fill };
        sitePanel = new Panel { Visible = false };
        sitePanel.Controls.Add(site);

        ui = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(zoomPanel);
        Controls.Add(moodlePanel);
        Controls.Add(ui);
        Controls.Add(sitePanel);
        ui.BringToFront();

        if (selfTest) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; }
        Load += async delegate { await InitAsync(); };
        Activated += delegate { CheckClipboard(false); };
        FormClosing += delegate { foreach (var p in aiRuns.Values.ToList()) KillTree(p); };
        statusTimer.Tick += async delegate { await PollStatus(false); };
    }

    Panel LoginPanel(WebView2 view, string hint, EventHandler back)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var bar = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(79, 70, 229) };
        var label = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f), Text = hint,
            TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 18, 0), RightToLeft = RightToLeft.Yes
        };
        var button = new Button
        {
            Dock = DockStyle.Left, Width = 170, Text = "\u05D7\u05D6\u05E8\u05D4 \u05DC\u05D0\u05E4\u05DC\u05D9\u05E7\u05E6\u05D9\u05D4", FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White, BackColor = Color.FromArgb(67, 56, 202), Font = new Font("Segoe UI", 10f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += back;
        bar.Controls.Add(label);
        bar.Controls.Add(button);
        panel.Controls.Add(view);
        panel.Controls.Add(bar);
        return panel;
    }

    async Task InitAsync()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions { AllowSingleSignOnUsingOSPrimaryAccount = false };
            env = await CoreWebView2Environment.CreateAsync(null, DataDir, options);
            await ui.EnsureCoreWebView2Async(env);
            await zoom.EnsureCoreWebView2Async(env);
            await moodle.EnsureCoreWebView2Async(env);
            await site.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            if (!selfTest) MessageBox.Show(this, "Couldn't start the built-in browser (Microsoft Edge WebView2).\n\n" + ex.Message, Text);
            Close();
            return;
        }

        // The app page only ever shows files from the "web" folder and can't be navigated anywhere else.
        var u = ui.CoreWebView2;
        u.Settings.AreDefaultContextMenusEnabled = false;
        u.Settings.IsZoomControlEnabled = false;
        u.Settings.AreDevToolsEnabled = demo;
        u.Settings.IsStatusBarEnabled = false;
        u.Settings.AreBrowserAcceleratorKeysEnabled = false;
        u.SetVirtualHostNameToFolderMapping("app.local", WebDir, CoreWebView2HostResourceAccessKind.DenyCors);
        u.WebMessageReceived += OnUiMessage;
        u.NavigationStarting += (s, e) => { if (!e.Uri.StartsWith("https://app.local/", StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; OpenExternal(e.Uri); } };
        u.NewWindowRequested += (s, e) => { e.Handled = true; OpenExternal(e.Uri); };
        string demoArgs = "";
        foreach (string a in Environment.GetCommandLineArgs())
            if (a.StartsWith("--demo-")) demoArgs += "&" + Uri.EscapeDataString(a.Substring(7).Split('=')[0]) + "=" + Uri.EscapeDataString(a.Contains("=") ? a.Split('=')[1] : "1");
        u.Navigate("https://app.local/ui.html" + (demo ? "?demo=1" + demoArgs : ""));

        foreach (var view in new[] { zoom, moodle })
        {
            var core = view.CoreWebView2;
            core.IsMuted = true;
            core.Settings.AreDevToolsEnabled = false;
            core.WebMessageReceived += OnEvalMessage;
            core.NewWindowRequested += (s, e) => { e.Handled = true; core.Navigate(e.Uri); };
        }
        zoom.CoreWebView2.WebResourceResponseReceived += OnZoomResponse;

        var sc = site.CoreWebView2;
        sc.Settings.AreDevToolsEnabled = false;
        sc.NewWindowRequested += (s, e) => { e.Handled = true; sc.Navigate(e.Uri); };
        sc.DownloadStarting += OnSiteDownload;
        sc.SourceChanged += delegate { Send(new { type = "siteNav", url = sc.Source, canBack = sc.CanGoBack, canForward = sc.CanGoForward }); };
        sc.NavigationStarting += delegate { Send(new { type = "siteLoading", loading = true }); };
        sc.NavigationCompleted += delegate { Send(new { type = "siteLoading", loading = false }); };

        InstallSkill();
        if (demo) return;
        if (selfTest) { await RunSelfTest(); return; }
        statusTimer.Start();
        CheckForUpdate();
    }

    // ---------- Settings ----------

    void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
                foreach (var kv in json.Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsFile, Encoding.UTF8)))
                    settings[kv.Key] = kv.Value;
        }
        catch { }
        bool hasD = Directory.Exists(@"D:\");
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Str(settings, "studyRoot").Length == 0)
            settings["studyRoot"] = hasD ? @"D:\HIT\Courses" : Path.Combine(docs, "HIT Courses");
        if (Str(settings, "recordingsDir").Length == 0)
        {
            // Carry over the folder chosen in the old HIT Recordings app.
            string old = Path.Combine(AppDir, "save-folder.txt");
            string dir = File.Exists(old) ? File.ReadAllText(old, Encoding.UTF8).Trim() : "";
            settings["recordingsDir"] = dir.Length > 0 ? dir : (hasD ? @"D:\HIT\Zoom Recordings" : Path.Combine(docs, "HIT Recordings"));
        }
        if (Str(settings, "aiProvider").Length == 0) settings["aiProvider"] = "claude";
    }

    void SaveSettings()
    {
        try { File.WriteAllText(SettingsFile, json.Serialize(settings), Encoding.UTF8); } catch { }
    }

    string StudyRoot { get { return Str(settings, "studyRoot"); } }
    string RecordingsDir { get { return Str(settings, "recordingsDir"); } }
    string NotabilityDir { get { return Str(settings, "notabilityDir"); } }
    string Provider { get { return Str(settings, "aiProvider") == "gemini" ? "gemini" : "claude"; } }

    void SendSettings()
    {
        Send(new
        {
            type = "settings", studyRoot = StudyRoot, recordingsDir = RecordingsDir, notabilityDir = NotabilityDir,
            aiProvider = Provider, hasGeminiKey = Str(settings, "geminiKey").Length > 0,
            onboarded = Bool(settings, "onboarded"), userName = Str(settings, "userName"), version = Version
        });
    }

    // ---------- UI messages ----------

    void Send(object message)
    {
        if (ui.CoreWebView2 != null) ui.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));
    }

    void Toast(string text, bool error) { Send(new { type = "toast", text = text, error = error }); }

    void OnUiMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith("https://app.local/", StringComparison.OrdinalIgnoreCase)) return;
        Dictionary<string, object> m;
        try { m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); }
        catch { return; }
        if (demo) return;
        try { HandleUiMessage(m); }
        catch (Exception ex) { Toast(ex.Message, true); }
    }

    void HandleUiMessage(Dictionary<string, object> m)
    {
        string type = Str(m, "type");
        string course = Str(m, "course");
        switch (type)
        {
            case "ready":
                SendSettings();
                foreach (var j in jobs) SendJob(j);
                CheckClipboard(false);
                var ignored = PollStatus(true);
                break;
            case "finishOnboarding":
                settings["onboarded"] = true;
                SaveSettings();
                SendSettings();
                break;
            case "checkStatus": var ignored2 = PollStatus(true); break;

            // AI
            case "setProvider":
                settings["aiProvider"] = Str(m, "provider") == "gemini" ? "gemini" : "claude";
                SaveSettings();
                SendSettings();
                var ignored3 = PollStatus(true);
                break;
            case "setupClaude": SetupClaude(); break;
            case "setupGemini": SetupGemini(); break;
            case "setGeminiKey":
                settings["geminiKey"] = Protect(Str(m, "key").Trim());
                SaveSettings();
                SendSettings();
                var ignored4 = PollStatus(true);
                break;
            case "openGeminiKeyPage": OpenExternal("https://aistudio.google.com/apikey"); break;

            // Moodle
            case "connectMoodle": ConnectMoodle(); break;
            case "loadCourses": LoadCourses(); break;
            case "loadHome": LoadHome(); break;
            case "openCourse": OpenCourse(Int(m, "id"), course); break;
            case "setLecturer":
                var meta = LoadMeta(course);
                meta["lecturer"] = Str(m, "name").Trim();
                SaveMeta(course, meta);
                break;
            case "syncSection": SyncSection(course, m["section"] as Dictionary<string, object>); break;
            case "syncCourse": SyncCourse(course, m["sections"] as System.Collections.ArrayList); break;
            case "addFiles": AddFiles(course, Str(m, "folder")); break;

            // AI tasks
            case "summarize": Summarize(course, Int(m, "num"), Str(m, "title")); break;
            case "analyzeCourse": AnalyzeCourse(course); break;
            case "examPrep": ExamPrep(course); break;
            case "chat": Chat(course, Str(m, "text")); break;
            case "chatReset":
                var meta2 = LoadMeta(course);
                meta2.Remove("chatSession");
                meta2.Remove("chatHistory");
                SaveMeta(course, meta2);
                break;
            case "cancelAi":
                System.Diagnostics.Process run;
                if (aiRuns.TryGetValue(Str(m, "key"), out run)) KillTree(run);
                break;

            // Recordings
            case "paste": CheckClipboard(true); break;
            case "add":
                string url = Str(m, "url").Trim();
                if (url.Length == 0) return;
                AddJob(new Job { Url = url, Name = Str(m, "name").Trim(), Transcript = Bool(m, "transcript") });
                break;
            case "retry": RetryJob(FindJob(m)); break;
            case "remove":
                var rj = FindJob(m);
                if (rj != null && (rj.State == "done" || rj.State == "failed")) jobs.Remove(rj);
                break;
            case "openFile":
                var oj = FindJob(m);
                string jp = oj == null ? null : (Str(m, "which") == "transcript" ? oj.TranscriptPath : oj.VideoPath);
                if (jp != null) OpenLocal(jp);
                break;
            case "showFile":
                var sj = FindJob(m);
                string sp = sj == null ? null : (sj.VideoPath ?? sj.TranscriptPath);
                if (sp != null && File.Exists(sp)) Process.Start("explorer.exe", "/select," + Q(sp));
                break;

            // Folders
            case "chooseFolder": ChooseFolder(Str(m, "which")); break;
            case "openFolder":
                string which = Str(m, "which");
                string folder = which == "study" ? StudyRoot : which == "notability" ? NotabilityDir : RecordingsDir;
                if (folder.Length == 0) return;
                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", Q(folder));
                break;
            case "openPath": OpenLocal(Str(m, "path")); break;
            case "openLink": OpenExternal(Str(m, "url")); break;

            // Notability
            case "linkNotability": LinkNotability(course, Str(m, "folder")); break;

            // Sites inside the app
            case "showSite": ShowSite(m); break;
            case "placeSite": PlaceSite(m); break;
            case "hideSite": sitePanel.Visible = false; break;
            case "siteAction": SiteAction(Str(m, "action")); break;
            case "setDownloadTarget":
                siteDownloadCourse = course.Length > 0 ? course : null;
                siteDownloadFolder = Str(m, "folder");
                break;
        }
    }

    void CheckClipboard(bool force)
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            string t = Clipboard.GetText().Trim();
            if (t.IndexOf("zoom.us/rec/", StringComparison.OrdinalIgnoreCase) < 0 || t.Length > 2000) return;
            if (!force && t == lastClipboard) return;
            lastClipboard = t;
            Send(new { type = "clipboard", url = t, force = force });
        }
        catch { }
    }

    void ChooseFolder(string which)
    {
        string key = which == "study" ? "studyRoot" : which == "notability" ? "notabilityDir" : "recordingsDir";
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = which == "study" ? "\u05D1\u05D7\u05E8 \u05D0\u05EA \u05EA\u05D9\u05E7\u05D9\u05D9\u05EA \u05D4\u05E7\u05D5\u05E8\u05E1\u05D9\u05DD"
                : which == "notability" ? "\u05D1\u05D7\u05E8 \u05D0\u05EA \u05EA\u05D9\u05E7\u05D9\u05D9\u05EA \u05D4\u05D2\u05D9\u05D1\u05D5\u05D9 \u05E9\u05DC Notability"
                : "\u05D1\u05D7\u05E8 \u05D0\u05D9\u05E4\u05D4 \u05DC\u05E9\u05DE\u05D5\u05E8 \u05D4\u05E7\u05DC\u05D8\u05D5\u05EA";
            string current = Str(settings, key);
            if (Directory.Exists(current)) dlg.SelectedPath = current;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            settings[key] = dlg.SelectedPath;
            SaveSettings();
            SendSettings();
            if (which == "notability") SendNotabilityFolders();
        }
    }

    static readonly HashSet<string> SafeToOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".html", ".htm", ".txt", ".md", ".mp4", ".m4a", ".mp3", ".docx", ".doc", ".pptx", ".ppt", ".xlsx", ".xls",
        ".png", ".jpg", ".jpeg", ".gif", ".csv", ".ipynb", ".py", ".c", ".cpp", ".h", ".java", ".m", ".zip", ".url"
    };

    void OpenLocal(string path)
    {
        // Only things inside the app's own folders. Anything that could run (an .exe from Moodle, a script)
        // is shown in Explorer instead of being opened.
        if (string.IsNullOrEmpty(path)) return;
        string full = Path.GetFullPath(path);
        bool inside = IsInside(full, StudyRoot) || IsInside(full, RecordingsDir) || (NotabilityDir.Length > 0 && IsInside(full, NotabilityDir));
        if (!inside) return;
        if (Directory.Exists(full)) Process.Start("explorer.exe", Q(full));
        else if (File.Exists(full))
        {
            if (SafeToOpen.Contains(Path.GetExtension(full))) Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
            else Process.Start("explorer.exe", "/select," + Q(full));
        }
    }

    static void OpenExternal(string url)
    {
        Uri u;
        if (Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    // ---------- Status ----------

    bool polling;

    async Task PollStatus(bool full)
    {
        if (polling) return;
        polling = true;
        try
        {
            var ai = await AiStatus();
            Send(new { type = "aiStatus", status = ai });
            string ms = await MoodleHeartbeat();
            Send(new { type = "moodle", state = ms, message = "" });
            if (full && ms == "ready") LoadHome();
            ImportAllNotability();
        }
        catch { }
        finally { polling = false; }
    }

    async void CheckForUpdate()
    {
        try
        {
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "HIT-Student/" + Version);
                string body = await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest");
                var rel = json.Deserialize<Dictionary<string, object>>(body);
                string tag = Str(rel, "tag_name").TrimStart('v');
                System.Version latest, mine;
                if (System.Version.TryParse(tag, out latest) && System.Version.TryParse(Version, out mine) && latest > mine)
                    Send(new { type = "update", version = tag, url = Str(rel, "html_url") });
            }
        }
        catch { }
    }

    // ---------- Sites inside the app ----------

    double siteScale = 1;

    void ShowSite(Dictionary<string, object> m)
    {
        string key = Str(m, "site");
        string url;
        if (!Sites.TryGetValue(key, out url)) return;
        PlaceSite(m);
        string current = site.Source == null ? "" : site.Source.ToString();
        string host = new Uri(url).Host;
        string deep = Str(m, "url");
        if (key == "moodle" && IsMoodleUrl(deep)) site.CoreWebView2.Navigate(deep);   // e.g. a deadline from the home page
        else if (current.IndexOf(host, StringComparison.OrdinalIgnoreCase) < 0) site.CoreWebView2.Navigate(url);
        sitePanel.Visible = true;
        sitePanel.BringToFront();
    }

    void PlaceSite(Dictionary<string, object> m)
    {
        // The page reports the box (in CSS pixels) where the site should appear; scale it to window pixels.
        double vw = Convert.ToDouble(m["vw"]);
        if (vw > 0) siteScale = ui.ClientSize.Width / vw;
        int x = (int)Math.Round(Convert.ToDouble(m["x"]) * siteScale);
        int y = (int)Math.Round(Convert.ToDouble(m["y"]) * siteScale);
        int w = (int)Math.Round(Convert.ToDouble(m["w"]) * siteScale);
        int h = (int)Math.Round(Convert.ToDouble(m["h"]) * siteScale);
        sitePanel.Bounds = new Rectangle(x, y, Math.Max(10, w), Math.Max(10, h));
    }

    void SiteAction(string action)
    {
        var c = site.CoreWebView2;
        if (c == null) return;
        if (action == "back" && c.CanGoBack) c.GoBack();
        else if (action == "forward" && c.CanGoForward) c.GoForward();
        else if (action == "reload") c.Reload();
        else if (action == "external" && site.Source != null) OpenExternal(site.Source.ToString());
    }

    void OnSiteDownload(object sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // Files downloaded from Moodle / CS24 inside the app go straight into the chosen course folder.
        string name = SafeName(Path.GetFileName(e.ResultFilePath));
        string dir = siteDownloadCourse != null
            ? Path.Combine(CourseDir(siteDownloadCourse), SafeName(siteDownloadFolder.Length > 0 ? siteDownloadFolder : Exams))
            : Path.Combine(StudyRoot, "\u05D4\u05D5\u05E8\u05D3\u05D5\u05EA"); // הורדות
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + " (" + i + ")" + Path.GetExtension(name));
        e.ResultFilePath = path;
        e.Handled = true;
        var op = e.DownloadOperation;
        Send(new { type = "siteDownload", name = Path.GetFileName(path), dir = dir, state = "started" });
        op.StateChanged += delegate
        {
            if (op.State == CoreWebView2DownloadState.Completed)
                Send(new { type = "siteDownload", name = Path.GetFileName(path), dir = dir, path = path, state = "done" });
            else if (op.State == CoreWebView2DownloadState.Interrupted)
                Send(new { type = "siteDownload", name = Path.GetFileName(path), dir = dir, state = "failed" });
        };
    }
}
