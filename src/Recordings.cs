using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

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
    public string Course;             // set for a lesson's recording, so the lesson card can show it
    public int Lecture;
    public Action<Job> Finished;
}

partial class HitStudent
{
    const string TranscriptSuffix = "תמלול"; // תמלול

    readonly List<Job> jobs = new List<Job>();
    readonly Queue<Job> pending = new Queue<Job>();
    int nextJobId = 1;
    bool working;
    TaskCompletionSource<string> playInfoTcs;
    bool zoomCancelled;

    void AddJob(Job j)
    {
        j.Id = nextJobId++;
        jobs.Add(j);
        SendJob(j);
        pending.Enqueue(j);
        Pump();
    }

    void RetryJob(Job job)
    {
        if (job == null || job.State != "failed") return;
        Update(job, "queued", "ממתין...", -1);
        pending.Enqueue(job);
        Pump();
    }

    Job FindJob(Dictionary<string, object> m)
    {
        int id = Int(m, "id");
        return jobs.Find(j => j.Id == id);
    }

    void SendJob(Job j)
    {
        Send(new
        {
            type = "job", id = j.Id, name = j.Name, url = j.Url, state = j.State, status = j.Status, progress = j.Progress,
            hasVideo = j.VideoPath != null, hasTranscript = j.TranscriptPath != null, course = j.Course, num = j.Lecture
        });
    }

    void Update(Job j, string state, string status, int progress)
    {
        j.State = state;
        j.Status = status;
        j.Progress = progress;
        SendJob(j);
    }

    void Log(Job j, string line) { Send(new { type = "log", id = j.Id, line = line }); }

