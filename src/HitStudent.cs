using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
    public string Dir;                // null = the recordings folder from settings
    public bool Transcript;
    public string State = "queued";   // queued | running | done | failed
    public string Status = "ממתין...";
    public int Progress = -1;         // -1 = indeterminate
    public string VideoPath;
    public string TranscriptPath;
    public Action<Job> Finished;
}

class HitStudent : Form
{
    const string MoodleBase = "https://md.hit.ac.il";
    const string SkillName = "course-study-builder";
    const string Exercises = "\u05EA\u05E8\u05D2\u05D9\u05DC\u05D9\u05DD";          // תרגילים
    const string RecordingFolder = "\u05D4\u05E7\u05DC\u05D8\u05D4";               // הקלטה
    const string StudyPages = "\u05D3\u05E4\u05D9 \u05DC\u05D9\u05DE\u05D5\u05D3"; // דפי לימוד
    const string TranscriptSuffix = "\u05EA\u05DE\u05DC\u05D5\u05DC";              // תמלול

    static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string DataDir = Path.Combine(AppDir, "browser-data");
    static readonly string SettingsFile = Path.Combine(AppDir, "settings.json");
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string SkillsDir = Path.Combine(Home, @".claude\skills");

    // "ui" shows the app. "zoom" and "moodle" are hidden browsers that share one sign-in profile;
    // each one comes to the front only when HIT asks the user to sign in.
    WebView2 ui, zoom, moodle;
    Panel zoomPanel, moodlePanel;

    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    readonly Dictionary<string, object> settings = new Dictionary<string, object>();
    string lastClipboard;

    // Recordings
    readonly List<Job> jobs = new List<Job>();
    readonly Queue<Job> pending = new Queue<Job>();
    int nextJobId = 1;
    bool working;
    TaskCompletionSource<string> playInfoTcs;
    bool zoomCancelled;

    // Moodle
    readonly SemaphoreSlim moodleLock = new SemaphoreSlim(1, 1);
    bool moodleCancelled;
    readonly HashSet<string> busySections = new HashSet<string>();

    // Page scripts
    readonly Dictionary<int, TaskCompletionSource<Dictionary<string, object>>> evals = new Dictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
    int nextEvalId = 1;

    // Claude
    readonly Dictionary<string, Process> claudeRuns = new Dictionary<string, Process>();
    bool? claudeConnected;

