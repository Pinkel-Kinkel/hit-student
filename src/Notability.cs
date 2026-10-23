using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

// Notability (iPad) can auto-backup every note as a PDF to Google Drive / OneDrive / Dropbox, one folder per subject.
// When that folder is synced to this PC, each course is linked to one subject folder and its notes are copied
// into the course's "מחברת" folder, where the study pages and the chat can use them.
partial class HitStudent
{
    // Opens the Notability app installed on this PC (Microsoft Store apps are started by their app id).
    // Without it, Notability on the web opens inside the app instead.
    async void OpenNotability()
    {
        var r = await Exec("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-StartApps | Where-Object { $_.Name -like '*Notability*' } | Select-Object -First 1).AppID\"", null, 20000);
        string appId = r.Item2.Trim();
        if (r.Item1 == 0 && appId.Length > 0 && !appId.Contains(" ") && !appId.Contains("\""))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + appId) { UseShellExecute = true });
            Toast("פותח את Notability. מחברת שתייצא ל-PDF לתיקיית \"מחברת\" של הקורס תיכנס לסיכומים.", false);
            return;
        }
        Send(new { type = "openSite", site = "notability" });
    }

    List<string> NotabilitySubjects()
    {
        string root = NotabilityDir;
        if (root.Length == 0 || !Directory.Exists(root)) return new List<string>();
        var subs = Directory.GetDirectories(root).Select(Path.GetFileName).Where(n => !n.StartsWith(".")).OrderBy(n => n).ToList();
        return subs;
    }

    string GuessSubject(string course, List<string> subjects)
    {
        string c = Loose(course);
        foreach (string s in subjects)
        {
            string l = Loose(s);
            if (l.Length >= 2 && (c.Contains(l) || l.Contains(c))) return s;
        }
        // First word match ("אינפי" in "אינפי 2 תשפ"ו")
        string first = c.Split(' ').FirstOrDefault() ?? "";
        return first.Length >= 3 ? subjects.FirstOrDefault(s => Loose(s).Contains(first)) : null;
    }

    object NotabilityInfo(string course)
    {
        var subjects = NotabilitySubjects();
        string linked = Str(LoadMeta(course), "notability");
        return new
        {
            configured = NotabilityDir.Length > 0 && Directory.Exists(NotabilityDir),
            linked = linked.Length > 0 ? linked : null,
            guess = linked.Length > 0 ? null : GuessSubject(course, subjects),
            subjects = subjects,
            notes = CountFiles(Path.Combine(CourseDir(course), Notebook))
        };
    }

    void SendNotabilityFolders() { Send(new { type = "notabilitySubjects", subjects = NotabilitySubjects() }); }

    void LinkNotability(string course, string subject)
    {
        var meta = LoadMeta(course);
        if (subject.Length == 0) meta.Remove("notability");
        else if (NotabilitySubjects().Contains(subject)) meta["notability"] = subject;
        else return;
        SaveMeta(course, meta);
        int added = ImportNotability(course);
        if (added > 0) Toast(added + " דפי מחברת יובאו מ-Notability", false);
        SendCourseLocal(course);
    }

    void ImportAllNotability()
    {
        if (NotabilityDir.Length == 0 || !Directory.Exists(NotabilityDir) || !Directory.Exists(StudyRoot)) return;
        foreach (string dir in Directory.GetDirectories(StudyRoot))
        {
            string course = Path.GetFileName(dir);
            if (Str(LoadMeta(course), "notability").Length > 0) ImportNotability(course);
        }
    }

    // Copies new or changed PDFs; returns how many were copied.
    int ImportNotability(string course)
    {
        string subject = Str(LoadMeta(course), "notability");
        if (subject.Length == 0 || NotabilityDir.Length == 0) return 0;
        string src = Path.Combine(NotabilityDir, SafeName(subject));
        if (!Directory.Exists(src) || !IsInside(src, NotabilityDir)) return 0;
        string dst = Path.Combine(CourseDir(course), Notebook);
        int added = 0;
        try
        {
            foreach (string f in Directory.GetFiles(src, "*.pdf", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.Length).TrimStart('\\');
                string target = Path.Combine(dst, rel);
                if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(f)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target, true);
                added++;
            }
        }
        catch { }
        return added;
    }
}
