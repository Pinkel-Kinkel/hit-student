using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.WinForms;

// CS24 (cs24.co.il) is a free student drive: folders are /drive?d=<id>, files are /drive?f=<id>, and a file's page shows
// it from a public assets.cs24.co.il address. The app walks the course's folder in a hidden browser like a student would,
// one page at a time, and saves the files for the student's own use. Nothing is uploaded or shared anywhere.
partial class HitStudent
{
    const string Cs24Base = "https://cs24.co.il";
    const string Cs24DefaultRoot = "https://cs24.co.il/drive?d=e47d12";
    const int Cs24MaxFiles = 400;

    WebView2 cs24View;

    class Cs24Entry { public string Name, Href; public bool Folder; }

    async Task<string> Cs24Open(string url)
    {
        cs24View.CoreWebView2.Navigate(url);
        // The drive is a single-page app: wait until its list (or a file viewer) is on the page.
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(500);
            try
            {
                string v = await EvalAsync(cs24View, "return location.href.includes(" + json.Serialize(url.Substring(Cs24Base.Length)) + ") && !!document.querySelector('a[href^=\"/drive?\"], iframe[src*=\"assets.cs24.co.il\"]');", 3000);
                if (json.Deserialize<bool>(v)) { await Task.Delay(600); return url; }
            }
            catch { }
        }
        return url;
    }

    async Task<List<Cs24Entry>> Cs24List(string url)
    {
        await Cs24Open(url);
        string v = await EvalAsync(cs24View, @"
const clean = (t) => (t || '').replace(/הוסף למועדפים|סמן כהושלם/g, '').replace(/\s+/g, ' ')
  .replace(/\s*\d{1,2}\s+ב?[֐-׿]+\s+\d{4}.*$/, '')   // the list shows the name followed by a date, e.g. 26 ביולי 2025
  .replace(/[\s\-–·]+$/, '').trim();
return [...document.querySelectorAll('a[href^=""/drive?d=""], a[href^=""/drive?f=""]')]
  .filter(a => !a.closest('header, nav, footer') && clean(a.innerText))
  .map(a => ({ name: clean(a.innerText), href: a.getAttribute('href'), folder: a.getAttribute('href').startsWith('/drive?d=') }));", 15000);
        var list = new List<Cs24Entry>();
        foreach (Dictionary<string, object> e in json.Deserialize<System.Collections.ArrayList>(v))
            list.Add(new Cs24Entry { Name = Str(e, "name"), Href = Cs24Base + Str(e, "href"), Folder = Bool(e, "folder") });
        // The breadcrumb / parent links point back up; keep only entries that are not the page itself.
        return list.Where(e => e.Href != url).GroupBy(e => e.Href).Select(g => g.First()).ToList();
    }

    // Course names differ a little between Moodle and CS24 ("פיסיקה 1 למהנדסים" vs "פיזיקה 1"), so compare words loosely.
    static int Cs24Match(string course, string folder)
    {
        string[] stop = { "בזום", "למהנדסים", "מהנדסים", "קורס", "hit" };
        Func<string, List<string>> words = s => Loose(s).Split(' ').Where(w => w.Length > 0 && !stop.Contains(w)).ToList();
        var c = words(course);
        var f = words(folder);
        if (f.Count == 0) return 0;
        int hit = 0;
        foreach (string w in f)
        {
            if (c.Contains(w)) { hit++; continue; }
            if (w.Length >= 4 && c.Any(x => x.Length >= 4 && EditDistance(x, w) <= 1)) { hit++; continue; }
            return 0;   // every word of the CS24 folder name must appear in the course name
        }
        return hit * 10 - Math.Abs(c.Count - f.Count);
    }

    static int EditDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    async Task<Cs24Entry> Cs24FindCourse(string course)
    {
        string root = Str(settings, "cs24Root");
        if (root.Length == 0) root = Cs24DefaultRoot;
        var best = (await Cs24List(root)).Where(e => e.Folder).Select(e => new { e, s = Cs24Match(course, e.Name) }).Where(x => x.s > 0).OrderByDescending(x => x.s).FirstOrDefault();
        return best == null ? null : best.e;
    }

    async Task<string> Cs24FileUrl(string filePage)
    {
        await Cs24Open(filePage);
        string v = await EvalAsync(cs24View, @"
const f = document.querySelector('iframe[src*=""assets.cs24.co.il""], embed[src*=""assets.cs24.co.il""], img[src*=""assets.cs24.co.il""], a[href*=""assets.cs24.co.il""], video[src*=""assets.cs24.co.il""]');
return f ? (f.getAttribute('src') || f.getAttribute('href')) : '';", 8000);
        string url = json.Deserialize<string>(v);
        Uri u;
        return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("assets.cs24.co.il", StringComparison.OrdinalIgnoreCase) ? u.AbsoluteUri : null;
    }

    readonly HashSet<string> cs24Running = new HashSet<string>();
    // There is one hidden CS24 browser, so courses take turns; Moodle downloads of other courses carry on meanwhile.
    readonly System.Threading.SemaphoreSlim cs24Lock = new System.Threading.SemaphoreSlim(1, 1);

    // While a course is being set up, its downloads share one progress bar: Moodle first, then CS24.
    readonly Dictionary<string, double[]> progressRange = new Dictionary<string, double[]>();

    void Progress(string course, string text, double fraction)
    {
        double[] r;
        if (!progressRange.TryGetValue(course, out r)) r = new[] { 0.0, 1.0 };
        int pct = (int)Math.Round(100 * (r[0] + (r[1] - r[0]) * Math.Max(0, Math.Min(1, fraction))));
        Send(new { type = "courseStatus", course = course, busy = true, text = text, pct = pct, setup = progressRange.ContainsKey(course) });
    }

    // Downloads the whole CS24 folder of a course: exams/quizzes into "מבחנים\CS24", everything else into "CS24".
    async Task<int> Cs24SyncCourse(string course)
    {
        if (!cs24Running.Add(course)) return 0;
        int saved = 0;
        bool locked = false;
        try
        {
            if (cs24Lock.CurrentCount == 0) Progress(course, "מחכה לתורו ב-CS24 (קורס אחר מוריד משם עכשיו)...", 0);
            await cs24Lock.WaitAsync();
            locked = true;
            Progress(course, "מחפש את הקורס ב-CS24...", 0);
            var folder = await Cs24FindCourse(course);
            if (folder == null)
            {
                Send(new { type = "courseStatus", course = course, busy = false, text = "לא מצאתי את הקורס ב-CS24. אפשר להוריד ידנית מהלשונית CS24 בצד.", error = false });
                return 0;
            }
            var meta = LoadMeta(course);
            meta["cs24Folder"] = folder.Href;
            SaveMeta(course, meta);

            // Walk the folder tree (breadth first, a few levels deep) and collect the file pages.
            var files = new List<Tuple<string, Cs24Entry>>();
            var queue = new Queue<Tuple<string, Cs24Entry, int>>();
            int visited = 0;
            queue.Enqueue(Tuple.Create("", folder, 0));
            while (queue.Count > 0 && files.Count < Cs24MaxFiles)
            {
                var cur = queue.Dequeue();
                visited++;
                // Listing the folders is about a tenth of the work; the downloads are the rest.
                Progress(course, "בודק מה יש ב-CS24: " + Trunc(cur.Item1.Length > 0 ? cur.Item1 : folder.Name, 60), 0.1 * visited / (visited + queue.Count + 1));
                foreach (var e in await Cs24List(cur.Item2.Href))
                {
                    string rel = cur.Item1.Length > 0 ? cur.Item1 + "\\" + SafeName(e.Name) : SafeName(e.Name);
                    if (e.Folder) { if (cur.Item3 < 3) queue.Enqueue(Tuple.Create(rel, e, cur.Item3 + 1)); }
                    else files.Add(Tuple.Create(cur.Item1, e));
                }
            }

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SumIT/" + Version);
                http.Timeout = TimeSpan.FromMinutes(10);
                for (int i = 0; i < files.Count; i++)
                {
                    var f = files[i];
                    Progress(course, "מוריד מ-CS24 (" + (i + 1) + " מתוך " + files.Count + "): " + Trunc(f.Item2.Name, 50), 0.1 + 0.9 * i / files.Count);
                    try
                    {
                        string asset = await Cs24FileUrl(f.Item2.Href);
                        if (asset == null) continue;
                        bool exam = Regex.IsMatch(f.Item1 + " " + f.Item2.Name, "(מבחן|מבחנים|בוחן|בחנים|מועד)");
                        string dir = exam ? Path.Combine(CourseDir(course), Exams, Cs24Folder, f.Item1) : Path.Combine(CourseDir(course), Cs24Folder, f.Item1);
                        string ext = Path.GetExtension(Uri.UnescapeDataString(new Uri(asset).AbsolutePath));
                        string name = SafeName(f.Item2.Name);
                        if (ext.Length > 0 && !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name += ext;
                        string path = Path.Combine(dir, name);
                        if (!IsInside(path, CourseDir(course))) continue;
                        if (File.Exists(path)) continue;
                        Directory.CreateDirectory(dir);
                        using (var resp = await http.GetAsync(asset, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (!resp.IsSuccessStatusCode) continue;
                            if (resp.Content.Headers.ContentLength.HasValue && resp.Content.Headers.ContentLength.Value > 200L * 1024 * 1024) continue;
                            string temp = path + ".part";
                            using (var fs = File.Create(temp)) await resp.Content.CopyToAsync(fs);
                            File.Move(temp, path);
                            saved++;
                        }
                    }
                    catch { }
                    await Task.Delay(250);   // gentle: one file at a time
                }
            }
            Send(new { type = "courseStatus", course = course, busy = false, text = saved > 0 ? "ירדו " + saved + " קבצים מ-CS24" : "", error = false });
        }
        catch (Exception ex) { Send(new { type = "courseStatus", course = course, busy = false, text = "CS24: " + ex.Message, error = true }); }
        finally
        {
            cs24Running.Remove(course);
            if (locked) cs24Lock.Release();
            if (cs24View.CoreWebView2 != null) cs24View.CoreWebView2.Navigate("about:blank");
            SendCourseLocal(course);
        }
        return saved;
    }

    async void Cs24Sync(string course) { await Cs24SyncCourse(course); }

    // The whole setup in one go, after the student answered the questions: Moodle, then CS24, then the study material.
    async void SetupRun(string course, System.Collections.ArrayList sections, bool cs24)
    {
        var meta = LoadMeta(course);
        meta["setupDone"] = true;
        SaveMeta(course, meta);
        Send(new { type = "setupRun", course = course, busy = true });
        try
        {
            progressRange[course] = new[] { 0.0, cs24 ? 0.3 : 1.0 };
            await SyncCourseAsync(course, sections);
            if (cs24)
            {
                progressRange[course] = new[] { 0.3, 1.0 };
                await Cs24SyncCourse(course);
            }
        }
        finally { progressRange.Remove(course); }
        Send(new { type = "setupRun", course = course, busy = false });
        LearnCourse(course, sections);
    }
}
