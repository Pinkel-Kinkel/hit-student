using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// During the semester the lecturer keeps uploading. The app remembers every Moodle item it has already downloaded,
// and when a course shows something new it tells the student and asks before downloading it.
partial class HitStudent
{
    static readonly string[] IgnoredMods = { "forum", "quiz", "url", "label" };
    DateTime lastNewCheck = DateTime.MinValue;

    static bool Watched(Dictionary<string, object> item)
    {
        return Str(item, "url").Length > 0 && !IgnoredMods.Contains(Str(item, "mod"));
    }

    static IEnumerable<Dictionary<string, object>> ItemsOf(Dictionary<string, object> section)
    {
        var items = section.ContainsKey("items") ? section["items"] as IEnumerable : null;
        if (items == null) yield break;
        foreach (Dictionary<string, object> it in items) yield return it;
    }

    HashSet<string> KnownItems(Dictionary<string, object> meta)
    {
        var list = meta.ContainsKey("knownItems") ? meta["knownItems"] as IEnumerable : null;
        return list == null ? new HashSet<string>() : new HashSet<string>(list.Cast<object>().Select(o => o.ToString()));
    }

    void RememberItems(string course, IEnumerable<Dictionary<string, object>> items)
    {
        var meta = LoadMeta(course);
        var known = KnownItems(meta);
        int before = known.Count;
        foreach (var it in items) if (Watched(it)) known.Add(Str(it, "url"));
        if (known.Count == before && meta.ContainsKey("knownItems")) return;
        meta["knownItems"] = known.ToList();
        SaveMeta(course, meta);
    }

    // Tells the page (and, when asked, Windows) about items on Moodle that were never downloaded. Returns how many.
    int CheckNewMaterial(string course, IEnumerable sections, bool notify)
    {
        var meta = LoadMeta(course);
        if (!Bool(meta, "setupDone")) return 0;
        var list = sections.Cast<Dictionary<string, object>>().ToList();
        // First look after this feature arrived: what is there now counts as known.
        if (!meta.ContainsKey("knownItems")) { RememberItems(course, list.SelectMany(ItemsOf)); return 0; }
        var known = KnownItems(meta);
        var fresh = new List<object>();
        foreach (var s in list)
            foreach (var it in ItemsOf(s))
                if (Watched(it) && !known.Contains(Str(it, "url")))
                    fresh.Add(new { num = Int(s, "num"), title = Str(s, "title"), name = Str(it, "name"), mod = Str(it, "mod") });
        if (fresh.Count == 0) return 0;
        Send(new { type = "newMaterial", course = course, items = fresh });
        if (notify) Notify("יש חומר חדש ב-Moodle בקורס " + course + " (" + fresh.Count + ")", course);
        return fresh.Count;
    }

    // The student said yes: download the lessons with new items, then rebuild the summary PDF of each lecture that changed.
    async void GetNewMaterial(string course, ArrayList sections, ArrayList nums)
    {
        if (sections == null || nums == null) return;
        var wanted = new HashSet<int>(nums.Cast<object>().Select(Convert.ToInt32));
        var list = sections.Cast<Dictionary<string, object>>().Where(s => wanted.Contains(Int(s, "num"))).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            double at = (i + 0.5) / list.Count;
            await SyncSectionAsync(course, list[i], (k, item) => Progress(course, "מוריד את החומר החדש: " + Trunc(item, 70), at));
        }
        Send(new { type = "courseStatus", course = course, busy = false, text = "החומר החדש ירד ✓", error = false });
        foreach (var s in list.Where(s => Int(s, "num") > 0 && CountFiles(LectureDir(course, Int(s, "num"))) > 0))
        {
            var r = await LecturePdfAsync(course, Int(s, "num"), Str(s, "title"));
            if (r != null && r.Limit) break;
        }
    }

    // Every few hours, look at all the courses that are set up, even when none of them is open.
    async Task BackgroundNewMaterialCheck()
    {
        if (lastCourses == null || DateTime.Now - lastNewCheck < TimeSpan.FromHours(3)) return;
        lastNewCheck = DateTime.Now;
        foreach (var c in lastCourses.ToList())
        {
            string name = Str(c, "name");
            int id = Int(c, "id");
            if (!Bool(LoadMeta(name), "setupDone")) continue;
            try
            {
                string v = await WithMoodle(() => EvalAsync(moodle, SectionsJs.Replace("COURSE_ID", id.ToString()), 120000));
                var meta = LoadMeta(name);
                meta["sectionsCache"] = json.DeserializeObject(v);
                SaveMeta(name, meta);
                CheckNewMaterial(name, json.Deserialize<ArrayList>(v), true);
            }
            catch { }
            await Task.Delay(2000);
        }
    }
}
