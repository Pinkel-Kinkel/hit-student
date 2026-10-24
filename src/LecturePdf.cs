using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// The main thing the student gets for each lecture: one PDF with a full, focused summary of the lecture followed by
// its homework, where every question says which part of the summary it uses and how this lecturer expects it solved.
// The AI builds it with the study skill (study page + exercise sheets with pointers); the app then prints the page
// with Edge and joins it with the pointer sheets into a single file.
partial class HitStudent
{
    const string LecturePdfs = "סיכומי הרצאות";

    string LecturePdfPath(string course, int num)
    {
        return Path.Combine(CourseDir(course), LecturePdfs, "הרצאה " + num + " - סיכום ושיעורי בית.pdf");
    }

    async void LecturePdf(string course, int num, string title) { await LecturePdfAsync(course, num, title, true); }

    async Task<AiResult> LecturePdfAsync(string course, int num, string title, bool open = false)
    {
        string key = "sum|" + course + "|" + num;
        if (aiRuns.ContainsKey(key)) return null;
        if (!AiReadyOrSay(t => SectionStatus(course, num, t, false, true))) return null;
        string dir = LectureDir(course, num);
        string transcript = Path.Combine(CourseDir(course), LectureRecordings, "הרצאה " + num + " " + TranscriptSuffix + ".txt");
        if (CountFiles(dir) == 0 && !File.Exists(transcript)) { SectionStatus(course, num, "אין עדיין חומר להרצאה הזו - הורד קודם", false, true); return null; }

        string folder = LectureFolderName(num);
        bool homework = CountFiles(Path.Combine(dir, Exercises)) > 0;
        string prompt = SkillInstruction() + " בנה את חומר הלימוד של הרצאה " + num + " (נושא ב-Moodle: \"" + title + "\"). התוצר יודפס ל-PDF אחד שהסטודנט ילמד ממנו.\n"
            + CourseContext(course) + "\n"
            + "החומר של ההרצאה: התיקייה \"" + folder + "\"" + (File.Exists(transcript) ? ", התמלול \"" + LectureRecordings + "/" + Path.GetFileName(transcript) + "\"" : "")
            + ", והערות מהשיעור ב-\"" + Notebook + "\" אם יש.\n"
            + "כתוב את דף הלימוד \"" + StudyPages + "/שבוע-" + num + ".html\" לפי הסקיל, ובו:\n"
            + "1. סיכום של כל החומר של ההרצאה - מפורט וברור ככל האפשר, בלי פרטים לא חשובים: כל נושא שנלמד, ההסברים, כרטיסי הנוסחאות, הדוגמאות של המרצה, והפערים שהמצגות לא מסבירות.\n"
            + (homework
                ? "2. שיעורי הבית של ההרצאה (בתיקייה \"" + folder + "/" + Exercises + "\"): לכל שאלה - לאיזה חלק בסיכום היא קשורה (כתוב את מספר ושם הסעיף במפורש, לא רק קישור), איך לגשת אליה צעד אחר צעד, ואיך המרצה מצפה שיענו עליה (שלבים, סימונים, רמת פירוט) לפי הפתרונות שלו לשיעורי בית ולמבחנים קודמים. "
                  + "צור גם את קבצי ה-PDF עם המפנים ב-\"" + folder + "/" + Exercises + "/" + PointersFolder + "\".\n"
                : "2. אין עדיין שיעורי בית להרצאה הזו - במקום החלק \"שימוש בתרגילים\" תן 2-3 שאלות תרגול בסגנון של המרצה (מהמבחנים הקודמים, אם יש).\n")
            + "הדף יודפס, אז כל טקסט חשוב צריך להיות כתוב בדף עצמו (לא רק בתוך קישור או אנימציה)." + WorkAlone;

        var result = await CourseTask(key, course, s => SectionStatus(course, num, s, true, false), prompt);
        string pdf = null;
        if (!result.Error && File.Exists(StudyPagePath(course, num)))
        {
            SectionStatus(course, num, "מכין את קובץ ה-PDF...", true, false);
            pdf = await BuildLecturePdf(course, num);
        }
        SectionStatus(course, num, result.Error ? "נכשל: " + Trunc(result.Text, 160)
            : (pdf != null ? "קובץ ה-PDF של ההרצאה מוכן ✨" : "דף הלימוד מוכן, אבל לא הצלחתי ליצור ממנו PDF"), false, result.Error || pdf == null);
        SendSectionLocal(course, num);
        if (pdf != null) { Notify("הסיכום של הרצאה " + num + " מוכן", course); if (open) OpenLocal(pdf); }
        return result;
    }