    // manual = the user pasted this link into the lesson, so download it even if it was downloaded before.
    bool QueueRecording(string course, int num, string url, bool manual)
    {
        var meta = LoadMeta(course);
        var done = meta.ContainsKey("recordings") ? ((ArrayList)meta["recordings"]).Cast<object>().Select(o => o.ToString()).ToList() : new List<string>();
        string id = url.Split('?')[0];
        if ((!manual && done.Contains(id)) || jobs.Any(j => j.Url.Split('?')[0] == id && (j.State == "queued" || j.State == "running"))) return false;

        string dir = Path.Combine(LectureDir(course, num), RecordingFolder);
        int already = jobs.Count(j => j.Dir == dir);
        string name = LectureFolderName(num) + (already > 0 ? " חלק " + (already + 1) : ""); // "שיעור N" / "שיעור N חלק 2"
        AddJob(new Job
        {
            Url = url, Name = name, Transcript = true, Dir = dir, Course = course, Lecture = num,
            Finished = j =>
            {
                if (j.State == "done")
                {
                    var m2 = LoadMeta(course);
                    var list = m2.ContainsKey("recordings") ? (ArrayList)m2["recordings"] : new ArrayList();
                    list.Add(id);
                    m2["recordings"] = list;
                    SaveMeta(course, m2);
                }
                SendSectionLocal(course, num);
            }
        });
        return true;
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
            catch (Exception ex) { Log(job, "Error: " + ex.Message); Update(job, "failed", "משהו השתבש - ראה פרטים", 0); }
            if (job.Finished != null) { try { job.Finished(job); } catch { } }
        }
        working = false;
        ui.BringToFront();
        if (zoom.CoreWebView2 != null) zoom.CoreWebView2.Navigate("about:blank");
    }

    async Task RunJob(Job job)
    {
        if (!IsZoomUrl(job.Url))
        {
            Update(job, "failed", "זה לא קישור להקלטת Zoom", 0); // זה לא קישור להקלטת Zoom
            return;
        }
        Update(job, "running", "פותח את ההקלטה...", -1);
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
                Update(job, "failed", "בוטל - לחץ נסה שוב אחרי ההתחברות", 0);
                return;
            }
            string src = zoom.Source == null ? "" : zoom.Source.ToString();
            bool onRecording = src.IndexOf("/rec/", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!shown && ((!onRecording && clock.Elapsed.TotalSeconds > 2) || clock.Elapsed.TotalSeconds > 15))
            {
                shown = true;
                zoomPanel.BringToFront();
                Update(job, "running", "מחכה שתתחבר...", -1);
                Log(job, "Zoom needs a sign-in; showing the browser.");
            }
            if (clock.Elapsed.TotalMinutes > 10)
            {
                Update(job, "failed", "פג הזמן להמתנה ל-Zoom", 0);
                return;
            }
        }
        ui.BringToFront();

        var info = json.Deserialize<Dictionary<string, object>>(playInfoTcs.Task.Result);
        var result = Dict(info, "result");
        string mp4Url = Str(result, "viewMp4Url");
        string transcriptUrl = Str(result, "transcriptUrl");
        string topic = Str(Dict(result, "meet"), "topic");

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
            if (transcriptUrl.Length == 0) Log(job, "This recording has no audio transcript.");
            else
            {
                Update(job, "running", "מוריד תמלול...", -1);
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
                else Log(job, "Couldn't download the transcript.");
            }
        }

        // 3. Video: hand the browser's session to yt-dlp.
        string ytdlp = FindYtDlp();
        if (ytdlp == null)
        {
            Update(job, "running", "מכין את כלי ההורדה (פעם ראשונה בלבד)...", -1);
            await Exec("winget", "install --id yt-dlp.yt-dlp -e --silent --accept-source-agreements --accept-package-agreements");
            ytdlp = FindYtDlp();
        }
        if (ytdlp == null)
        {
            Log(job, "yt-dlp not found. Install it with: winget install yt-dlp.yt-dlp");
            Update(job, "failed", "כלי ההורדה לא מותקן", 0);
            return;
        }
        string cookieFile = Path.Combine(DataDir, "zoom-cookies-" + Guid.NewGuid().ToString("N") + ".txt");
        await WriteCookies(cookieFile);
        string agent = zoom.CoreWebView2.Settings.UserAgent;
        string referer = new Uri(zoom.Source.ToString()).GetLeftPart(UriPartial.Authority) + "/";
        string output = Path.Combine(dir, name.Replace("%", "%%") + ".%(ext)s");
        string common = "--cookies " + Q(cookieFile) + " --user-agent " + Q(agent) + " --referer " + Q(referer)
                      + " --encoding utf-8 --newline --no-mtime --no-playlist --no-exec -o " + Q(output);

        Update(job, "running", "מוריד וידאו...", 0);
        bool ok = false;
        try
        {
            if (mp4Url.StartsWith("https://")) ok = await RunYtDlp(job, ytdlp, common + " -- " + Q(mp4Url));
            if (!ok)
            {
                Log(job, "Trying the Zoom page link instead...");
                ok = await RunYtDlp(job, ytdlp, common + " -- " + Q(job.Url));
            }
        }
        finally { try { File.Delete(cookieFile); } catch { } }

        if (ok)
        {
            foreach (string f in Directory.GetFiles(dir, name + ".*"))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".mp4" || ext == ".m4a" || ext == ".webm" || ext == ".mkv") job.VideoPath = f;
            }
            Update(job, "done", job.TranscriptPath != null ? "הוידאו והתמלול נשמרו" : "הוידאו נשמר", 100);
        }
        else
        {
            Update(job, "failed", job.TranscriptPath != null ? "התמלול נשמר, הוידאו נכשל" : "ההורדה נכשלה - ראה פרטים", 0);
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
            if (!c.Domain.TrimStart('.').EndsWith("zoom.us", StringComparison.OrdinalIgnoreCase)) continue;
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

    Task<bool> RunYtDlp(Job job, string exe, string args)
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
                string status = "מוריד וידאו · " + m.Groups[1].Value + "% מתוך " + m.Groups[2].Value;
                if (m.Groups[4].Success && !m.Groups[4].Value.StartsWith("Unknown")) status += " · נשארו " + m.Groups[4].Value;
                Update(job, "running", status, (int)pct);
                if (pct < 100) return;
            }
        }
        Log(job, line);
    }

    static string FindYtDlp()
    {
        string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages");
        if (Directory.Exists(packages))
        {
            foreach (string dir in Directory.GetDirectories(packages, "yt-dlp.yt-dlp_*"))
            {
                string exe = Path.Combine(dir, "yt-dlp.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        return FindOnPath("yt-dlp.exe");
    }
}
