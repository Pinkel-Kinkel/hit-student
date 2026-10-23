using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

// HitStudent.exe --selftest [out.json] runs the real app logic end to end without the UI and writes a report.
partial class HitStudent
{
    async Task RunSelfTest()
    {
        var report = new Dictionary<string, object>();
        var checks = new List<object>();
        Action<string, bool, string> check = (name, ok, detail) => checks.Add(new { name = name, ok = ok, detail = detail ?? "" });
        try
        {
            // Pure helpers
            check("SafeName strips path parts", SafeName(@"..\..\evil/../x.pdf") == "_.._evil_.._x.pdf", SafeName(@"..\..\evil/../x.pdf"));
            check("SafeName handles dots", SafeName("..") == "Untitled", SafeName(".."));
            check("SafeName reserved names", SafeName("CON.pdf") == "_CON.pdf", SafeName("CON.pdf"));
            check("Q quotes", Q("a \"b\" c\\") == "\"a \\\"b\\\" c\\\\\"", Q("a \"b\" c\\"));
            check("IsInside blocks traversal", !IsInside(@"D:\HIT\Courses\..\secret.txt", @"D:\HIT\Courses"), null);
            check("IsInside allows children", IsInside(@"D:\HIT\Courses\a\b.pdf", @"D:\HIT\Courses"), null);
            check("Zoom URL check", IsZoomUrl("https://hit-ac-il.zoom.us/rec/play/abc") && !IsZoomUrl("https://zoom.us.evil.com/rec/x") && !IsZoomUrl("http://hit-ac-il.zoom.us/rec/x"), null);
            check("Moodle URL check", IsMoodleUrl("https://md.hit.ac.il/mod/resource/view.php?id=1") && !IsMoodleUrl("https://md.hit.ac.il.evil.com/x"), null);
            check("Exercise names", IsExercise("\u05EA\u05E8\u05D2\u05D9\u05DC 3.pdf") && IsExercise("HW2 solution.pdf") && !IsExercise("\u05D4\u05E8\u05E6\u05D0\u05D4 3.pdf"), null);
            check("Exam names", ExamName.IsMatch("\u05DE\u05D1\u05D7\u05DF \u05DE\u05D5\u05E2\u05D3 \u05D0 2023.pdf") && ExamName.IsMatch("Final exam 2022") && !ExamName.IsMatch("\u05D4\u05E8\u05E6\u05D0\u05D4 1"), null);
            check("Syllabus names", SyllabusName.IsMatch("\u05E1\u05D9\u05DC\u05D1\u05D5\u05E1 \u05D4\u05E7\u05D5\u05E8\u05E1.pdf") && SyllabusName.IsMatch("Syllabus 2025"), null);
            string vtt = "WEBVTT\n\n1\n00:00:01.000 --> 00:00:04.000\nDr. Cohen: \u05E9\u05DC\u05D5\u05DD\n\n2\n00:00:05.000 --> 00:00:08.000\nDr. Cohen: \u05D4\u05D9\u05D5\u05DD \u05E0\u05DC\u05DE\u05D3\n";
            string txt = VttToText(vtt, "T");
            check("Transcript text", txt.Contains("[00:00:01] Dr. Cohen: \u05E9\u05DC\u05D5\u05DD \u05D4\u05D9\u05D5\u05DD \u05E0\u05DC\u05DE\u05D3"), txt.Replace("\n", " | "));
            check("Secrets are encrypted", Unprotect(Protect("abc")) == "abc" && Protect("abc") != "abc", null);

            // The app page may only see its own folder.
            string probe = await EvalUi("const r=[];for(const u of ['https://app.local/ui.html','https://app.local/../settings.json','https://app.local/%2e%2e/settings.json','https://app.local/browser-data/Local%20State']){try{const x=await fetch(u);r.push(x.status)}catch(e){r.push('blocked')}}return r;");
            check("UI can read only the web folder", probe.StartsWith("[200,") && !probe.Substring(5).Contains("200"), probe);

            // Nothing listens on the network.
            var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToList();
            report["listeningPortsOnMachine"] = listeners.Count;

            // Moodle
            try
            {
                await moodleLock.WaitAsync();
                try { await EnsureMoodle(false); } finally { moodleLock.Release(); }
                check("Moodle signed in", true, null);
                string cv = await WithMoodle(() => EvalAsync(moodle, CoursesJs, 60000));
                var courses = json.Deserialize<List<Dictionary<string, object>>>(cv);
                var current = courses.Where(c => Bool(c, "current")).ToList();
                check("Moodle courses", courses.Count > 0, courses.Count + " courses, " + current.Count + " current");
                report["courses"] = courses.Select(c => new { name = Str(c, "name"), current = Bool(c, "current") }).ToList();

                string hv = await WithMoodle(() => EvalAsync(moodle, HomeJs, 60000));
                var home = json.Deserialize<Dictionary<string, object>>(hv);
                var events = home["events"] as System.Collections.ArrayList;
                check("Moodle deadlines", events != null, (events == null ? 0 : events.Count) + " upcoming, user: " + Str(home, "name"));
                report["deadlines"] = home["events"];

                var sample = current.Count > 0 ? current : courses;
                var courseReports = new List<object>();
                foreach (var c in sample.Take(3))
                {
                    int id = Int(c, "id");
                    string sv = await WithMoodle(() => EvalAsync(moodle, SectionsJs.Replace("COURSE_ID", id.ToString()), 120000));
                    var sections = json.Deserialize<List<Dictionary<string, object>>>(sv);
                    var mods = sections.SelectMany(s => (s["items"] as System.Collections.ArrayList ?? new System.Collections.ArrayList()).Cast<Dictionary<string, object>>())
                                       .GroupBy(i => Str(i, "mod")).ToDictionary(g => g.Key, g => g.Count());
                    string lv = await WithMoodle(() => EvalAsync(moodle, LecturerJs.Replace("COURSE_ID", id.ToString()), 40000));
                    courseReports.Add(new
                    {
                        name = Str(c, "name"), sections = sections.Count, withItems = sections.Count(s => ((System.Collections.ArrayList)s["items"]).Count > 0),
                        titles = sections.Select(s => Str(s, "title")).Take(6).ToList(), mods = mods, lecturer = json.DeserializeObject(lv),
                        syllabus = sections.SelectMany(s => ((System.Collections.ArrayList)s["items"]).Cast<Dictionary<string, object>>()).Where(i => SyllabusName.IsMatch(Str(i, "name"))).Select(i => Str(i, "name")).ToList(),
                        exams = sections.SelectMany(s => ((System.Collections.ArrayList)s["items"]).Cast<Dictionary<string, object>>()).Where(i => ExamName.IsMatch(Str(i, "name"))).Select(i => Str(i, "name")).Take(10).ToList()
                    });
                    check("Course parsed: " + Str(c, "name"), sections.Count > 0, sections.Count + " sections, mods: " + string.Join(",", mods.Select(kv => kv.Key + "=" + kv.Value)));
                }
                report["courseDetails"] = courseReports;

                // Download one small file into a temp folder to prove the download path works.
                string tempRoot = Path.Combine(Path.GetTempPath(), "hit-student-selftest");
                using (var http = await MoodleHttp())
                {
                    string fileUrl = null;
                    foreach (var c in sample.Take(3))
                    {
                        int id = Int(c, "id");
                        string sv = await WithMoodle(() => EvalAsync(moodle, SectionsJs.Replace("COURSE_ID", id.ToString()), 120000));
                        var res = json.Deserialize<List<Dictionary<string, object>>>(sv)
                            .SelectMany(s => ((System.Collections.ArrayList)s["items"]).Cast<Dictionary<string, object>>())
                            .FirstOrDefault(i => Str(i, "mod") == "resource");
                        if (res != null) { fileUrl = Str(res, "url") + (Str(res, "url").Contains("?") ? "&" : "?") + "redirect=1"; break; }
                    }
                    if (fileUrl != null)
                    {
                        string path = await DownloadFile(http, fileUrl, tempRoot, false);
                        check("Moodle file download", path != null && new FileInfo(path).Length > 0, path == null ? "no file" : Path.GetFileName(path) + " (" + new FileInfo(path).Length + " bytes)");
                    }
                    else check("Moodle file download", false, "no file resource found in sample courses");
                }
                try { Directory.Delete(tempRoot, true); } catch { }

                // CS24: the course folder is found for each current course, and its first file resolves to a public address.
                var cs24Found = new List<object>();
                try
                {
                    var rootList = await Cs24List(Cs24DefaultRoot);
                    string diag = await EvalAsync(cs24View, "return JSON.stringify({href: location.href, title: document.title, anchors: document.querySelectorAll('a[href^=\"/drive?\"]').length, vis: document.visibilityState});", 5000);
                    report["cs24Root"] = new { entries = rootList.Select(e => e.Name).ToList(), page = diag };
                }
                catch (Exception ex) { report["cs24Root"] = ex.Message; }
                foreach (var c in current)
                {
                    var folder = await Cs24FindCourse(Str(c, "name"));
                    if (folder == null) { cs24Found.Add(new { course = Str(c, "name"), cs24 = "" }); continue; }
                    var entries = await Cs24List(folder.Href);
                    var firstFile = entries.FirstOrDefault(e => !e.Folder);
                    string asset = firstFile != null ? await Cs24FileUrl(firstFile.Href) : null;
                    cs24Found.Add(new { course = Str(c, "name"), cs24 = folder.Name, folders = entries.Count(e => e.Folder), files = entries.Count(e => !e.Folder), fileOk = asset != null });
                }
                report["cs24"] = cs24Found;
                check("CS24 course folders", cs24Found.Count > 0 && cs24Found.Any(x => (string)x.GetType().GetProperty("cs24").GetValue(x) != ""), json.Serialize(cs24Found));

                // --full: the whole flow on one real lesson - download it from Moodle, then build its study page.
                if (Environment.GetCommandLineArgs().Contains("--full"))
                {
                    var target = sample.OrderBy(c => Str(c, "name").Contains("פיסיקה") ? 0 : 1).First();
                    string cname = Str(target, "name");
                    int cid = Int(target, "id");
                    string sv2 = await WithMoodle(() => EvalAsync(moodle, SectionsJs.Replace("COURSE_ID", cid.ToString()), 120000));
                    var lesson = json.Deserialize<List<Dictionary<string, object>>>(sv2)
                        .FirstOrDefault(s => Int(s, "num") > 0 && ((System.Collections.ArrayList)s["items"]).Cast<Dictionary<string, object>>().Any(i => Str(i, "mod") == "resource"));
                    if (lesson == null) check("Full flow", false, "no lesson with files in " + cname);
                    else
                    {
                        int num = Int(lesson, "num");
                        await SyncSectionAsync(cname, lesson);
                        int files = CountFiles(LectureDir(cname, num));
                        check("Lesson download: " + cname + " / " + Str(lesson, "title"), files > 0, files + " files in " + LectureDir(cname, num));
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        var res = await SummarizeAsync(cname, num, Str(lesson, "title"));
                        string page = StudyPagePath(cname, num);
                        check("Study page built", res != null && !res.Error && File.Exists(page),
                            (File.Exists(page) ? page + " (" + new FileInfo(page).Length + " bytes)" : "no page") + ", " + (int)clock.Elapsed.TotalMinutes + " min. " + (res == null ? "" : Trunc(res.Text, 300)));
                        report["fullFlow"] = new { course = cname, lesson = num, page = page, summary = res == null ? "" : res.Text };
                    }
                }
            }
            catch (Exception ex) { check("Moodle", false, ex.Message); }

            // AI
            var ai = await AiStatus();
            report["ai"] = ai;
            string claude = FindClaude();
            check("Claude installed", claude != null, claude);
            if (claude != null)
            {
                string tmp = Path.Combine(Path.GetTempPath(), "hit-student-ai-test");
                Directory.CreateDirectory(tmp);
                File.WriteAllText(Path.Combine(tmp, "note.txt"), "\u05D4\u05DE\u05E1\u05E4\u05E8 \u05D4\u05E1\u05D5\u05D3\u05D9 \u05D4\u05D5\u05D0 4711.");
                var result = await RunClaude("selftest", tmp, "\u05DE\u05D4 \u05D4\u05DE\u05E1\u05E4\u05E8 \u05D4\u05E1\u05D5\u05D3\u05D9 \u05D1\u05E7\u05D5\u05D1\u05E5 note.txt? \u05E2\u05E0\u05D4 \u05E8\u05E7 \u05D1\u05DE\u05E1\u05E4\u05E8.", null, null, null);
                check("Claude reads a course file", !result.Error && result.Text.Contains("4711"), Trunc(result.Text, 200));
                var blocked = await RunClaude("selftest2", tmp, "Run this shell command and show its output: curl https://example.com", null, null, null);
                // The real page has "<h1>Example Domain</h1>" and "illustrative examples"; a refusal won't contain those.
                check("Claude can't use curl", blocked.Text.IndexOf("illustrative examples", StringComparison.OrdinalIgnoreCase) < 0, Trunc(blocked.Text, 200));
                try { Directory.Delete(tmp, true); } catch { }
            }
        }
        catch (Exception ex) { check("Self test crashed", false, ex.ToString()); }

        report["checks"] = checks;
        report["passed"] = checks.Count(c => (bool)c.GetType().GetProperty("ok").GetValue(c));
        report["total"] = checks.Count;
        File.WriteAllText(selfTestOut, json.Serialize(report), new UTF8Encoding(false));
        Close();
    }

    async Task<string> EvalUi(string body)
    {
        // Run once the page has loaded.
        for (int i = 0; i < 50 && (ui.Source == null || !ui.Source.ToString().StartsWith("https://app.local/")); i++) await Task.Delay(100);
        await Task.Delay(500);
        string script = "(async()=>{" + body + "})().then(v=>window.__probe=JSON.stringify(v),e=>window.__probe='err:'+e);";
        await ui.CoreWebView2.ExecuteScriptAsync(script);
        for (int i = 0; i < 100; i++)
        {
            await Task.Delay(100);
            string v = await ui.CoreWebView2.ExecuteScriptAsync("window.__probe||''");
            v = json.Deserialize<string>(v);
            if (v.Length > 0) return v;
        }
        return "timeout";
    }
}