    // Study page (+ the exercise sheets with pointers) -> one PDF. Returns its path, or null.
    async Task<string> BuildLecturePdf(string course, int num)
    {
        string edge = FindEdge();
        if (edge == null || !await EnsurePython(s => { })) return null;
        string page = StudyPagePath(course, num);
        string work = Path.Combine(Path.GetTempPath(), "SumIT-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        // A printable copy next to the original (so its relative links still work): every card open, no buttons.
        string printable = Path.Combine(Path.GetDirectoryName(page), ".print-" + num + ".html");
        try
        {
            string html = File.ReadAllText(page, Encoding.UTF8);
            const string extra = "<style>@media print{details>summary .chev,button,input[type=checkbox],.no-print{display:none!important}"
                + "details,.card,.fx{break-inside:avoid-page} h2{break-after:avoid} body{background:#fff}}</style>"
                + "<script>addEventListener('DOMContentLoaded',function(){document.querySelectorAll('details').forEach(function(d){d.open=true})})</script>";
            int head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            html = head >= 0 ? html.Insert(head, extra) : extra + html;
            File.WriteAllText(printable, html, new UTF8Encoding(false));

            string summary = Path.Combine(work, "summary.pdf");
            // Its own profile, so it never hands the job to an Edge window the student has open.
            await Exec(edge, "--headless=new --disable-gpu --no-first-run --no-pdf-header-footer --run-all-compositor-stages-before-draw --virtual-time-budget=25000"
                + " --user-data-dir=" + Q(Path.Combine(work, "profile")) + " --print-to-pdf=" + Q(summary) + " " + Q(new Uri(printable).AbsoluteUri), null, 120000);
            if (!File.Exists(summary)) return null;

            // The summary first, then each homework sheet (with pointers when there are, the original otherwise).
            var parts = new List<string> { "סיכום הרצאה " + num, summary };
            string exercises = Path.Combine(LectureDir(course, num), Exercises);
            string pointers = Path.Combine(exercises, PointersFolder);
            var sheets = Directory.Exists(pointers) ? Directory.GetFiles(pointers, "*.pdf").OrderBy(f => f).ToList() : new List<string>();
            if (sheets.Count == 0 && Directory.Exists(exercises)) sheets = Directory.GetFiles(exercises, "*.pdf").OrderBy(f => f).ToList();
            foreach (string s in sheets) { parts.Add("שיעורי בית: " + Path.GetFileNameWithoutExtension(s).Replace(" - מפנים", "")); parts.Add(s); }

            string script = Path.Combine(work, "merge.py");
            File.WriteAllText(script, MergeScript, new UTF8Encoding(false));
            string outPdf = LecturePdfPath(course, num);
            Directory.CreateDirectory(Path.GetDirectoryName(outPdf));
            string args = "-3 -I " + Q(script) + " " + Q(outPdf) + " " + string.Join(" ", parts.Select(Q));
            var r = await Exec("py", args, null, 120000, new Dictionary<string, string> { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } });
            return r.Item1 == 0 && File.Exists(outPdf) ? outPdf : null;
        }
        catch { return null; }
        finally
        {
            try { File.Delete(printable); } catch { }
            try { Directory.Delete(work, true); } catch { }
        }
    }

    // Joins PDFs into one, with a bookmark per part. Arguments: out.pdf, then pairs of (bookmark title, file).
    const string MergeScript = @"import sys, pymupdf
out, rest = sys.argv[1], sys.argv[2:]
doc, toc = pymupdf.open(), []
for title, path in zip(rest[0::2], rest[1::2]):
    try:
        src = pymupdf.open(path)
    except Exception:
        continue
    toc.append([1, title, doc.page_count + 1])
    doc.insert_pdf(src)
if doc.page_count == 0:
    sys.exit(1)
doc.set_toc(toc)
doc.save(out, garbage=3, deflate=True)
";

    static string FindEdge()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
        {
            string p = Path.Combine(Environment.GetFolderPath(root), "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