    [STAThread]
    static void Main()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.Run(new HitStudent());
    }

    HitStudent()
    {
        Text = "HIT Student";
        ClientSize = new Size(1180, 800);
        MinimumSize = new Size(760, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(17, 19, 24);
        RightToLeftLayout = false;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        LoadSettings();

        zoom = new WebView2 { Dock = DockStyle.Fill };
        zoomPanel = LoginPanel(zoom, "\u05D4\u05EA\u05D7\u05D1\u05E8 \u05DC-Zoom \u05E9\u05DC HIT (\u05E4\u05E2\u05DD \u05D0\u05D7\u05EA \u05D1\u05DC\u05D1\u05D3). \u05D4\u05D4\u05D5\u05E8\u05D3\u05D4 \u05EA\u05DE\u05E9\u05D9\u05DA \u05DC\u05D1\u05D3.",
            delegate { if (working) zoomCancelled = true; ui.BringToFront(); });
        moodle = new WebView2 { Dock = DockStyle.Fill };
        moodlePanel = LoginPanel(moodle, "\u05D4\u05EA\u05D7\u05D1\u05E8 \u05E2\u05DD \u05D4\u05DE\u05E9\u05EA\u05DE\u05E9 \u05E9\u05DC HIT \u05DB\u05D3\u05D9 \u05DC\u05D7\u05D1\u05E8 \u05D0\u05EA \u05D4-Moodle (\u05E4\u05E2\u05DD \u05D0\u05D7\u05EA \u05D1\u05DC\u05D1\u05D3).",
            delegate { moodleCancelled = true; ui.BringToFront(); });

        ui = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(zoomPanel);
        Controls.Add(moodlePanel);
        Controls.Add(ui);
        ui.BringToFront();

        Load += async delegate { await InitAsync(); };
        Activated += delegate { CheckClipboard(false); };
        FormClosing += delegate { foreach (var p in claudeRuns.Values) KillTree(p); };
    }

    Panel LoginPanel(WebView2 view, string hint, EventHandler back)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var bar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(37, 99, 235) };
        var label = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f), Text = hint,
            TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 16, 0), RightToLeft = RightToLeft.Yes
        };
        var button = new Button
        {
            Dock = DockStyle.Left, Width = 150, Text = "\u05D7\u05D6\u05E8\u05D4 \u05DC\u05D0\u05E4\u05DC\u05D9\u05E7\u05E6\u05D9\u05D4", FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White, BackColor = Color.FromArgb(29, 78, 216), Font = new Font("Segoe UI", 10f, FontStyle.Bold)
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
            var env = await CoreWebView2Environment.CreateAsync(null, DataDir);
            await ui.EnsureCoreWebView2Async(env);
            await zoom.EnsureCoreWebView2Async(env);
            await moodle.EnsureCoreWebView2Async(env);
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
        u.NewWindowRequested += (s, e) => { e.Handled = true; OpenExternal(e.Uri); };
        u.Navigate("https://app.local/ui.html");

        foreach (var view in new[] { zoom, moodle })
        {
            var core = view.CoreWebView2;
            core.IsMuted = true;
            core.WebMessageReceived += OnEvalMessage;
            core.NewWindowRequested += (s, e) => { e.Handled = true; core.Navigate(e.Uri); };
        }
        zoom.CoreWebView2.WebResourceResponseReceived += OnZoomResponse;

        InstallSkill();
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
    }

    void SaveSettings()
    {
        try { File.WriteAllText(SettingsFile, json.Serialize(settings), Encoding.UTF8); } catch { }
    }

    string StudyRoot { get { return Str(settings, "studyRoot"); } }
    string RecordingsDir { get { return Str(settings, "recordingsDir"); } }

    void SendSettings()
    {
        Send(new { type = "settings", studyRoot = StudyRoot, recordingsDir = RecordingsDir });
    }

    // ---------- UI messages ----------

    void Send(object message)
    {
        if (ui.CoreWebView2 != null) ui.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));
    }

    void OnUiMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        Dictionary<string, object> m;
        try { m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); }
        catch { return; }
        try { HandleUiMessage(m); }
        catch (Exception ex) { Send(new { type = "toast", text = ex.Message, error = true }); }
    }

    void HandleUiMessage(Dictionary<string, object> m)
    {
        string type = Str(m, "type");
        Job job = FindJob(m);
        switch (type)
        {
            case "ready":
                SendSettings();
                foreach (var j in jobs) SendJob(j);
                CheckClipboard(false);
                SendClaudeState(null);
                break;

            // Recordings
            case "paste": CheckClipboard(true); break;
            case "add":
                string url = Str(m, "url").Trim();
                if (url.Length == 0) return;
                AddJob(new Job { Url = url, Name = Str(m, "name").Trim(), Transcript = Bool(m, "transcript") });
                break;
            case "retry":
                if (job != null && job.State == "failed")
                {
                    Update(job, "queued", "ממתין...", -1);
                    pending.Enqueue(job);
                    Pump();
                }
                break;
            case "remove":
                if (job != null && (job.State == "done" || job.State == "failed")) jobs.Remove(job);
                break;
            case "openFile":
                string jp = job == null ? null : (Str(m, "which") == "transcript" ? job.TranscriptPath : job.VideoPath);
                if (jp != null && File.Exists(jp)) Process.Start(jp);
                break;
            case "showFile":
                string sp = job == null ? null : (job.VideoPath ?? job.TranscriptPath);
                if (sp != null && File.Exists(sp)) Process.Start("explorer.exe", "/select,\"" + sp + "\"");
                break;

            // Folders
            case "chooseFolder": ChooseFolder(Str(m, "which")); break;
            case "openFolder":
                string folder = Str(m, "which") == "study" ? StudyRoot : RecordingsDir;
                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", "\"" + folder + "\"");
                break;
            case "openPath": OpenLocalPath(Str(m, "path")); break;

            // Moodle
            case "loadCourses": LoadCourses(); break;
            case "openCourse": OpenCourse(Convert.ToInt32(m["id"]), Str(m, "name")); break;
            case "syncSection": SyncSection(m); break;
            case "addFiles": AddFiles(Str(m, "course"), Convert.ToInt32(m["num"])); break;

            // Claude
            case "claudeCheck": CheckClaude(); break;
            case "claudeInstall": InstallClaude(); break;
            case "claudeConnect": ConnectClaude(); break;
            case "summarize": Summarize(Convert.ToInt32(m["courseId"]), Str(m, "course"), Convert.ToInt32(m["num"]), Str(m, "title")); break;
            case "chat": Chat(Str(m, "course"), Str(m, "text")); break;
            case "chatReset":
                var meta = LoadMeta(Str(m, "course"));
                meta.Remove("chatSession");
                SaveMeta(Str(m, "course"), meta);
                break;
            case "cancelClaude":
                Process run;
                if (claudeRuns.TryGetValue(Str(m, "key"), out run)) KillTree(run);
                break;
            case "openLink": OpenExternal(Str(m, "url")); break;
        }
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

    void ChooseFolder(string which)
    {
        string key = which == "study" ? "studyRoot" : "recordingsDir";
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = which == "study" ? "\u05D1\u05D7\u05E8 \u05D0\u05EA \u05EA\u05D9\u05E7\u05D9\u05D9\u05EA \u05D4\u05E7\u05D5\u05E8\u05E1\u05D9\u05DD" : "\u05D1\u05D7\u05E8 \u05D0\u05D9\u05E4\u05D4 \u05DC\u05E9\u05DE\u05D5\u05E8 \u05D4\u05E7\u05DC\u05D8\u05D5\u05EA";
            string current = Str(settings, key);
            if (Directory.Exists(current)) dlg.SelectedPath = current;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            settings[key] = dlg.SelectedPath;
            SaveSettings();
            SendSettings();
        }
    }

    void OpenLocalPath(string path)
    {
        // Only open things inside the app's own folders.
        string full = Path.GetFullPath(path);
        bool inside = full.StartsWith(Path.GetFullPath(StudyRoot), StringComparison.OrdinalIgnoreCase)
                   || full.StartsWith(Path.GetFullPath(RecordingsDir), StringComparison.OrdinalIgnoreCase);
        if (!inside) return;
        if (Directory.Exists(full)) Process.Start("explorer.exe", "\"" + full + "\"");
        else if (File.Exists(full)) Process.Start(full);
    }

    static void OpenExternal(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            Process.Start(url);
    }

    // ---------- Scripts inside the hidden browsers ----------

    async Task<string> EvalAsync(WebView2 view, string body, int timeoutMs)
    {
        int id = nextEvalId++;
        var tcs = new TaskCompletionSource<Dictionary<string, object>>();
        evals[id] = tcs;
        string script = "(async()=>{const __id=" + id + ";try{const __v=await (async()=>{" + body + "})();"
                      + "chrome.webview.postMessage({__rid:__id,ok:true,value:JSON.stringify(__v===undefined?null:__v)});}"
                      + "catch(e){chrome.webview.postMessage({__rid:__id,ok:false,value:String((e&&e.message)||e)});}})();";
        try
        {
            await view.CoreWebView2.ExecuteScriptAsync(script);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (done != tcs.Task) throw new Exception("\u05D4\u05D3\u05E3 \u05DC\u05D0 \u05E2\u05E0\u05D4 \u05D1\u05D6\u05DE\u05DF");
            var m = tcs.Task.Result;
            if (!Bool(m, "ok")) throw new Exception(Str(m, "value"));
            return Str(m, "value");
        }
        finally { evals.Remove(id); }
    }

    void OnEvalMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
            if (m == null || !m.ContainsKey("__rid")) return;
            TaskCompletionSource<Dictionary<string, object>> tcs;
            if (evals.TryGetValue(Convert.ToInt32(m["__rid"]), out tcs)) tcs.TrySetResult(m);
        }
        catch { }
    }

    // ---------- Moodle ----------

    void SendMoodle(string state, string message)
    {
        Send(new { type = "moodle", state = state, message = message });
    }

    async Task EnsureMoodle()
    {
        if (await MoodleReady()) return;
        SendMoodle("connecting", "\u05DE\u05EA\u05D7\u05D1\u05E8 \u05DC-Moodle...");
        moodleCancelled = false;
        moodle.CoreWebView2.Navigate(MoodleBase + "/my/");
        var clock = Stopwatch.StartNew();
        bool shown = false;
        while (true)
        {
            await Task.Delay(500);
            if (moodleCancelled)
            {
                moodleCancelled = false;
                throw new Exception("\u05D4\u05D4\u05EA\u05D7\u05D1\u05E8\u05D5\u05EA \u05DC-Moodle \u05D1\u05D5\u05D8\u05DC\u05D4");
            }
            if (await MoodleReady()) break;
            if (!shown && clock.Elapsed.TotalSeconds > 3)
            {
                shown = true;
                moodlePanel.BringToFront();
                SendMoodle("login", "\u05DE\u05D7\u05DB\u05D4 \u05DC\u05D4\u05EA\u05D7\u05D1\u05E8\u05D5\u05EA...");
            }
            if (clock.Elapsed.TotalMinutes > 10) throw new Exception("\u05E4\u05D2 \u05D4\u05D6\u05DE\u05DF \u05DC\u05D4\u05EA\u05D7\u05D1\u05E8\u05D5\u05EA");
        }
        ui.BringToFront();
        SendMoodle("ready", "");
    }

    async Task<bool> MoodleReady()
    {
        string src = moodle.Source == null ? "" : moodle.Source.ToString();
        if (!src.StartsWith(MoodleBase, StringComparison.OrdinalIgnoreCase) || src.IndexOf("/login/", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        try
        {
            string v = await EvalAsync(moodle, "return (typeof M!=='undefined'&&M.cfg&&M.cfg.sesskey)||'';", 4000);
            return json.Deserialize<string>(v).Length > 0;
        }
        catch { return false; }
    }

    async Task<T> WithMoodle<T>(Func<Task<T>> work)
    {
        await moodleLock.WaitAsync();
        try
        {
            await EnsureMoodle();
            return await work();
        }
        finally { moodleLock.Release(); }
    }

    const string CoursesJs = @"
const call = async (classification) => {
  const r = await fetch('/lib/ajax/service.php?sesskey=' + M.cfg.sesskey + '&info=core_course_get_enrolled_courses_by_timeline_classification', {
    method: 'POST', headers: {'Content-Type': 'application/json'},
    body: JSON.stringify([{index: 0, methodname: 'core_course_get_enrolled_courses_by_timeline_classification',
      args: {offset: 0, limit: 0, classification, sort: 'fullname'}}])});
  const j = await r.json();
  if (j[0].error) throw new Error(j[0].exception && j[0].exception.message || 'Moodle error');
  return j[0].data.courses;
};
const now = await call('inprogress');
const all = await call('all');
const ids = new Set(now.map(c => c.id));
return all.filter(c => !c.hidden).map(c => ({id: c.id, name: c.fullname, short: c.shortname, category: c.coursecategory || '',
  current: ids.has(c.id), url: c.viewurl}));";

    // Reads the course page (and any per-section pages) into sections of activities.
    const string SectionsJs = @"
const base = location.origin;
const abs = (h) => { try { return new URL(h, base).href; } catch (e) { return null; } };
const clean = (el) => { if (!el) return ''; const c = el.cloneNode(true); c.querySelectorAll('.accesshide, .sr-only, .visually-hidden').forEach(x => x.remove()); return c.textContent.replace(/\s+/g, ' ').trim(); };
const load = async (url) => new DOMParser().parseFromString(await (await fetch(url, {credentials: 'include'})).text(), 'text/html');
const readItems = (root) => {
  const items = [], seen = new Set();
  root.querySelectorAll('li.activity').forEach(a => {
    const link = a.querySelector('a.aalink, .activityname a, a[href*=""/mod/""]');
    const mod = ((a.className.match(/modtype_(\w+)/) || [])[1]) || '';
    const name = clean(a.querySelector('.instancename, .activityname')) || clean(link);
    const url = link ? abs(link.getAttribute('href')) : null;
    if (mod === 'label') {
      a.querySelectorAll('a[href]').forEach(x => {
        const h = abs(x.getAttribute('href'));
        if (!h || seen.has(h)) return;
        if (/zoom\.us\/rec/i.test(h)) { seen.add(h); items.push({mod: 'zoom', name: clean(x) || 'Zoom', url: h}); }
        else if (/pluginfile\.php/i.test(h)) { seen.add(h); items.push({mod: 'file', name: clean(x) || h.split('/').pop(), url: h}); }
      });
      return;
    }
    if (url && !seen.has(url)) { seen.add(url); items.push({mod, name, url}); }
  });
  root.querySelectorAll('.summary a[href], .summarytext a[href], [data-for=""sectioninfo""] a[href]').forEach(x => {
    const h = abs(x.getAttribute('href'));
    if (!h || seen.has(h)) return;
    if (/zoom\.us\/rec/i.test(h)) { seen.add(h); items.push({mod: 'zoom', name: clean(x) || 'Zoom', url: h}); }
    else if (/pluginfile\.php/i.test(h)) { seen.add(h); items.push({mod: 'file', name: clean(x) || decodeURIComponent(h.split('/').pop().split('?')[0]), url: h}); }
  });
  return items;
};
const doc = await load('/course/view.php?id=' + COURSE_ID);
let nodes = [...doc.querySelectorAll('li.section, li[data-for=""section""]')];
nodes = nodes.filter(n => !nodes.some(o => o !== n && o.contains(n)));
const out = [];
for (let i = 0; i < nodes.length; i++) {
  const s = nodes[i];
  const numAttr = s.getAttribute('data-number') ?? s.getAttribute('data-sectionnum') ?? (s.id || '').replace('section-', '');
  const num = /^\d+$/.test(numAttr) ? parseInt(numAttr) : i;
  const title = clean(s.querySelector('.sectionname, [data-for=""section_title""], h3')) || ('\u05D9\u05D7\u05D9\u05D3\u05D4 ' + num);
  let items = readItems(s);
  if (!items.length) {
    const more = s.querySelector('a[href*=""section.php""], a[href*=""&section=""]');
    if (more) { try { items = readItems(await load(abs(more.getAttribute('href')))); } catch (e) {} }
  }
  out.push({num, title, items});
}
return out;";

    async void LoadCourses()
    {
        try
        {
            string v = await WithMoodle(() => EvalAsync(moodle, CoursesJs, 60000));
            Send(new { type = "courses", data = json.DeserializeObject(v) });
        }
        catch (Exception ex)
        {
            ui.BringToFront();
            SendMoodle("error", ex.Message);
        }
    }

    async void OpenCourse(int id, string name)
    {
        try
        {
            string v = await WithMoodle(() => EvalAsync(moodle, SectionsJs.Replace("COURSE_ID", id.ToString()), 120000));
            var sections = json.Deserialize<List<Dictionary<string, object>>>(v);
            var meta = LoadMeta(name);
            meta["courseId"] = id;
            SaveMeta(name, meta);
            foreach (var s in sections) s["local"] = LocalInfo(name, Convert.ToInt32(s["num"]));
            Send(new { type = "course", id = id, name = name, folder = CourseDir(name), sections = sections });
        }
        catch (Exception ex)
        {
            ui.BringToFront();
            Send(new { type = "courseError", id = id, message = ex.Message });
        }
    }

    // ---------- Course folders ----------

    string CourseDir(string course) { return Path.Combine(StudyRoot, SafeName(course)); }

    static string LectureFolderName(int num)
    {
        return num == 0 ? "\u05DB\u05DC\u05DC\u05D9" : "\u05E9\u05D9\u05E2\u05D5\u05E8 " + num; // כללי / שיעור N
    }

    string LectureDir(string course, int num) { return Path.Combine(CourseDir(course), LectureFolderName(num)); }

    string StudyPagePath(string course, int num)
    {
        return Path.Combine(CourseDir(course), StudyPages, "\u05E9\u05D1\u05D5\u05E2-" + num + ".html"); // שבוע-N.html
    }

    object LocalInfo(string course, int num)
    {
        string dir = LectureDir(course, num);
        int files = 0, videos = 0, transcripts = 0;
        if (Directory.Exists(dir))
        {
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".mp4") videos++;
                else if (f.EndsWith(TranscriptSuffix + ".txt")) transcripts++;
                else files++;
            }
        }
        string page = StudyPagePath(course, num);
        return new
        {
            dir = dir, exists = Directory.Exists(dir), files = files, videos = videos, transcripts = transcripts,
            studyPage = File.Exists(page) ? page : null
        };
    }

    void SendSectionLocal(string course, int num)
    {
        Send(new { type = "sectionLocal", course = course, num = num, local = LocalInfo(course, num) });
    }

    void SectionStatus(string course, int num, string text, bool busy, bool error)
    {
        Send(new { type = "sectionStatus", course = course, num = num, text = text, busy = busy, error = error });
    }

    Dictionary<string, object> LoadMeta(string course)
    {
        string file = Path.Combine(CourseDir(course), ".hit-student.json");
        try { if (File.Exists(file)) return json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8)); }
        catch { }
        return new Dictionary<string, object>();
    }

    void SaveMeta(string course, Dictionary<string, object> meta)
    {
        string dir = CourseDir(course);
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, ".hit-student.json");
        try
        {
            if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal);
            File.WriteAllText(file, json.Serialize(meta), Encoding.UTF8);
            File.SetAttributes(file, FileAttributes.Hidden);
        }
        catch { }
    }

    void AddFiles(string course, int num)
    {
        using (var dlg = new OpenFileDialog { Multiselect = true, Title = "\u05D4\u05D5\u05E1\u05E3 \u05E7\u05D1\u05E6\u05D9\u05DD \u05DC\u05E9\u05D9\u05E2\u05D5\u05E8" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string dir = LectureDir(course, num);
            Directory.CreateDirectory(dir);
            foreach (string f in dlg.FileNames)
            {
                string target = Path.Combine(IsExercise(Path.GetFileName(f)) ? Path.Combine(dir, Exercises) : dir, Path.GetFileName(f));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target, true);
            }
            SendSectionLocal(course, num);
        }
    }

    static readonly Regex ExerciseName = new Regex(
        "(\u05EA\u05E8\u05D2\u05D9\u05DC|\u05EA\u05E8\u05D2\u05D5\u05DC|\u05E4\u05EA\u05E8\u05D5\u05DF|\u05E4\u05EA\u05E8\u05D5\u05E0\u05D5\u05EA|\u05E9\u05D9\u05E2\u05D5\u05E8\u05D9 \u05D1\u05D9\u05EA|\u05DE\u05D8\u05DC\u05D4|\u05E9\"\u05D1|targ|tirgul|solut|homework|\\bhw|exercise|assignment|\\bsol)",
        RegexOptions.IgnoreCase);

    static bool IsExercise(string name) { return ExerciseName.IsMatch(name); }

    // ---------- Downloading a lecture from Moodle ----------

    async void SyncSection(Dictionary<string, object> m)
    {
        string course = Str(m, "course");
        var section = m["section"] as Dictionary<string, object>;
        int num = Convert.ToInt32(section["num"]);
        string key = course + "|" + num;
        if (!busySections.Add(key)) return;

        string dir = LectureDir(course, num);
        var items = (section["items"] as System.Collections.ArrayList ?? new System.Collections.ArrayList()).Cast<Dictionary<string, object>>().ToList();
        int saved = 0, recordings = 0, failed = 0;
        try
        {
            SectionStatus(course, num, "\u05DE\u05EA\u05D7\u05D1\u05E8 \u05DC-Moodle...", true, false);
            Directory.CreateDirectory(dir);
            using (var http = await MoodleHttp())
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    string mod = Str(it, "mod"), name = Str(it, "name"), url = Str(it, "url");
                    SectionStatus(course, num, "\u05DE\u05D5\u05E8\u05D9\u05D3 " + (i + 1) + "/" + items.Count + ": " + name, true, false);
                    bool exercise = mod == "assign" || IsExercise(name);
                    string target = exercise ? Path.Combine(dir, Exercises) : dir;
                    try
                    {
                        if (mod == "resource")
                        {
                            string sep = url.Contains("?") ? "&" : "?";
                            if (await DownloadFile(http, url + sep + "redirect=1", target, exercise) != null) saved++;
                        }
                        else if (mod == "file")
                        {
                            if (await DownloadFile(http, url, target, exercise) != null) saved++;
                        }
                        else if (mod == "zoom")
                        {
                            if (QueueRecording(course, num, url)) recordings++;
                        }
                        else if (mod == "url")
                        {
                            string real = await WithMoodle(() => ResolveMoodleUrl(url));
                            if (real.IndexOf("zoom.us/rec", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (QueueRecording(course, num, real)) recordings++;
                            }
                            else if (real.StartsWith("http"))
                            {
                                File.WriteAllText(Path.Combine(dir, SafeName(name) + ".url"), "[InternetShortcut]\r\nURL=" + real + "\r\n");
                            }
                        }
                        else if (mod == "assign" || mod == "folder" || mod == "page" || mod == "book")
                        {
                            foreach (string f in await PluginFiles(http, url))
                                if (await DownloadFile(http, f, target, exercise) != null) saved++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Send(new { type = "toast", text = name + ": " + ex.Message, error = true });
                    }
                }
            }
            string summary = "\u05E0\u05E9\u05DE\u05E8\u05D5 " + saved + " \u05E7\u05D1\u05E6\u05D9\u05DD";
            if (recordings > 0) summary += ", " + recordings + " \u05D4\u05E7\u05DC\u05D8\u05D5\u05EA \u05D1\u05EA\u05D5\u05E8 \u05D4\u05D5\u05E8\u05D3\u05D4";
            if (failed > 0) summary += ", " + failed + " \u05E0\u05DB\u05E9\u05DC\u05D5";
            SectionStatus(course, num, summary, false, failed > 0);
        }
        catch (Exception ex)
        {
            ui.BringToFront();
            SectionStatus(course, num, ex.Message, false, true);
        }
        finally
        {
            busySections.Remove(key);
            SendSectionLocal(course, num);
        }
    }

    async Task<HttpClient> MoodleHttp()
    {
        await moodleLock.WaitAsync();
        try { await EnsureMoodle(); }
        finally { moodleLock.Release(); }

        var jar = new CookieContainer();
        foreach (var c in await moodle.CoreWebView2.CookieManager.GetCookiesAsync(MoodleBase))
        {
            try { jar.Add(new Cookie(c.Name, c.Value, c.Path, c.Domain)); } catch { }
        }
        var http = new HttpClient(new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = true, UseCookies = true });
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", moodle.CoreWebView2.Settings.UserAgent);
        http.Timeout = TimeSpan.FromMinutes(30);
        return http;
    }

    async Task<string> DownloadFile(HttpClient http, string url, string dir, bool exercise)
    {
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            if (!resp.IsSuccessStatusCode) throw new Exception("HTTP " + (int)resp.StatusCode);
            var ct = resp.Content.Headers.ContentType;
            if (ct != null && ct.MediaType == "text/html") return null; // a Moodle page, not a file

            string fromUrl = Uri.UnescapeDataString(resp.RequestMessage.RequestUri.AbsolutePath.Split('/').Last());
            string name = fromUrl;
            var cd = resp.Content.Headers.ContentDisposition;
            if (!fromUrl.Contains(".") && cd != null)
                name = (cd.FileNameStar ?? cd.FileName ?? fromUrl).Trim('"');
            name = SafeName(name);
            if (Regex.IsMatch(Path.GetExtension(name), @"^\.(png|jpe?g|gif|svg|css|js)$", RegexOptions.IgnoreCase) && !exercise) return null;

            string path = Path.Combine(dir, name);
            long? length = resp.Content.Headers.ContentLength;
            if (File.Exists(path) && length.HasValue && new FileInfo(path).Length == length.Value) return path;
            Directory.CreateDirectory(dir);
            using (var fs = File.Create(path)) await resp.Content.CopyToAsync(fs);
            return path;
        }
    }

    async Task<List<string>> PluginFiles(HttpClient http, string pageUrl)
    {
        string html = await http.GetStringAsync(pageUrl);
        var found = new List<string>();
        foreach (Match mt in Regex.Matches(html, "href=\"(https?://[^\"]*/pluginfile\\.php/[^\"]+)\""))
        {
            string u = WebUtility.HtmlDecode(mt.Groups[1].Value);
            if (u.Contains("/user/icon/") || u.Contains("/course/overviewfiles/")) continue;
            if (!found.Contains(u)) found.Add(u);
        }
        return found;
    }

    async Task<string> ResolveMoodleUrl(string viewUrl)
    {
        var tcs = new TaskCompletionSource<string>();
        EventHandler<CoreWebView2NavigationStartingEventArgs> watch = (s, e) =>
        {
            if (!e.Uri.StartsWith(MoodleBase, StringComparison.OrdinalIgnoreCase) && e.Uri.StartsWith("http"))
            {
                e.Cancel = true;
                tcs.TrySetResult(e.Uri);
            }
        };
        moodle.CoreWebView2.NavigationStarting += watch;
        try
        {
            moodle.CoreWebView2.Navigate(viewUrl);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(8000));
            if (done == tcs.Task) return tcs.Task.Result;
            string v = await EvalAsync(moodle,
                "const a=document.querySelector('.urlworkaround a, [role=main] a[href^=\"http\"]:not([href*=\"md.hit.ac.il\"])');return a?a.href:'';", 5000);
            return json.Deserialize<string>(v);
        }
        finally { moodle.CoreWebView2.NavigationStarting -= watch; }
    }

    bool QueueRecording(string course, int num, string url)
    {
        var meta = LoadMeta(course);
        var done = meta.ContainsKey("recordings") ? ((System.Collections.ArrayList)meta["recordings"]).Cast<object>().Select(o => o.ToString()).ToList() : new List<string>();
        string id = url.Split('?')[0];
        if (done.Contains(id) || jobs.Any(j => j.Url.Split('?')[0] == id && j.State != "failed")) return false;

        int already = jobs.Count(j => j.Dir == Path.Combine(LectureDir(course, num), RecordingFolder));
        string name = LectureFolderName(num) + (already > 0 ? " \u05D7\u05DC\u05E7 " + (already + 1) : ""); // "שיעור N" / "שיעור N חלק 2"
        AddJob(new Job
        {
            Url = url, Name = name, Transcript = true, Dir = Path.Combine(LectureDir(course, num), RecordingFolder),
            Finished = j =>
            {
                if (j.State == "done")
                {
                    var m2 = LoadMeta(course);
                    var list = m2.ContainsKey("recordings") ? (System.Collections.ArrayList)m2["recordings"] : new System.Collections.ArrayList();
                    list.Add(id);
                    m2["recordings"] = list;
                    SaveMeta(course, m2);
                }
                SendSectionLocal(course, num);
            }
        });
        return true;
    }

    // ---------- Recordings ----------

    void AddJob(Job j)
    {
        j.Id = nextJobId++;
        jobs.Add(j);
        SendJob(j);
        pending.Enqueue(j);
        Pump();
    }

    Job FindJob(Dictionary<string, object> m)
    {
        if (!m.ContainsKey("id") || m["id"] == null) return null;
        int id;
        if (!int.TryParse(m["id"].ToString(), out id)) return null;
        return jobs.Find(j => j.Id == id);
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

    async void Pump()
    {
        if (working) return;
        working = true;
        while (pending.Count > 0)
        {
            var job = pending.Dequeue();
            try { await RunJob(job); }
            catch (Exception ex) { Log(job, "Error: " + ex.Message); Update(job, "failed", "\u05DE\u05E9\u05D4\u05D5 \u05D4\u05E9\u05EA\u05D1\u05E9 - \u05E8\u05D0\u05D4 \u05E4\u05E8\u05D8\u05D9\u05DD", 0); }
            if (job.Finished != null) { try { job.Finished(job); } catch { } }
        }
        working = false;
        ui.BringToFront();
        if (zoom.CoreWebView2 != null) zoom.CoreWebView2.Navigate("about:blank");
    }

    async Task RunJob(Job job)
    {
        Update(job, "running", "\u05E4\u05D5\u05EA\u05D7 \u05D0\u05EA \u05D4\u05D4\u05E7\u05DC\u05D8\u05D4...", -1);
        Log(job, "Opening " + job.Url);

        // 1. Load the recording page in the built-in browser. If HIT/Zoom wants a sign-in, show it to the user.
        playInfoTcs = new TaskCompletionSource<string>();
        zoomCancelled = false;
        zoom.CoreWebView2.Navigate(job.Url);
        var clock = Stopwatch.StartNew();
        bool shown = false;
        while (!playInfoTcs.Task.IsCompleted)
        {
            await Task.Delay(400);
            if (zoomCancelled)
            {
                Update(job, "failed", "\u05D1\u05D5\u05D8\u05DC - \u05DC\u05D7\u05E5 \u05E0\u05E1\u05D4 \u05E9\u05D5\u05D1 \u05D0\u05D7\u05E8\u05D9 \u05D4\u05D4\u05EA\u05D7\u05D1\u05E8\u05D5\u05EA", 0);
                return;
            }
            string src = zoom.Source == null ? "" : zoom.Source.ToString();
            bool onRecording = src.IndexOf("/rec/", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!shown && ((!onRecording && clock.Elapsed.TotalSeconds > 2) || clock.Elapsed.TotalSeconds > 15))
            {
                shown = true;
                zoomPanel.BringToFront();
                Update(job, "running", "\u05DE\u05D7\u05DB\u05D4 \u05E9\u05EA\u05EA\u05D7\u05D1\u05E8...", -1);
                Log(job, "Zoom needs a sign-in; showing the browser.");
            }
            if (clock.Elapsed.TotalMinutes > 10)
            {
                Update(job, "failed", "\u05E4\u05D2 \u05D4\u05D6\u05DE\u05DF \u05DC\u05D4\u05DE\u05EA\u05E0\u05D4 \u05DC-Zoom", 0);
                return;
            }
        }
        ui.BringToFront();

        var info = json.Deserialize<Dictionary<string, object>>(playInfoTcs.Task.Result);
        var result = info.ContainsKey("result") ? info["result"] as Dictionary<string, object> : null;
        string mp4Url = Str(result, "viewMp4Url");
        string transcriptUrl = Str(result, "transcriptUrl");
        var meet = result != null && result.ContainsKey("meet") ? result["meet"] as Dictionary<string, object> : null;
        string topic = Str(meet, "topic");

        string dir = job.Dir ?? RecordingsDir;
        string baseName = SafeName(job.Name.Length > 0 ? job.Name : (topic.Length > 0 ? topic : "Zoom recording"));
        Directory.CreateDirectory(dir);
        string name = baseName;
        for (int i = 2; File.Exists(Path.Combine(dir, name + ".mp4")) || File.Exists(TranscriptFile(dir, name)); i++)
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
                Update(job, "running", "\u05DE\u05D5\u05E8\u05D9\u05D3 \u05EA\u05DE\u05DC\u05D5\u05DC...", -1);
                string vtt = null;
                try
                {
                    string v = await EvalAsync(zoom, "const r=await fetch(" + json.Serialize(transcriptUrl) + ",{credentials:'include'});"
                                               + "if(!r.ok) throw new Error('HTTP '+r.status);return await r.text();", 60000);
                    vtt = json.Deserialize<string>(v);
                }
                catch (Exception ex) { Log(job, "Transcript: " + ex.Message); }
                if (vtt != null && vtt.Contains("-->"))
                {
                    job.TranscriptPath = TranscriptFile(dir, name);
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
            Update(job, "running", "\u05DE\u05EA\u05E7\u05D9\u05DF yt-dlp (\u05E4\u05E2\u05DD \u05E8\u05D0\u05E9\u05D5\u05E0\u05D4 \u05D1\u05DC\u05D1\u05D3)...", -1);
            await RunTool(job, "winget", "install --id yt-dlp.yt-dlp -e --silent --accept-source-agreements --accept-package-agreements");
            ytdlp = FindYtDlp();
        }
        if (ytdlp == null)
        {
            Log(job, "yt-dlp not found. Install it with: winget install yt-dlp.yt-dlp");
            Update(job, "failed", "yt-dlp \u05DC\u05D0 \u05DE\u05D5\u05EA\u05E7\u05DF", 0);
            return;
        }
        string cookieFile = Path.Combine(DataDir, "zoom-cookies.txt");
        await WriteCookies(cookieFile);
        string agent = zoom.CoreWebView2.Settings.UserAgent;
        string pageUrl = zoom.Source.ToString();
        string referer = new Uri(pageUrl).GetLeftPart(UriPartial.Authority) + "/";
        string output = Path.Combine(dir, name.Replace("%", "%%") + ".%(ext)s");
        string common = "--cookies " + Q(cookieFile) + " --user-agent " + Q(agent) + " --referer " + Q(referer)
                      + " --encoding utf-8 --newline --no-mtime --no-playlist -o " + Q(output);

        Update(job, "running", "\u05DE\u05D5\u05E8\u05D9\u05D3 \u05D5\u05D9\u05D3\u05D0\u05D5...", 0);
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
            foreach (string f in Directory.GetFiles(dir, name + ".*"))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".mp4" || ext == ".m4a" || ext == ".webm" || ext == ".mkv") job.VideoPath = f;
            }
            Update(job, "done", job.TranscriptPath != null ? "\u05D4\u05D5\u05D9\u05D3\u05D0\u05D5 \u05D5\u05D4\u05EA\u05DE\u05DC\u05D5\u05DC \u05E0\u05E9\u05DE\u05E8\u05D5" : "\u05D4\u05D5\u05D9\u05D3\u05D0\u05D5 \u05E0\u05E9\u05DE\u05E8", 100);
        }
        else
        {
            Update(job, "failed", job.TranscriptPath != null ? "\u05D4\u05EA\u05DE\u05DC\u05D5\u05DC \u05E0\u05E9\u05DE\u05E8, \u05D4\u05D5\u05D9\u05D3\u05D0\u05D5 \u05E0\u05DB\u05E9\u05DC" : "\u05D4\u05D4\u05D5\u05E8\u05D3\u05D4 \u05E0\u05DB\u05E9\u05DC\u05D4 - \u05E8\u05D0\u05D4 \u05E4\u05E8\u05D8\u05D9\u05DD", 0);
        }
    }

    static string TranscriptFile(string dir, string name)
    {
        // "הרצאה 1" -> "הרצאה 1 תמלול.txt"
        return Path.Combine(dir, name + " " + TranscriptSuffix + ".txt");
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
                string status = "\u05DE\u05D5\u05E8\u05D9\u05D3 \u05D5\u05D9\u05D3\u05D0\u05D5 - " + m.Groups[1].Value + "% \u05DE\u05EA\u05D5\u05DA " + m.Groups[2].Value;
                if (m.Groups[4].Success && !m.Groups[4].Value.StartsWith("Unknown")) status += " - \u05E0\u05E9\u05D0\u05E8\u05D5 " + m.Groups[4].Value;
                Update(job, "running", status, (int)pct);
                if (pct < 100) return;
            }
        }
        Log(job, line);
    }

    // ---------- Claude ----------

    static string FindClaude()
    {
        string native = Path.Combine(Home, @".local\bin\claude.exe");
        if (File.Exists(native)) return native;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                foreach (string n in new[] { "claude.exe", "claude.cmd" })
                {
                    string exe = Path.Combine(dir.Trim(), n);
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }
        }
        string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"npm\claude.cmd");
        return File.Exists(npm) ? npm : null;
    }

    void SendClaudeState(string message)
    {
        Send(new { type = "claude", installed = FindClaude() != null, connected = claudeConnected, message = message });
    }

    void InstallClaude()
    {
        Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoExit -ExecutionPolicy Bypass -Command \"Write-Host 'Installing Claude Code...'; irm https://claude.ai/install.ps1 | iex; Write-Host ''; Write-Host 'Done. Close this window and press Check again in HIT Student.'\"")
        { UseShellExecute = true });
    }

    void ConnectClaude()
    {
        string claude = FindClaude();
        if (claude == null) { InstallClaude(); return; }
        Directory.CreateDirectory(StudyRoot);
        // Claude Code asks to sign in on first start; the user closes the window when done.
        Process.Start(new ProcessStartInfo("cmd.exe", "/k \"echo Sign in to Claude, then close this window. && \"" + claude + "\"\"")
        { UseShellExecute = true, WorkingDirectory = StudyRoot });
        claudeConnected = null;
        SendClaudeState("\u05D0\u05D7\u05E8\u05D9 \u05D4\u05D4\u05EA\u05D7\u05D1\u05E8\u05D5\u05EA \u05E1\u05D2\u05D5\u05E8 \u05D0\u05EA \u05D4\u05D7\u05DC\u05D5\u05DF \u05D5\u05DC\u05D7\u05E5 \"\u05D1\u05D3\u05D5\u05E7 \u05E9\u05D5\u05D1\"");
    }

    async void CheckClaude()
    {
        string claude = FindClaude();
        if (claude == null) { claudeConnected = false; SendClaudeState(null); return; }
        Send(new { type = "claude", installed = true, connected = (bool?)null, checking = true });
        Directory.CreateDirectory(StudyRoot);
        var result = await RunClaude("check", StudyRoot, "Reply with the single word OK.", null, "--max-turns 1", null);
        claudeConnected = result != null && !Bool(result, "is_error");
        SendClaudeState(claudeConnected == true ? null : Str(result, "result"));
    }

    void InstallSkill()
    {
        // Ship the study skill with the app. A copy the user installed themselves is left alone.
        try
        {
            string source = Path.Combine(AppDir, "skill", SkillName);
            string target = Path.Combine(SkillsDir, SkillName);
            if (!Directory.Exists(source)) return;
            if (Directory.Exists(target) && !File.Exists(Path.Combine(target, ".installed-by-hit-student"))) return;
            foreach (string f in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(target, f.Substring(source.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(f, dest, true);
            }
            File.WriteAllText(Path.Combine(target, ".installed-by-hit-student"), "");
        }
        catch { }
    }

    async Task<bool> EnsurePython(Action<string> status)
    {
        if (await Exec("py", "-3 -c \"import fitz\"") == 0) return true;
        if (await Exec("py", "-3 --version") != 0)
        {
            status("\u05DE\u05EA\u05E7\u05D9\u05DF Python (\u05E4\u05E2\u05DD \u05E8\u05D0\u05E9\u05D5\u05E0\u05D4 \u05D1\u05DC\u05D1\u05D3)...");
            await Exec("winget", "install --id Python.Python.3.12 -e --silent --accept-source-agreements --accept-package-agreements");
        }
        status("\u05DE\u05EA\u05E7\u05D9\u05DF PyMuPDF...");
        await Exec("py", "-3 -m pip install --user --quiet pymupdf");
        return await Exec("py", "-3 -c \"import fitz\"") == 0;
    }

    static Task<int> Exec(string exe, string args)
    {
        var tcs = new TaskCompletionSource<int>();
        try
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true },
                EnableRaisingEvents = true
            };
            p.OutputDataReceived += delegate { };
            p.ErrorDataReceived += delegate { };
            p.Exited += delegate { p.WaitForExit(); tcs.TrySetResult(p.ExitCode); p.Dispose(); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }
        catch { tcs.TrySetResult(-1); }
        return tcs.Task;
    }

    // Runs `claude -p` with the prompt on stdin and streams its JSON events. Returns the final "result" event.
    Task<Dictionary<string, object>> RunClaude(string key, string cwd, string prompt, string resume, string extraArgs, Action<Dictionary<string, object>> onEvent)
    {
        var tcs = new TaskCompletionSource<Dictionary<string, object>>();
        string claude = FindClaude();
        if (claude == null) { tcs.SetResult(null); return tcs.Task; }

        string args = "-p --output-format stream-json --verbose --permission-mode acceptEdits"
                    + " --allowedTools \"Bash,Read,Write,Edit,MultiEdit,Glob,Grep,Skill,TodoWrite,WebFetch,WebSearch\""
                    + " --add-dir " + Q(Path.Combine(SkillsDir, SkillName))
                    + (resume != null ? " --resume " + resume : "")
                    + (extraArgs != null ? " " + extraArgs : "");
        var psi = new ProcessStartInfo(claude, args)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = cwd,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        Dictionary<string, object> last = null;
        var errors = new StringBuilder();
        p.OutputDataReceived += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            Dictionary<string, object> ev;
            try { ev = json.Deserialize<Dictionary<string, object>>(e.Data); } catch { return; }
            BeginInvoke((MethodInvoker)delegate
            {
                if (Str(ev, "type") == "result") last = ev;
                if (onEvent != null) { try { onEvent(ev); } catch { } }
            });
        };
        p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
        p.Exited += delegate
        {
            p.WaitForExit();
            p.Dispose();
            BeginInvoke((MethodInvoker)delegate
            {
                claudeRuns.Remove(key);
                if (last == null)
                {
                    string err = errors.ToString().Trim();
                    last = new Dictionary<string, object> { { "type", "result" }, { "is_error", true }, { "result", err.Length > 0 ? err : "Claude stopped without an answer." } };
                }
                tcs.TrySetResult(last);
            });
        };
        try
        {
            p.Start();
            claudeRuns[key] = p;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            byte[] bytes = new UTF8Encoding(false).GetBytes(prompt);
            p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            p.StandardInput.Close();
        }
        catch (Exception ex)
        {
            tcs.TrySetResult(new Dictionary<string, object> { { "type", "result" }, { "is_error", true }, { "result", ex.Message } });
        }
        return tcs.Task;
    }

    static void KillTree(Process p)
    {
        try { Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { CreateNoWindow = true, UseShellExecute = false }); } catch { }
    }

    // Short Hebrew line describing what Claude is doing right now.
    string Describe(Dictionary<string, object> ev, out string text)
    {
        text = null;
        if (Str(ev, "type") != "assistant") return null;
        var msg = ev["message"] as Dictionary<string, object>;
        var content = msg == null ? null : msg["content"] as System.Collections.ArrayList;
        if (content == null) return null;
        string action = null;
        var sb = new StringBuilder();
        foreach (Dictionary<string, object> part in content)
        {
            string t = Str(part, "type");
            if (t == "text") sb.Append(Str(part, "text"));
            else if (t == "tool_use")
            {
                var input = part.ContainsKey("input") ? part["input"] as Dictionary<string, object> : null;
                string file = Path.GetFileName(Str(input, "file_path"));
                switch (Str(part, "name"))
                {
                    case "Read": action = "\u05E7\u05D5\u05E8\u05D0: " + file; break;
                    case "Write": action = "\u05DB\u05D5\u05EA\u05D1: " + file; break;
                    case "Edit": case "MultiEdit": action = "\u05E2\u05D5\u05E8\u05DA: " + file; break;
                    case "Bash":
                        string d = Str(input, "description");
                        action = "\u05DE\u05E8\u05D9\u05E5: " + Trunc(d.Length > 0 ? d : Str(input, "command"), 90);
                        break;
                    case "Glob": case "Grep": action = "\u05DE\u05D7\u05E4\u05E9 \u05D1\u05E7\u05D1\u05E6\u05D9\u05DD..."; break;
                    case "Skill": action = "\u05D8\u05D5\u05E2\u05DF \u05D0\u05EA \u05D4\u05E1\u05E7\u05D9\u05DC " + Str(input, "skill"); break;
                    case "TodoWrite": action = "\u05DE\u05E2\u05D3\u05DB\u05DF \u05EA\u05D5\u05DB\u05E0\u05D9\u05EA \u05E2\u05D1\u05D5\u05D3\u05D4"; break;
                    case "WebFetch": case "WebSearch": action = "\u05DE\u05D7\u05E4\u05E9 \u05D1\u05D0\u05D9\u05E0\u05D8\u05E8\u05E0\u05D8..."; break;
                    default: action = Str(part, "name"); break;
                }
            }
        }
        if (sb.Length > 0) text = sb.ToString();
        return action;
    }

    async void Summarize(int courseId, string course, int num, string title)
    {
        string key = "sum|" + course + "|" + num;
        if (claudeRuns.ContainsKey(key)) return;
        string dir = LectureDir(course, num);
        if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
        {
            SectionStatus(course, num, "\u05D0\u05D9\u05DF \u05E2\u05D3\u05D9\u05D9\u05DF \u05E7\u05D1\u05E6\u05D9\u05DD \u05D1\u05E9\u05D9\u05E2\u05D5\u05E8 \u05D4\u05D6\u05D4 - \u05D4\u05D5\u05E8\u05D3 \u05E7\u05D5\u05D3\u05DD", false, true);
            return;
        }
        if (FindClaude() == null)
        {
            SectionStatus(course, num, "Claude Code \u05DC\u05D0 \u05DE\u05D5\u05EA\u05E7\u05DF - \u05E2\u05D1\u05D5\u05E8 \u05DC\u05D4\u05D2\u05D3\u05E8\u05D5\u05EA", false, true);
            return;
        }

        Send(new { type = "claudeRun", key = key, course = course, num = num, busy = true });
        SectionStatus(course, num, "\u05DE\u05DB\u05D9\u05DF \u05DB\u05DC\u05D9\u05DD...", true, false);
        await EnsurePython(s => SectionStatus(course, num, s, true, false));

        string folder = LectureFolderName(num);
        string prompt =
            "\u05D4\u05E9\u05EA\u05DE\u05E9 \u05D1\u05E1\u05E7\u05D9\u05DC " + SkillName + " \u05DB\u05D3\u05D9 \u05DC\u05D1\u05E0\u05D5\u05EA \u05D0\u05EA \u05D7\u05D5\u05DE\u05E8 \u05D4\u05DC\u05D9\u05DE\u05D5\u05D3 \u05E9\u05DC \u05E9\u05D1\u05D5\u05E2 " + num
            + " \u05D1\u05E7\u05D5\u05E8\u05E1 \"" + course + "\" (\u05E0\u05D5\u05E9\u05D0 \u05D4\u05E9\u05D9\u05E2\u05D5\u05E8 \u05D1-Moodle: \"" + title + "\").\n"
            + "\u05D4\u05D7\u05D5\u05DE\u05E8 \u05E0\u05DE\u05E6\u05D0 \u05D1\u05EA\u05D9\u05E7\u05D9\u05D9\u05D4 \"" + folder + "\" (\u05DE\u05E6\u05D2\u05D5\u05EA), \"" + folder + "/" + Exercises + "\" (\u05EA\u05E8\u05D2\u05D9\u05DC\u05D9\u05DD \u05D5\u05E4\u05EA\u05E8\u05D5\u05E0\u05D5\u05EA)"
            + " \u05D5-\"" + folder + "/" + RecordingFolder + "\" (\u05EA\u05DE\u05DC\u05D5\u05DC \u05D4\u05E9\u05D9\u05E2\u05D5\u05E8, \u05D0\u05DD \u05D9\u05E9 - \u05D4\u05E9\u05EA\u05DE\u05E9 \u05D1\u05D5 \u05DB\u05D3\u05D9 \u05DC\u05D4\u05D1\u05D9\u05DF \u05DE\u05D4 \u05D4\u05DE\u05E8\u05E6\u05D4 \u05D4\u05D3\u05D2\u05D9\u05E9; \u05D0\u05DC \u05EA\u05E7\u05E8\u05D0 \u05D0\u05EA \u05E7\u05D5\u05D1\u05E5 \u05D4\u05D5\u05D9\u05D3\u05D0\u05D5).\n"
            + "\u05E9\u05DE\u05D5\u05E8 \u05D0\u05EA \u05D3\u05E3 \u05D4\u05DC\u05D9\u05DE\u05D5\u05D3 \u05D1-\"" + StudyPages + "/\u05E9\u05D1\u05D5\u05E2-" + num + ".html\".\n"
            + "\u05E2\u05D1\u05D5\u05D3 \u05E2\u05D3 \u05D4\u05E1\u05D5\u05E3 \u05D1\u05DC\u05D9 \u05DC\u05E9\u05D0\u05D5\u05DC \u05E9\u05D0\u05DC\u05D5\u05EA: \u05DB\u05E9\u05DE\u05E9\u05D4\u05D5 \u05DC\u05D0 \u05D1\u05E8\u05D5\u05E8, \u05D4\u05D7\u05DC\u05D8 \u05D1\u05E2\u05E6\u05DE\u05DA \u05D5\u05E6\u05D9\u05D9\u05DF \u05D6\u05D0\u05EA. \u05D1\u05E1\u05D5\u05E3 \u05DB\u05EA\u05D5\u05D1 \u05E1\u05D9\u05DB\u05D5\u05DD \u05E7\u05E6\u05E8 \u05D1\u05E2\u05D1\u05E8\u05D9\u05EA.";

        var result = await RunClaude(key, CourseDir(course), prompt, null, null, ev =>
        {
            string text;
            string action = Describe(ev, out text);
            if (action != null) SectionStatus(course, num, "Claude: " + action, true, false);
            if (action != null || text != null) Send(new { type = "claudeLog", key = key, text = action ?? text });
        });

        bool failed = result == null || Bool(result, "is_error");
        string page = StudyPagePath(course, num);
        SectionStatus(course, num, failed ? "Claude \u05E0\u05DB\u05E9\u05DC: " + Trunc(Str(result, "result"), 160)
            : (File.Exists(page) ? "\u05D3\u05E3 \u05D4\u05DC\u05D9\u05DE\u05D5\u05D3 \u05DE\u05D5\u05DB\u05DF" : "Claude \u05E1\u05D9\u05D9\u05DD"), false, failed);
        Send(new { type = "claudeRun", key = key, course = course, num = num, busy = false, result = Str(result, "result"), error = failed });
        SendSectionLocal(course, num);
    }

    async void Chat(string course, string text)
    {
        string key = "chat|" + course;
        if (claudeRuns.ContainsKey(key) || text.Trim().Length == 0) return;
        if (FindClaude() == null)
        {
            Send(new { type = "chat", course = course, role = "error", text = "Claude Code \u05DC\u05D0 \u05DE\u05D5\u05EA\u05E7\u05DF. \u05E2\u05D1\u05D5\u05E8 \u05DC\u05D4\u05D2\u05D3\u05E8\u05D5\u05EA \u05DB\u05D3\u05D9 \u05DC\u05D4\u05EA\u05E7\u05D9\u05DF \u05D5\u05DC\u05D4\u05EA\u05D7\u05D1\u05E8." });
            Send(new { type = "chatDone", course = course });
            return;
        }
        string dir = CourseDir(course);
        Directory.CreateDirectory(dir);
        var meta = LoadMeta(course);
        string session = Str(meta, "chatSession");

        string system = "\u05D0\u05EA\u05D4 \u05E2\u05D5\u05D6\u05E8 \u05DC\u05D9\u05DE\u05D5\u05D3 \u05E9\u05DC \u05E1\u05D8\u05D5\u05D3\u05E0\u05D8 \u05D1-HIT \u05D1\u05E7\u05D5\u05E8\u05E1 \"" + course + "\". "
            + "\u05D4\u05EA\u05D9\u05E7\u05D9\u05D9\u05D4 \u05D4\u05E0\u05D5\u05DB\u05D7\u05D9\u05EA \u05D4\u05D9\u05D0 \u05EA\u05D9\u05E7\u05D9\u05D9\u05EA \u05D4\u05E7\u05D5\u05E8\u05E1: \u05D1\u05DB\u05DC \"\u05E9\u05D9\u05E2\u05D5\u05E8 N\" \u05D9\u05E9 \u05DE\u05E6\u05D2\u05D5\u05EA, \u05D1-\"\u05E9\u05D9\u05E2\u05D5\u05E8 N/\u05EA\u05E8\u05D2\u05D9\u05DC\u05D9\u05DD\" \u05EA\u05E8\u05D2\u05D9\u05DC\u05D9\u05DD \u05D5\u05E4\u05EA\u05E8\u05D5\u05E0\u05D5\u05EA, \u05D5\u05D1-\"\u05E9\u05D9\u05E2\u05D5\u05E8 N/\u05D4\u05E7\u05DC\u05D8\u05D4\" \u05EA\u05DE\u05DC\u05D5\u05DC \u05D4\u05E9\u05D9\u05E2\u05D5\u05E8. "
            + "\u05E7\u05E8\u05D0 \u05D0\u05EA \u05D4\u05D7\u05D5\u05DE\u05E8 \u05D4\u05E8\u05DC\u05D5\u05D5\u05E0\u05D8\u05D9 \u05DC\u05E4\u05E0\u05D9 \u05E9\u05D0\u05EA\u05D4 \u05E2\u05D5\u05E0\u05D4. \u05E2\u05E0\u05D4 \u05D1\u05E2\u05D1\u05E8\u05D9\u05EA, \u05E7\u05E6\u05E8 \u05D5\u05D1\u05E8\u05D5\u05E8. "
            + "\u05D1\u05E9\u05D9\u05E2\u05D5\u05E8\u05D9 \u05D1\u05D9\u05EA: \u05EA\u05DF \u05E7\u05D5\u05D3\u05DD \u05E8\u05DE\u05D6 \u05D5\u05DB\u05D9\u05D5\u05D5\u05DF \u05D5\u05D4\u05E1\u05D1\u05E8 \u05D0\u05EA \u05D4\u05E9\u05D9\u05D8\u05D4, \u05D5\u05E4\u05EA\u05E8\u05D5\u05DF \u05DE\u05DC\u05D0 \u05E8\u05E7 \u05D0\u05DD \u05D4\u05E1\u05D8\u05D5\u05D3\u05E0\u05D8 \u05DE\u05D1\u05E7\u05E9.";

        var result = await RunClaude(key, dir, text, session.Length > 0 ? session : null,
            "--append-system-prompt " + Q(system), ev =>
        {
            string t = Str(ev, "type");
            if (t == "system" && Str(ev, "session_id").Length > 0)
            {
                var m2 = LoadMeta(course);
                m2["chatSession"] = Str(ev, "session_id");
                SaveMeta(course, m2);
            }
            string reply;
            string action = Describe(ev, out reply);
            if (reply != null && reply.Trim().Length > 0) Send(new { type = "chat", course = course, role = "assistant", text = reply });
            if (action != null) Send(new { type = "chatTool", course = course, text = action });
        });

        if (result == null || Bool(result, "is_error"))
        {
            string err = Str(result, "result");
            if (session.Length > 0 && err.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var m3 = LoadMeta(course);
                m3.Remove("chatSession");
                SaveMeta(course, m3);
            }
            Send(new { type = "chat", course = course, role = "error", text = err.Length > 0 ? err : "Claude \u05DC\u05D0 \u05E2\u05E0\u05D4." });
        }
        Send(new { type = "chatDone", course = course });
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
        name = Regex.Replace(name, @"\s+", " ").Trim().TrimEnd('.');
        if (name.Length > 120) name = name.Substring(0, 120).Trim();
        return name.Length > 0 ? name : "Untitled";
    }

    static string Trunc(string s, int max)
    {
        s = (s ?? "").Replace("\n", " ");
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
    }

    static bool Bool(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v;
    }

    static string Q(string s)
    {
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
