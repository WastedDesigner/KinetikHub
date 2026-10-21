// Kinetik Hub: one tray app that looks after the other Kinetik apps. It starts them at login, deploys new builds and
// GitHub releases (keeping the old exe), backs up all their settings into one file, and sets one theme across all of them.
// Copyright © 2026 Akila Sella Hennedige. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Kinetik Hub")]
[assembly: AssemblyProduct("Kinetik Hub")]
[assembly: AssemblyCopyright("Copyright © 2026 Akila Sella Hennedige")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

static class Program
{
    public const string Version = "2.0.0";
    public static bool Demo, AtStartup, OpenUpdates;
    public static string ShotPath; // --shot file.png: draws the window into a PNG and quits (for the README screenshot)
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    static void Report(string file)
    {
        Places.Cfg = Settings.Load();
        var sb = new StringBuilder();
        foreach (var a in AppDef.All)
        {
            var exe = Places.Installed(a); var build = Places.Build(a);
            sb.AppendLine(a.Title + ": installed=" + (exe ?? "-") + " v" + (exe != null ? Places.Short(Places.VersionOf(exe)) : "-") + " | build=" + (build ?? "-") +
                " | newer build=" + Places.BuildIsNewer(a, exe, build) + " | running=" + Running.Is(a) + " | own startup=" + OwnStartup.Has(a) + " -> " + (OwnStartup.TargetOf(a) ?? "-"));
        }
        File.WriteAllText(file, sb.ToString());
    }

    static void UpdatesReport(string file)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        Places.Cfg = Settings.Load();
        var sb = new StringBuilder();
        foreach (var a in AppDef.WithSelf)
        {
            Release latest = null; string err = null;
            if (a.Repo != null) try { latest = Updates.Latest(a); } catch (Exception ex) { err = ex.Message; }
            var exe = a == AppDef.Self ? Application.ExecutablePath : Places.Installed(a);
            sb.AppendLine(a.Title + ": installed v" + (exe != null ? Places.Short(Places.VersionOf(exe)) : "-") + " | latest " + (latest != null ? latest.Tag + (latest.ZipSha != null ? " (verified zip)" : "") : err ?? "none") +
                " | " + UpdateStatus.Of(a, exe, latest, a.Repo != null, "").Text);
            if (a.Repo != null && err == null)
                try { foreach (var r in Updates.List(a, 5)) sb.AppendLine("    " + r.Tag + "  " + r.Published.ToString("yyyy-MM-dd") + "  " + (r.ZipSha != null ? "sha256 ok" : "no digest") + "  notes: " + r.Notes.Length + " chars"); }
                catch (Exception ex) { sb.AppendLine("    list failed: " + ex.Message); }
        }
        File.WriteAllText(file, sb.ToString());
    }

    static int TestInstall(string id, string tag, string folder)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var a = id == AppDef.Self.Id ? AppDef.Self : AppDef.Get(id);
        if (a == null || a.Repo == null) { Console.Error.WriteLine("Unknown app"); return 2; }
        var r = Updates.List(a, 30).FirstOrDefault(x => x.Tag == tag);
        if (r == null) { Console.Error.WriteLine("No such release"); return 3; }
        string temp = null;
        try
        {
            var exe = Updates.Download(a, r, out temp);
            Deploy.Fresh(a, exe, Path.Combine(Path.GetFullPath(folder), a.Exe));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Path.GetFullPath(folder), "test-install-error.txt"), ex.ToString()); return 1; }
        finally { Updates.CleanUp(temp); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        // Run as administrator by Hub itself, for the one app that runs elevated (Kinetik's taskbar bar). Does its job and quits.
        int bi = Array.IndexOf(args, "--bar");
        if (bi >= 0) return Elevated.Run(args.Skip(bi + 1).ToArray());

        // --report file.txt: writes what Hub finds for each app (where it is, its build, how it starts) and quits
        int ri = Array.IndexOf(args, "--report");
        if (ri >= 0 && ri + 1 < args.Length) { Report(args[ri + 1]); return 0; }

        // --updates-report file.txt: checks GitHub now and writes each app's update status (and its releases) and quits
        int ui = Array.IndexOf(args, "--updates-report");
        if (ui >= 0 && ui + 1 < args.Length) { UpdatesReport(args[ui + 1]); return 0; }

        // --test-install <AppId> <tag> <folder>: downloads that release, verifies it and installs it into folder, for testing
        int ti = Array.IndexOf(args, "--test-install");
        if (ti >= 0 && ti + 3 < args.Length) return TestInstall(args[ti + 1], args[ti + 2], args[ti + 3]);

        // Started by a Hub that has just replaced its own exe: wait for that one to close before taking over.
        int wi = Array.IndexOf(args, "--wait-pid"); int waitPid;
        if (wi >= 0 && wi + 1 < args.Length && int.TryParse(args[wi + 1], out waitPid))
            try { using (var old = Process.GetProcessById(waitPid)) old.WaitForExit(20000); } catch { }

        Demo = args.Contains("--demo");
        AtStartup = args.Contains("--startup");
        OpenUpdates = args.Contains("--updates");
        int si = Array.IndexOf(args, "--shot");
        if (si >= 0 && si + 1 < args.Length) { ShotPath = Path.GetFullPath(args[si + 1]); Demo = true; }
        bool created;
        using (new Mutex(true, Demo ? @"Local\KinetikHubDemo" : @"Local\KinetikHub", out created))
        {
            if (!created) { ShowSignal.Signal(); return 0; } // already running: bring its window up
            SetProcessDPIAware();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ToolStripManager.Renderer = new DarkMenu();
            Backup.KeyName = "KinetikHub"; Backup.AppTitle = "Kinetik Hub";
            Startup.Args = " --startup";
            if (!Demo) Backup.OfferRestore();
            var app = new HubContext();
            using (new KinetikSuite.Shutdown("KinetikHub" + (Demo ? "Demo" : ""), app.ExitHub))
                Application.Run(app);
        }
        if (!Demo) Backup.AfterExit();
        return 0;
    }
}

// A second launch (e.g. double-clicking the exe while Hub sits in the tray) asks the running one to show its window.
static class ShowSignal
{
    public const string EventName = @"Local\KinetikHubShow";
    public static void Signal() { try { using (var e = EventWaitHandle.OpenExisting(EventName)) e.Set(); } catch { } }
}

// ======================================================================= Settings
class Settings
{
    const string KeyPath = @"Software\KinetikHub";
    public string StartWith = "";   // app ids Hub starts at login, joined by '|'
    public string Paths = "";       // "id=path" lines, for apps found by hand with Locate…
    public string Moved = "";       // "id=what" lines: the apps' own startup entries Hub took over, so they can be put back
    public string Latest = "";      // "id=tag|url|asset" lines from the last update check
    public string LastCheck = "";   // when that was, as a round-trip date
    public string ThemeName = "Midnight";
    public string Skipped = "";     // "id=tag" lines: releases the user chose to skip
    public bool StayInTray = true, CheckUpdates = true, AutoInstall;

    static IEnumerable<FieldInfo> Fields
    {
        get
        {
            return typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int) || f.FieldType == typeof(bool) || f.FieldType == typeof(string));
        }
    }

    public static bool Unreadable;

    // Kinetik 3.2 and earlier were the app id "Kinetik"; since 4.0 it's Kinetik Stats, id "KinetikStats". Choices saved
    // under the old id (start at login, a located copy, moved startup entries, update notes) follow it.
    void RenameKinetik()
    {
        var old = new System.Text.RegularExpressions.Regex(@"(?m)(^|\|)Kinetik(?=[=|\r]|$)");
        StartWith = old.Replace(StartWith ?? "", "$1KinetikStats");
        Paths = old.Replace(Paths ?? "", "$1KinetikStats");
        Moved = old.Replace(Moved ?? "", "$1KinetikStats");
        Latest = old.Replace(Latest ?? "", "$1KinetikStats");
        Skipped = old.Replace(Skipped ?? "", "$1KinetikStats");
    }

    // Made-up GitHub results for --demo: one release ahead of each installed app, so the Updates window has something to show.
    static string DemoReleases()
    {
        var lines = new List<string>();
        if (Places.Cfg == null) Places.Cfg = new Settings(); // Places reads it to find the apps
        foreach (var a in AppDef.WithSelf.Where(x => x.Repo != null))
        {
            var exe = a == AppDef.Self ? Application.ExecutablePath : Places.Installed(a);
            var v = exe != null ? Places.VersionOf(exe) : new Version(1, 0, 0);
            var tag = a.Id == "KinetikFocus" ? "" : "v" + v.Major + "." + v.Minor + "." + (Math.Max(0, v.Build) + (a.Id == "KinetikClock" ? 0 : 1));
            lines.Add(a.Id + "=" + (tag == "" ? "" : tag + "|https://github.com/" + a.Repo + "/releases/tag/" + tag + "|https://github.com/" + a.Repo + "/releases/download/" + tag + "/" + a.Id + ".zip|" + new string('0', 64) + "|"));
        }
        return string.Join("\n", lines);
    }

    public static Settings Load()
    {
        if (Program.Demo) return new Settings { StartWith = "KinetikCalendar|KinetikClock|KinetikNotes", Latest = DemoReleases(), LastCheck = DateTime.UtcNow.AddMinutes(-3).ToString("o") };
        Settings s = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0) Thread.Sleep(500);
            bool ok;
            s = TryLoad(out ok);
            if (ok) { Unreadable = false; s.RenameKinetik(); return s; }
        }
        Unreadable = true;
        MessageBox.Show("Kinetik Hub couldn't read its saved settings, so it's running on the defaults for now.\n\n" +
            "Your settings haven't been changed, and nothing will be saved over them while this is showing. Restart Kinetik Hub to try again.",
            "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return s;
    }

    static Settings TryLoad(out bool ok)
    {
        ok = true;
        var s = new Settings();
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                if (k != null)
                    foreach (var f in Fields)
                    {
                        var v = k.GetValue(f.Name);
                        if (v == null) continue;
                        try
                        {
                            if (f.FieldType == typeof(string)) f.SetValue(s, v.ToString());
                            else if (f.FieldType == typeof(int)) f.SetValue(s, Convert.ToInt32(v));
                            else f.SetValue(s, Convert.ToInt32(v) != 0);
                        }
                        catch { ok = false; }
                    }
        }
        catch { ok = false; }
        KinetikSuite.SettingsValidation.Sanitize(s); return s;
    }

    public void Save()
    {
        if (Program.Demo || Unreadable) return;
        KinetikSuite.SettingsValidation.Sanitize(this);
        if (KinetikSuite.RegistryFiles.CannotSave) return;
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                foreach (var f in Fields)
                {
                    var v = f.GetValue(this);
                    if (f.FieldType == typeof(bool)) k.SetValue(f.Name, (bool)v ? 1 : 0);
                    else if (f.FieldType == typeof(int)) k.SetValue(f.Name, (int)v);
                    else k.SetValue(f.Name, (string)v ?? "");
                }
        }
        catch { }
    }

    // "id=value" lines as a dictionary, and back
    public static Dictionary<string, string> Map(string lines)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in (lines ?? "").Split('\n'))
        {
            int i = l.IndexOf('=');
            if (i > 0) d[l.Substring(0, i)] = l.Substring(i + 1);
        }
        return d;
    }
    public static string Unmap(Dictionary<string, string> d) { return string.Join("\n", d.Select(p => p.Key + "=" + p.Value.Replace("\n", " "))); }

    public bool Starts(string id) { return StartWith.Split('|').Contains(id); }
    public void SetStarts(string id, bool on)
    {
        var l = StartWith.Split('|').Where(x => x != "" && x != id).ToList();
        if (on) l.Add(id);
        StartWith = string.Join("|", l);
    }
}

// ======================================================================= The apps Hub knows about
class AppDef
{
    public string Id, Title, Folder, Source, Repo, ThemeField, Blurb;
    public bool Admin; // runs as administrator (Kinetik's bar): started, stopped and deployed through an elevated copy of Hub
    public bool NoClickThrough; // has no "Click-through when locked"
    public string Exe { get { return Id + ".exe"; } }
    public bool HasLook { get { return ThemeField != null; } }

    // The registry name of one of the shared look settings (ThemeName, Opacity, Scale, FontName, Layer, Locked, Snap,
    // ClickThrough) in this app, or null if it doesn't have it. Kinetik's bar keeps its widget's under "Widget…".
    public string Field(string common)
    {
        if (!HasLook || (common == "ClickThrough" && NoClickThrough)) return null;
        if (!Admin) return common;
        return common == "FontName" ? "WidgetFont" : "Widget" + common;
    }
    public string Key { get { return @"Software\" + Id; } }

    public static readonly AppDef[] All =
    {
        new AppDef { Id = "KinetikStats", Title = "Kinetik Stats", Folder = "Kinetik Stats", Source = "KinetikStats", Repo = "WastedDesigner/Kinetik", ThemeField = "WidgetThemeName", Admin = true, Blurb = "Taskbar system monitor" },
        new AppDef { Id = "KinetikScreen", Title = "Kinetik Screen", Folder = "Kinetik Screen", Source = "KinetikScreen", Blurb = "Sensor panel companion" },
        new AppDef { Id = "KinetikCalendar", Title = "Kinetik Calendar", Folder = "Kinetik Calendar", Source = "KinetikCalendar", Repo = "WastedDesigner/KinetikCalendar", ThemeField = "ThemeName", Blurb = "Calendar widget" },
        new AppDef { Id = "KinetikClock", Title = "Kinetik Clock", Folder = "Kinetik Clock", Source = "KinetikClock", Repo = "WastedDesigner/KinetikClock", ThemeField = "ThemeName", Blurb = "World clock widget" },
        new AppDef { Id = "KinetikFeeds", Title = "Kinetik Feeds", Folder = "Kinetik Feeds", Source = "KinetikFeeds", Repo = "WastedDesigner/KinetikFeeds", ThemeField = "ThemeName", Blurb = "News headlines widget" },
        new AppDef { Id = "KinetikNotes", Title = "Kinetik Notes", Folder = "Kinetik Notes", Source = "KinetikNotes", Repo = "WastedDesigner/KinetikNotes", ThemeField = "ThemeName", NoClickThrough = true, Blurb = "Markdown sticky notes" },
        new AppDef { Id = "KinetikWeather", Title = "Kinetik Weather", Folder = "Kinetik Weather", Source = "KinetikWeather", Repo = "WastedDesigner/KinetikWeather", ThemeField = "ThemeName", Blurb = "Weather widget" },
        new AppDef { Id = "KinetikFocus", Title = "Kinetik Focus", Folder = "Kinetik Focus", Source = "KinetikFocus", Repo = "WastedDesigner/KinetikFocus", ThemeField = "ThemeName", Blurb = "Focus timer" },
        new AppDef { Id = "KinetikDock", Title = "Kinetik Dock", Folder = "Kinetik Dock", Source = "KinetikDock", Repo = "WastedDesigner/KinetikDock", ThemeField = "ThemeName", NoClickThrough = true, Blurb = "Shortcut groups" },
        // A window app, not a widget, so no shared look settings. It builds into <Apps>\Kinetik Files\dist\, and its
        // GitHub repo is private with no releases, so Hub doesn't look for updates.
        new AppDef { Id = "KinetikFiles", Title = "Kinetik Files", Folder = "Kinetik Files", Source = "dist", Blurb = "File manager" },
    };

    public static AppDef Get(string id) { return All.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); }

    // Hub itself: updated from its own GitHub releases, but not started, stopped or themed like the others.
    public static readonly AppDef Self = new AppDef { Id = "KinetikHub", Title = "Kinetik Hub", Folder = "Kinetik Hub", Source = "KinetikHub", Repo = "WastedDesigner/KinetikHub", Blurb = "This app" };
    public static IEnumerable<AppDef> WithSelf { get { return new[] { Self }.Concat(All); } }
}

// Where things are on disk, worked out from Hub's own place. Each app has one folder, <Apps>\<App folder>\<App>\, holding
// its source, its build (the exe that runs) and its settings backup, with earlier exes in <Apps>\<App folder>\Old Versions.
// Hub sits in the same layout: <Apps>\Kinetik Hub\KinetikHub\KinetikHub.exe. The older layout, with the copy in use in a
// separate <App>_Personal folder, is still understood.
static class Places
{
    public static Settings Cfg;

    public static string AppsRoot
    {
        get
        {
            try { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "..")); }
            catch { return null; }
        }
    }

    // The copy in use: one picked by hand, else the build in <Folder>\<Source>\, else <Folder>\<Id>_Personal\<Id>.exe,
    // else wherever it's running from or starts from, else <Folder>\<Id>.exe.
    public static string Installed(AppDef a)
    {
        string p;
        if (Settings.Map(Cfg.Paths).TryGetValue(a.Id, out p) && File.Exists(p)) return p;
        var root = AppsRoot;
        if (root != null)
        {
            if (a.Source != null) { p = Path.Combine(root, a.Folder, a.Source, a.Exe); if (File.Exists(p)) return p; }
            p = Path.Combine(root, a.Folder, a.Id + "_Personal", a.Exe);
            if (File.Exists(p)) return p;
        }
        p = Running.PathOf(a);
        if (p != null && File.Exists(p)) return p;
        p = OwnStartup.TargetOf(a);
        if (p != null && File.Exists(p)) return p;
        if (root != null)
        {
            p = Path.Combine(root, a.Folder, a.Exe);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // Kinetik's bar runs from this copy (its logon task starts it as administrator); its build puts itself here.
    public static string BarRunning { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KinetikStats", "KinetikStats.exe"); } }

    // The app runs from its build, but that was rebuilt after it started: a restart picks up the new one.
    public static bool RestartPending(AppDef a, string exe)
    {
        if (a.Admin || exe == null) return false;
        try
        {
            var built = File.GetLastWriteTime(exe);
            foreach (var pr in Running.Of(a))
                using (pr)
                    try { if (pr.MainModule.FileName.Equals(exe, StringComparison.OrdinalIgnoreCase) && pr.StartTime < built.AddSeconds(-2)) return true; } catch { }
        }
        catch { }
        return false;
    }

    // Kinetik's bar: its build is newer than the copy in Program Files that the logon task runs.
    public static bool BarPending(AppDef a, string exe)
    {
        if (!a.Admin || exe == null) return false;
        try
        {
            var pf = BarRunning;
            if (!File.Exists(pf) || Same(exe, pf)) return false;
            return File.GetLastWriteTimeUtc(exe) > File.GetLastWriteTimeUtc(pf).AddSeconds(2);
        }
        catch { return false; }
    }

    // Where an app that isn't on this PC is installed from GitHub: its own folder in the same layout, <Apps>\<Folder>\<Source>\.
    public static string InstallTarget(AppDef a)
    {
        var root = AppsRoot;
        return root == null ? null : Path.Combine(root, a.Folder, a.Source ?? a.Id, a.Exe);
    }

    // The working build next to the source, e.g. <Apps>\Kinetik Clock\KinetikClock\KinetikClock.exe
    public static string Build(AppDef a)
    {
        var root = AppsRoot;
        if (root == null || a.Source == null) return null;
        var p = Path.Combine(root, a.Folder, a.Source, a.Exe);
        return File.Exists(p) ? p : null;
    }

    // A newer build than the installed copy: written later and not byte-for-byte the same.
    public static bool BuildIsNewer(AppDef a, string installed, string build)
    {
        try
        {
            if (installed == null || build == null || Same(installed, build)) return false;
            var bi = new FileInfo(build); var ii = new FileInfo(installed);
            return bi.LastWriteTimeUtc > ii.LastWriteTimeUtc.AddSeconds(2);
        }
        catch { return false; }
    }

    sealed class CachedFile { public long Length, Stamp; public DateTime At; public string Hash; public Version Version; }
    static readonly object fileGate = new object();
    static readonly Dictionary<string, CachedFile> fileCache = new Dictionary<string, CachedFile>(StringComparer.OrdinalIgnoreCase);
    public static void InvalidateFiles() { lock (fileGate) fileCache.Clear(); }
    static CachedFile Inspect(string path, bool needHash)
    {
        var info = new FileInfo(path);
        lock (fileGate)
        {
            CachedFile entry;
            if (fileCache.TryGetValue(path, out entry) && entry.Length == info.Length && entry.Stamp == info.LastWriteTimeUtc.Ticks &&
                (DateTime.UtcNow - entry.At).TotalSeconds < 30 && (!needHash || entry.Hash != null)) return entry;
            var version = FileVersionInfo.GetVersionInfo(path);
            entry = new CachedFile { Length = info.Length, Stamp = info.LastWriteTimeUtc.Ticks, At = DateTime.UtcNow,
                Version = new Version(version.FileMajorPart, version.FileMinorPart, Math.Max(0, version.FileBuildPart)),
                Hash = needHash ? KinetikSuite.Hashes.FileHash(path) : null };
            fileCache[path] = entry; return entry;
        }
    }
    public static bool Same(string a, string b) { return Inspect(a, true).Hash == Inspect(b, true).Hash; }
    public static Version VersionOf(string exe) { try { return Inspect(exe, false).Version; } catch { return null; } }

    public static string Short(Version v) { return v == null ? "?" : v.Major + "." + v.Minor + "." + Math.Max(0, v.Build); }

    // <Folder>\Old Versions (beside the app's own folder), else an Old Versions folder next to the exe
    public static string OldVersionsDir(string installed)
    {
        var dir = Path.GetDirectoryName(installed);
        var up = Path.Combine(Path.GetDirectoryName(dir), "Old Versions");
        if (Directory.Exists(up) || Path.GetFileName(dir).EndsWith("_Personal", StringComparison.OrdinalIgnoreCase)) return up;
        return Path.Combine(dir, "Old Versions");
    }

    public static string Friendly(string path)
    {
        if (path == null) return "not found";
        var root = AppsRoot;
        if (root != null && path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return "…\\" + path.Substring(root.Length + 1);
        return path;
    }
}

// ======================================================================= Running, starting and stopping the apps
static class Running
{
    static readonly object processGate = new object();
    static readonly Dictionary<string, Tuple<DateTime, int[]>> processes = new Dictionary<string, Tuple<DateTime, int[]>>();
    public static void Invalidate() { lock (processGate) processes.Clear(); }
    public static Process[] Of(AppDef a)
    {
        lock (processGate)
        {
            Tuple<DateTime, int[]> cached;
            if (!processes.TryGetValue(a.Id, out cached) || (DateTime.UtcNow - cached.Item1).TotalSeconds >= 1)
            {
                var current = Process.GetProcessesByName(a.Id);
                try { cached = Tuple.Create(DateTime.UtcNow, current.Select(p => p.Id).ToArray()); processes[a.Id] = cached; }
                finally { foreach (var p in current) p.Dispose(); }
            }
            var live = new List<Process>();
            foreach (var id in cached.Item2) try { var p = Process.GetProcessById(id); if (p.ProcessName == a.Id) live.Add(p); else p.Dispose(); } catch { }
            return live.ToArray();
        }
    }
    public static bool Is(AppDef a) { var ps = Of(a); foreach (var p in ps) p.Dispose(); return ps.Length > 0; }

    // null when it isn't running or (running as administrator) its path can't be read
    public static string PathOf(AppDef a)
    {
        foreach (var p in Of(a))
            using (p)
                try { return p.MainModule.FileName; } catch { }
        return null;
    }

    public static bool Elevated(AppDef a)
    {
        var ps = Of(a);
        bool any = ps.Length > 0, readable = false;
        foreach (var p in ps) using (p) try { var f = p.MainModule.FileName; readable = true; } catch { }
        return any && !readable;
    }

    public static bool Start(AppDef a, string exe, bool asAdmin)
    {
        if (exe == null || !File.Exists(exe)) return false;
        try
        {
            var psi = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true };
            if (asAdmin) psi.Verb = "runas";
            // Kinetik's bar quits at once if its single-instance lock is still held (e.g. by a companion just after the
            // old copy was stopped); --restart makes it wait for the lock instead.
            if (a.Admin) psi.Arguments = "--restart";
            Process.Start(psi); Invalidate();
            return true;
        }
        catch { return false; } // e.g. the admin prompt was turned down
    }

    // Ask the UI thread to flush pending work and close. An unresponsive or older
    // companion is left running, so deployment and settings writes can safely stop.
    public static bool Stop(AppDef a)
    {
        bool ok = true;
        var processes = Of(a);
        try
        {
            if (processes.Length == 0) return true;
            bool signalled = KinetikSuite.Shutdown.Request(a.Id);
            foreach (var p in processes)
            {
                try
                {
                    if (!signalled && !p.CloseMainWindow()) { ok = false; continue; }
                    if (!p.WaitForExit(15000)) ok = false;
                }
                catch { ok = false; }
            }
            Invalidate(); return ok && !Is(a);
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    public static string WithStopped(IEnumerable<AppDef> targets, Action apply)
    {
        var stopped = new List<AppDef>(); var errors = new List<string>();
        try
        {
            foreach (var app in targets.Where(Is))
            {
                if (!Stop(app)) { errors.Add("Could not close " + app.Title + " safely. No requested settings were changed."); break; }
                stopped.Add(app);
            }
            if (errors.Count == 0) apply();
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        finally
        {
            foreach (var app in stopped)
                try { if (!StartVerified(app, Places.Installed(app), false)) errors.Add(app.Title + " did not restart."); }
                catch (Exception ex) { errors.Add(app.Title + ": " + ex.Message); }
        }
        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    public static bool StartVerified(AppDef a, string exe, bool admin)
    {
        if (!Start(a, exe, admin)) return false;
        var until = DateTime.UtcNow.AddSeconds(15); DateTime seen = DateTime.MinValue; int seenPid = -1;
        do
        {
            foreach (var p in Of(a)) using (p)
                try
                {
                    if (string.Equals(Path.GetFullPath(p.MainModule.FileName), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
                    {
                        if (p.Id != seenPid) { seenPid = p.Id; seen = DateTime.UtcNow; }
                        if ((DateTime.UtcNow - seen).TotalMilliseconds >= 1500 && !p.HasExited) return true;
                    }
                } catch { }
            Thread.Sleep(200);
        } while (DateTime.UtcNow < until);
        return false;
    }

}

// The apps' own ways of starting at login: a shortcut in the Startup folder or a value under HKCU\...\Run.
// When Hub takes over an app's start, it moves the shortcut into its own folder (or notes the Run value) so it can
// put it back exactly if the app is handed back.
static class OwnStartup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string StartupDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Startup); } }
    static string KeptDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KinetikHub", "Startup shortcuts"); } }

    static IEnumerable<string> Shortcuts(AppDef a)
    {
        if (!Directory.Exists(StartupDir)) yield break;
        foreach (var f in Directory.GetFiles(StartupDir, "*.lnk"))
        {
            var t = Lnk.Target(f);
            if (t != null && Path.GetFileName(t).Equals(a.Exe, StringComparison.OrdinalIgnoreCase)) yield return f;
        }
    }

    static IEnumerable<KeyValuePair<string, string>> RunValues(AppDef a)
    {
        var list = new List<KeyValuePair<string, string>>();
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                if (k != null)
                    foreach (var n in k.GetValueNames())
                    {
                        var v = k.GetValue(n) as string ?? "";
                        if (v.IndexOf("\\" + a.Exe, StringComparison.OrdinalIgnoreCase) >= 0 || v.Trim('"').Equals(a.Exe, StringComparison.OrdinalIgnoreCase)) list.Add(new KeyValuePair<string, string>(n, v));
                    }
        }
        catch { }
        return list;
    }

    public static int BarState(out string detail)
    {
        detail = "Kinetik Stats manages its administrator startup task.";
        object service = null, folder = null, task = null, definition = null, actions = null, action = null;
        try
        {
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var value = run == null ? null : run.GetValue("KinetikStats") as string;
                if (!string.IsNullOrEmpty(value))
                {
                    var path = value.Trim().Trim('"');
                    if (File.Exists(path)) { detail = "Starts from the Run key: " + path; return 1; }
                    detail = "The Run key target is missing.";
                }
            }
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            service.GetType().InvokeMember("Connect", BindingFlags.InvokeMethod, null, service, new object[0]);
            folder = service.GetType().InvokeMember("GetFolder", BindingFlags.InvokeMethod, null, service, new object[] { "\\" });
            task = folder.GetType().InvokeMember("GetTask", BindingFlags.InvokeMethod, null, folder, new object[] { "KinetikStats" });
            var enabled = (bool)task.GetType().InvokeMember("Enabled", BindingFlags.GetProperty, null, task, null);
            definition = task.GetType().InvokeMember("Definition", BindingFlags.GetProperty, null, task, null);
            actions = definition.GetType().InvokeMember("Actions", BindingFlags.GetProperty, null, definition, null);
            action = actions.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, actions, new object[] { 1 });
            var target = action.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, action, null) as string;
            if (!enabled) { detail = "Kinetik Stats' startup task is disabled."; return 0; }
            if (target == null || !File.Exists(target)) { detail = "The startup task target is missing."; return 0; }
            if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(Places.BarRunning), StringComparison.OrdinalIgnoreCase)) { detail = "The startup task points to an unexpected location: " + target; return 0; }
            detail = "Starts using the enabled administrator logon task."; return 1;
        }
        catch (Exception ex)
        {
            var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            var com = cause as COMException;
            if (com != null && (com.ErrorCode == unchecked((int)0x80070002) || com.ErrorCode == unchecked((int)0x8004130F))) { detail = "Kinetik Stats has no startup task."; return 0; }
            detail = "Startup state could not be read: " + cause.Message; return -1;
        }
        finally { foreach (var item in new[] { action, actions, definition, task, folder, service }) if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); }
    }

    public static bool Has(AppDef a) { return Shortcuts(a).Any() || RunValues(a).Any(); }

    public static string TargetOf(AppDef a)
    {
        foreach (var f in Shortcuts(a)) return Lnk.Target(f);
        foreach (var rv in RunValues(a))
        {
            var v = rv.Value.Trim();
            if (v.StartsWith("\"")) { int e = v.IndexOf('"', 1); if (e > 0) return v.Substring(1, e - 1); }
            int x = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (x > 0) return v.Substring(0, x + 4);
        }
        return null;
    }

    // Moves the app's own entries aside; returns what was moved, to be stored for HandBack.
    public static string TakeOver(AppDef a)
    {
        var moved = new List<string>();
        foreach (var f in Shortcuts(a).ToList())
            try
            {
                Directory.CreateDirectory(KeptDir);
                var dest = Path.Combine(KeptDir, Path.GetFileName(f));
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(f, dest);
                moved.Add("lnk:" + Path.GetFileName(f));
            }
            catch { }
        foreach (var rv in RunValues(a).ToList())
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k.DeleteValue(rv.Key, false);
                moved.Add("run:" + rv.Key + "\t" + rv.Value);
            }
            catch { }
        return string.Join("\u001f", moved);
    }

    public static void HandBack(string moved)
    {
        foreach (var m in (moved ?? "").Split('\u001f'))
            try
            {
                if (m.StartsWith("lnk:"))
                {
                    var src = Path.Combine(KeptDir, m.Substring(4));
                    if (File.Exists(src)) File.Move(src, Path.Combine(StartupDir, m.Substring(4)));
                }
                else if (m.StartsWith("run:"))
                {
                    var kv = m.Substring(4).Split(new[] { '\t' }, 2);
                    if (kv.Length == 2) using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(kv[0], kv[1]);
                }
            }
            catch { }
    }
}

// Reads a .lnk's target with the Windows shell's own ShellLink object (no extra libraries).
static class Lnk
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr fd, int flags);
        void GetIDList(out IntPtr ppidl); void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short key); void SetHotkey(short key);
        void GetShowCmd(out int cmd); void SetShowCmd(int cmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    public static string Target(string lnk)
    {
        object o = null;
        try
        {
            o = new ShellLink();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)o).Load(lnk, 0);
            var sb = new StringBuilder(1024);
            ((IShellLinkW)o).GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4); // SLGP_RAWPATH
            var s = Environment.ExpandEnvironmentVariables(sb.ToString());
            return s == "" ? null : s;
        }
        catch { return null; }
        finally { if (o != null) Marshal.ReleaseComObject(o); }
    }
}

static class Jobs
{
    static bool busy;
    public static volatile string Progress = "";
    public static bool Busy { get { return busy; } }
    public static void Run(Func<string> work, Action<string> done)
    {
        if (busy) { done("Another suite operation is still running. Wait for it to finish."); return; }
        busy = true; Progress = "Applying app changes…";
        var ui = new Control(); var handle = ui.Handle;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string error = null;
            try { error = work(); } catch (Exception ex) { error = ex.Message; }
            try { ui.BeginInvoke((Action)(() => { busy = false; Progress = ""; try { done(error); } finally { ui.Dispose(); } })); }
            catch { busy = false; ui.Dispose(); }
        });
    }
    public static void ShowError(string error) { if (error != null) MessageBox.Show(error, "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
}

// ======================================================================= Deploying: a new build or a downloaded release
static class Deploy
{
    // Backs up the installed exe to Old Versions, copies the new one in, refreshes the app's settings backup next to it,
    // and starts it again if it was running. Returns null when it worked, else what went wrong.
    public static string Install(AppDef a, string installed, string newExe, string label)
    {
        if (a.Admin) return Elevated.Launch("install", installed, newExe, label);
        bool wasRunning = Running.Is(a);
        if (wasRunning && !Running.Stop(a)) return a.Title + " could not close safely. Exit it from its tray menu, then try again.";
        try { Copy(installed, newExe, label); }
        catch (Exception ex)
        {
            if (wasRunning) Running.Start(a, installed, false);
            return ex.Message;
        }
        ExportSettings(a, installed);
        if (wasRunning && !Running.StartVerified(a, installed, false)) return "Installed, but " + a.Title + " did not restart successfully.";
        return null;
    }

    public static void Copy(string installed, string newExe, string label)
    {
        if (string.Equals(Path.GetFullPath(installed), Path.GetFullPath(newExe), StringComparison.OrdinalIgnoreCase)) throw new IOException("The selected build is already the installed file.");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { { Path.GetFileName(installed), File.ReadAllBytes(newExe) } };
        var lib = Path.Combine(Path.GetDirectoryName(newExe), "lib");
        if (Directory.Exists(lib))
            foreach (var f in Directory.GetFiles(lib, "*.dll", SearchOption.AllDirectories)) files["lib/" + f.Substring(lib.Length + 1).Replace('\\', '/')] = File.ReadAllBytes(f);
        var backup = Path.Combine(Places.OldVersionsDir(installed), "deploy-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        KinetikSuite.FileTransaction.Install(Path.GetDirectoryName(installed), files, backup);
        Places.InvalidateFiles();
    }

    // An app that isn't installed yet: its exe (and lib\, if the release has one) into target's folder.
    public static void Fresh(AppDef a, string newExe, string target)
    {
        var dir = Path.GetDirectoryName(target);
        Directory.CreateDirectory(dir);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { { a.Exe, File.ReadAllBytes(newExe) } };
        var lib = Path.Combine(Path.GetDirectoryName(newExe), "lib");
        if (Directory.Exists(lib))
            foreach (var f in Directory.GetFiles(lib, "*.dll", SearchOption.AllDirectories)) files["lib/" + f.Substring(lib.Length + 1).Replace('\\', '/')] = File.ReadAllBytes(f);
        foreach (var name in new[] { "README.md", "LICENSE" })
        {
            var extra = Path.Combine(Path.GetDirectoryName(newExe), name);
            if (File.Exists(extra) && !File.Exists(Path.Combine(dir, name))) files[name] = File.ReadAllBytes(extra);
        }
        var backup = Path.Combine(Places.OldVersionsDir(target), "install-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        KinetikSuite.FileTransaction.Install(dir, files, backup);
        // nothing was replaced, so the recovery folder holds only its status note
        try { if (Directory.GetFiles(backup, "*", SearchOption.AllDirectories).All(f => Path.GetFileName(f) == "transaction-status.txt")) Directory.Delete(backup, true); } catch { }
        Places.InvalidateFiles();
    }

    // Downloads a release, checks it, and installs it: over the installed copy (kept in Old Versions, the app restarted if
    // it was running) or, for an app that isn't on this PC, into its own folder. Runs off the UI thread. Null when it worked.
    public static string FromRelease(AppDef a, Release r)
    {
        string temp = null, exe;
        try { exe = Updates.Download(a, r, out temp); }
        catch (Exception ex) { Updates.CleanUp(temp); return "Couldn't download " + a.Title + " " + r.Tag + ": " + ex.Message; }
        try
        {
            var installed = Places.Installed(a);
            if (installed != null) return Install(a, installed, exe, r.Tag);
            var target = Places.InstallTarget(a);
            if (target == null) return "Couldn't work out where to install " + a.Title + ".";
            Fresh(a, exe, target);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
        finally { Updates.CleanUp(temp); }
    }

    public static string Bar(string build, string label)
    {
        return Elevated.Launch("deploy", build, Places.OldVersionsDir(build), label);
    }

    // reg export HKCU\Software\<Id> to <Id>-settings.reg beside the exe, as each app's own Backup does
    public static void ExportSettings(AppDef a, string installed)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(a.Key)) if (k == null) return;
            Reg.Run("export \"HKCU\\" + a.Key + "\" \"" + Path.Combine(Path.GetDirectoryName(installed), a.Id + "-settings.reg") + "\" /y");
        }
        catch { }
    }
}

static class Reg
{
    public static bool Run(string args) { return KinetikSuite.RegistryFiles.Run(args); }
}

// Kinetik's bar runs as administrator, so stopping, restarting or replacing it takes an elevated helper: Hub runs a
// copy of itself as administrator with "--bar <job> …", which does just that job and quits.
static class Elevated
{
    static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "") + "\""; }

    public static string Launch(params string[] job)
    {
        try
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath, "--bar " + string.Join(" ", job.Select(Q))) { UseShellExecute = true, Verb = "runas" };
            using (var p = Process.Start(psi))
            {
                // A timed-out helper may still mutate files; do not abandon it and allow
                // another operation to race it. Wait off the UI thread until it exits.
                bool exceededDeadline = !p.WaitForExit(60000);
                if (exceededDeadline) { Jobs.Progress = "Administrator operation exceeded 60 seconds; waiting for it to finish safely."; p.WaitForExit(); }
                return p.ExitCode == 0 ? (exceededDeadline ? "The helper exceeded its deadline, but has now completed. Verify the app state before continuing." : null) : "The administrator helper failed (code " + p.ExitCode + "). Check that the app closed and its files are writable.";
            }
        }
        catch (System.ComponentModel.Win32Exception ex) { return ex.NativeErrorCode == 1223 ? "The administrator request was cancelled." : ex.Message; }
        catch (Exception ex) { return ex.Message; }
    }

    // Jobs: restart <exe> [s:Name=text | d:Name=number]… · deploy <build> <oldVersionsDir> <label> · stop
    public static int Run(string[] a)
    {
        var bar = AppDef.Get("KinetikStats");
        try
        {
            string job = a.Length > 0 ? a[0] : "";
            if (job == "stop") return Running.Stop(bar) ? 0 : 3;
            if ((job == "restart" || job == "settings") && a.Length >= 2)
            {
                bool wasRunning = Running.Is(bar);
                if (wasRunning && !Running.Stop(bar)) return 3;
                // settings to change while it's stopped, so it can't save its old ones over them
                using (var k = Registry.CurrentUser.CreateSubKey(bar.Key))
                    foreach (var pair in a.Skip(2))
                    {
                        int eq = pair.IndexOf('=');
                        if (pair.Length < 3 || pair[1] != ':' || eq < 3) continue;
                        var name = pair.Substring(2, eq - 2); var val = pair.Substring(eq + 1);
                        if (!name.StartsWith("Widget")) continue; // only the widget's look settings
                        int n;
                        if (pair[0] == 'd' && int.TryParse(val, out n)) k.SetValue(name, n, RegistryValueKind.DWord);
                        else if (pair[0] == 's') k.SetValue(name, val);
                    }
                return job == "settings" && !wasRunning ? 0 : Running.StartVerified(bar, a[1], false) ? 0 : 4;
            }
            if (job == "install" && a.Length >= 4)
            {
                bool wasRunning = Running.Is(bar);
                if (wasRunning && !Running.Stop(bar)) return 3;
                try
                {
                    Deploy.Copy(a[1], a[2], a[3]);
                    if (File.Exists(Places.BarRunning) && !string.Equals(Path.GetFullPath(a[1]), Path.GetFullPath(Places.BarRunning), StringComparison.OrdinalIgnoreCase))
                        Deploy.Copy(Places.BarRunning, a[1], a[3]);
                }
                catch { if (wasRunning) Running.Start(bar, a[1], false); throw; }
                return !wasRunning || Running.StartVerified(bar, File.Exists(Places.BarRunning) ? Places.BarRunning : a[1], false) ? 0 : 4;
            }
            if (job == "deploy" && a.Length >= 4)
            {
                var pf = Places.BarRunning;
                bool wasRunning = Running.Is(bar);
                if (wasRunning && !Running.Stop(bar)) return 3;
                try
                {
                    if (File.Exists(pf))
                    {
                        Deploy.Copy(pf, a[1], a[3]);
                        if (!Places.Same(pf, a[1])) throw new IOException("The installed Kinetik Stats checksum does not match the selected build.");
                    }
                }
                catch { if (wasRunning) Running.Start(bar, File.Exists(pf) ? pf : a[1], false); throw; }
                return Running.StartVerified(bar, File.Exists(pf) ? pf : a[1], false) ? 0 : 4;
            }
        }
        catch (Exception ex) { MessageBox.Show("Kinetik Hub couldn't finish: " + ex.Message, "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
        return 2;
    }
}

// ======================================================================= Updates from GitHub releases
class Release { public string Tag, Page, Zip, ZipSha, ExeSha, Notes = ""; public Version V; public DateTime Published; }

static class Updates
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // GitHub allows 60 API requests an hour without signing in. A refusal for that (403 or 429) gets a plain explanation,
    // with the time it resets when GitHub says.
    static string Get(string url)
    {
        try { return KinetikSuite.Http.Text(url, 8 << 20, "KinetikHub/" + Program.Version, CancellationToken.None, 20000, "application/vnd.github+json"); }
        catch (WebException ex)
        {
            int code = 0; string reset = null;
            try { var r = ex.Response as HttpWebResponse; if (r != null) { code = (int)r.StatusCode; reset = r.Headers["X-RateLimit-Reset"]; } } catch { }
            if (code == 0 && (ex.Message.Contains("(403)") || ex.Message.Contains("(429)"))) code = 403;
            if (code != 403 && code != 429) throw;
            long unix; string until = "";
            if (long.TryParse(reset, out unix)) until = " until " + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToLocalTime().ToString("h:mm tt");
            throw new IOException("GitHub allows 60 update checks an hour without signing in, and they're used up" + (until == "" ? " for now" : until) + ". Try again then.");
        }
    }

    public static Version ParseTag(string tag)
    {
        var m = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    // The latest release of repo, or null when it has none (or isn't published).
    public static Release Latest(AppDef a)
    {
        string json;
        try { json = Get("https://api.github.com/repos/" + a.Repo + "/releases/latest"); }
        catch (WebException ex)
        {
            var r = ex.Response as HttpWebResponse;
            if (r != null && r.StatusCode == HttpStatusCode.NotFound) return null;
            throw;
        }
        return Parse(a, new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>);
    }

    // The newest releases, newest first, leaving out drafts and pre-releases and any without a version in the tag.
    public static List<Release> List(AppDef a, int count)
    {
        var list = new List<Release>();
        string json;
        try { json = Get("https://api.github.com/repos/" + a.Repo + "/releases?per_page=" + count); }
        catch (WebException ex)
        {
            var r = ex.Response as HttpWebResponse;
            if (r != null && r.StatusCode == HttpStatusCode.NotFound) return list;
            throw;
        }
        var arr = new JavaScriptSerializer { MaxJsonLength = 8 << 20 }.DeserializeObject(json) as object[];
        if (arr == null) return list;
        foreach (var o in arr)
        {
            var d = o as Dictionary<string, object>;
            if (d == null || true.Equals(d.ContainsKey("draft") ? d["draft"] : null) || true.Equals(d.ContainsKey("prerelease") ? d["prerelease"] : null)) continue;
            var rel = Parse(a, d);
            if (rel != null && rel.V != null) list.Add(rel);
        }
        return list.OrderByDescending(r => r.V).ToList();
    }

    static Release Parse(AppDef a, Dictionary<string, object> root)
    {
        if (root == null) return null;
        var rel = new Release { Tag = root["tag_name"] as string, Page = root["html_url"] as string };
        rel.V = ParseTag(rel.Tag);
        object when;
        DateTime published;
        if (root.TryGetValue("published_at", out when) && DateTime.TryParse(when as string, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out published)) rel.Published = published;
        object body;
        var notes = root.TryGetValue("body", out body) ? body as string ?? "" : "";
        rel.Notes = notes.Replace("\r\n", "\n").Trim();
        var hash = System.Text.RegularExpressions.Regex.Match(notes, "SHA-256 of `" + System.Text.RegularExpressions.Regex.Escape(a.Exe) + "`:?\\*?\\*?\\s*`([0-9A-Fa-f]{64})`");
        if (hash.Success) rel.ExeSha = hash.Groups[1].Value.ToUpperInvariant();
        object assets;
        if (root.TryGetValue("assets", out assets) && assets is object[])
            foreach (var o in (object[])assets)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var name = d["name"] as string ?? "";
                if (name.Equals(a.Id + ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    rel.Zip = d["browser_download_url"] as string;
                    object digest;
                    var value = d.TryGetValue("digest", out digest) ? digest as string : null;
                    if (value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^sha256:[0-9a-fA-F]{64}$")) rel.ZipSha = value.Substring(7).ToUpperInvariant();
                }
            }
        return rel;
    }

    public static string Encode(Release r) { return r == null ? "" : (r.Tag ?? "") + "|" + (r.Page ?? "") + "|" + (r.Zip ?? "") + "|" + (r.ZipSha ?? "") + "|" + (r.ExeSha ?? ""); }
    public static Release Decode(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var p = s.Split('|');
        var r = new Release { Tag = p[0], Page = p.Length > 1 ? p[1] : "", Zip = p.Length > 2 && p[2] != "" ? p[2] : null };
        r.ZipSha = p.Length > 3 && p[3] != "" ? p[3] : null; r.ExeSha = p.Length > 4 && p[4] != "" ? p[4] : null;
        r.V = ParseTag(r.Tag);
        return r.V == null ? null : r;
    }

    // Downloads the release's zip and returns the path of the exe inside it (extracted to a temp folder, which the caller
    // removes with CleanUp(folder) once it's installed).
    public static string Download(AppDef a, Release r) { string folder; return Download(a, r, out folder); }
    public static void CleanUp(string folder) { if (folder != null) try { Directory.Delete(folder, true); } catch { } }
    public static string Download(AppDef a, Release r, out string folder)
    {
        folder = null;
        if (r.ZipSha == null && r.ExeSha == null) throw new IOException("This release has no verified digest. Check for updates again, or install a verified release manually.");
        if (r.Zip == null || !r.Zip.StartsWith("https://github.com/" + a.Repo + "/releases/download/", StringComparison.Ordinal)) throw new IOException("The release download URL is unexpected.");
        var bytes = KinetikSuite.Http.Get(r.Zip, 64 << 20, "KinetikHub/" + Program.Version, CancellationToken.None, 60000);
        if (r.ZipSha != null && !string.Equals(KinetikSuite.Hashes.Of(bytes), r.ZipSha, StringComparison.OrdinalIgnoreCase)) throw new IOException("The release archive checksum does not match.");
        var dir = Path.Combine(Path.GetTempPath(), "KinetikHub", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); folder = dir;
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0; int count = 0;
            using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read))
                foreach (var entry in archive.Entries)
                {
                    if (++count > 256 || (expanded += entry.Length) > (128 << 20) || entry.Length > (32 << 20)) throw new IOException("The release archive is too large.");
                    var relative = entry.FullName.Replace('\\', '/');
                    if (relative.EndsWith("/")) continue;
                    var target = Path.GetFullPath(Path.Combine(dir, relative));
                    if (!target.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !names.Add(target) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("The archive contains an unsafe or duplicate path.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[16384]; long written = 0; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        { written += read; if (written > entry.Length || written > (32 << 20)) throw new IOException("Archive entry exceeded its declared size."); output.Write(buffer, 0, read); }
                        if (written != entry.Length) throw new IOException("Archive entry was incomplete.");
                    }
                }
            var executables = Directory.GetFiles(dir, a.Exe, SearchOption.AllDirectories);
            if (executables.Length != 1) throw new IOException("The release must contain exactly one " + a.Exe);
            var exe = executables[0];
            if (r.ExeSha != null && !string.Equals(KinetikSuite.Hashes.FileHash(exe), r.ExeSha, StringComparison.OrdinalIgnoreCase)) throw new IOException("The executable checksum does not match.");
            var identity = AssemblyName.GetAssemblyName(exe).Name;
            if (identity != a.Id && identity != a.Id + ".new") throw new IOException("The release contains a different application.");
            if (Places.VersionOf(exe) != r.V) throw new IOException("The executable version does not match the release tag.");
            return exe;
        }
        catch { try { Directory.Delete(dir, true); } catch { } folder = null; throw; }
    }

    // Hub replacing its own exe: the running one moves to Old Versions (Windows allows renaming a running exe), the new
    // one takes its place, and a new Hub starts that waits for this one to close. Null when it worked.
    public static string ReplaceSelf(string newExe, string tag)
    {
        var cur = Application.ExecutablePath;
        var ov = Places.OldVersionsDir(cur);
        Directory.CreateDirectory(ov);
        var kept = Path.Combine(ov, "KinetikHub-" + Program.Version + "-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".exe");
        try { File.Move(cur, kept); }
        catch (Exception ex) { return "Couldn't move the current Kinetik Hub aside: " + ex.Message; }
        try { File.Copy(newExe, cur); }
        catch (Exception ex)
        {
            try { if (File.Exists(cur)) File.Delete(cur); File.Move(kept, cur); } catch { }
            return "Couldn't put Kinetik Hub " + tag + " in place: " + ex.Message;
        }
        Places.InvalidateFiles();
        try { Process.Start(new ProcessStartInfo(cur, "--wait-pid " + Process.GetCurrentProcess().Id) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(cur) }); }
        catch (Exception ex) { return "Kinetik Hub " + tag + " is in place, but didn't start: " + ex.Message + ". Start it again by hand."; }
        return null;
    }

}

// One app's place against GitHub, for the Updates window and the rows.
class UpdateStatus
{
    public string Text;
    public Color Col;
    public string Action;  // "Install", "Update", or null
    public Release Release;

    public static UpdateStatus Of(AppDef a, string exe, Release latest, bool checkedYet, string skippedTag)
    {
        var s = new UpdateStatus { Col = UI.Sub, Release = latest };
        if (a.Repo == null) { s.Text = "Not on GitHub"; return s; }
        if (latest == null) { s.Text = checkedYet ? "No release on GitHub yet" : "Not checked yet"; return s; }
        if (exe == null)
        {
            s.Text = "Not installed  ·  " + latest.Tag + " on GitHub"; s.Col = UI.Accent;
            if (latest.Zip != null) s.Action = "Install";
            return s;
        }
        var v = Places.VersionOf(exe);
        if (v != null && latest.V > v)
        {
            bool skipped = skippedTag == latest.Tag;
            s.Text = (skipped ? "Skipped " : "Update available: ") + latest.Tag + (latest.Published != DateTime.MinValue ? "  ·  " + latest.Published.ToLocalTime().ToString("d MMM") : "");
            s.Col = skipped ? UI.Sub : UI.Warn;
            if (latest.Zip != null) s.Action = "Update";
            return s;
        }
        if (v != null && latest.V < v) { s.Text = "Newer than GitHub (" + latest.Tag + "): a local build"; s.Col = UI.Accent; return s; }
        s.Text = "Up to date"; s.Col = UI.Good;
        return s;
    }
}

// ======================================================================= Settings for every app in one file
static class SuiteBackup
{
    public static string DefaultFile { get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Kinetik-suite-settings.reg"); } }

    static IEnumerable<string> Keys { get { return AppDef.All.Select(a => a.Id).Concat(new[] { "KinetikHub" }); } }

    // Exports each app's key and joins them into one .reg file. Returns the apps included.
    public static List<string> Save(string file)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "KinetikHub-export");
        Directory.CreateDirectory(tmp);
        var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        var done = new List<string>();
        foreach (var id in Keys)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\" + id)) if (k == null) continue; } catch { continue; }
            var part = Path.Combine(tmp, id + ".reg");
            if (!Reg.Run("export \"HKCU\\Software\\" + id + "\" \"" + part + "\" /y")) continue;
            var lines = File.ReadAllLines(part); // reg.exe writes UTF-16 with a BOM, which ReadAllLines detects
            sb.Append("\r\n").Append(string.Join("\r\n", lines.Skip(1)).Trim('\r', '\n')).Append("\r\n");
            done.Add(id);
            try { File.Delete(part); } catch { }
        }
        File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
        return done;
    }

    // The app ids a backup file holds; null if it touches anything that isn't a Kinetik app's own key.
    public static List<string> Contents(string file)
    {
        try
        {
            var ids = new List<string>();
            foreach (var line in File.ReadAllLines(file))
            {
                var l = line.Trim();
                if (!l.StartsWith("[")) continue;
                var id = Keys.FirstOrDefault(k => l.StartsWith("[HKEY_CURRENT_USER\\Software\\" + k + "]", StringComparison.OrdinalIgnoreCase) ||
                                                   l.StartsWith("[HKEY_CURRENT_USER\\Software\\" + k + "\\", StringComparison.OrdinalIgnoreCase));
                if (id == null) return null;
                if (!ids.Contains(id)) ids.Add(id);
            }
            return ids.Count > 0 ? ids : null;
        }
        catch { return null; }
    }

    // Imports the file, leaving out the apps in skip.
    public static bool Restore(string file, ICollection<string> skip)
    {
        var lines = File.ReadAllLines(file);
        var keep = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        bool on = true;
        foreach (var line in lines.Skip(1))
        {
            var l = line.Trim();
            if (l.StartsWith("["))
                on = !skip.Any(k => l.StartsWith("[HKEY_CURRENT_USER\\Software\\" + k + "]", StringComparison.OrdinalIgnoreCase) ||
                                   l.StartsWith("[HKEY_CURRENT_USER\\Software\\" + k + "\\", StringComparison.OrdinalIgnoreCase));
            if (on) keep.Append(line).Append("\r\n");
        }
        var included = Contents(file);
        if (included == null) return false;
        included = included.Where(id => !skip.Contains(id)).ToList();
        if (included.Count == 0) return true;
        var tmp = Path.Combine(Path.GetTempPath(), "KinetikHub-restore-" + Guid.NewGuid().ToString("N") + ".reg");
        File.WriteAllText(tmp, keep.ToString(), Encoding.Unicode);
        try { return KinetikSuite.RegistryFiles.Replace(tmp, included); }
        finally { try { File.Delete(tmp); } catch { } }
    }
}

// ======================================================================= Tray and startup
class HubContext : ApplicationContext
{
    readonly Settings c = Settings.Load();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly ContextMenuStrip menu = KinetikSuite.MenuFocus.Attach(new ContextMenuStrip());
    readonly EventWaitHandle showEvent;
    readonly RegisteredWaitHandle showWait;
    readonly System.Windows.Forms.Timer daily = new System.Windows.Forms.Timer { Interval = 60 * 60 * 1000 };
    readonly Control invoker = new Control();
    HubForm form;
    UpdatesForm updatesForm;

    public HubContext()
    {
        Places.Cfg = c;
        invoker.CreateControl();
        try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Application; }
        tray.Text = Program.Demo ? "Kinetik Hub (demo)" : "Kinetik Hub";
        tray.ContextMenuStrip = menu;
        menu.Opening += (s, e) => BuildMenu();
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowHub(); };
        tray.BalloonTipClicked += (s, e) => ShowUpdates();
        Backup.BeforeExit = () => tray.Visible = false;
        tray.Visible = true;

        bool createdNew;
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignal.EventName + (Program.Demo ? "Demo" : ""), out createdNew);
        showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (st, to) => KinetikSuite.Ui.Post(invoker, ShowHub), null, -1, false);

        daily.Tick += (s, e) => MaybeCheckUpdates(false);
        daily.Start();

        if (Program.AtStartup)
        {
            // Give the desktop a moment to settle, then start each app in turn.
            var wait = new System.Windows.Forms.Timer { Interval = 4000 };
            wait.Tick += (s, e) =>
            {
                wait.Stop(); wait.Dispose();
                StartAll(AppDef.All.Where(a => c.Starts(a.Id) && !a.Admin).ToList(), () =>
                {
                    if (!c.StayInTray) ExitHub();
                    else MaybeCheckUpdates(false);
                });
            };
            wait.Start();
        }
        else if (Program.OpenUpdates) ShowUpdates(); // --updates: straight to the Updates window
        else
        {
            ShowHub();
            MaybeCheckUpdates(false);
        }
        if (Program.ShotPath != null)
        {
            tray.Visible = false;
            var shot = new System.Windows.Forms.Timer { Interval = 1500 };
            shot.Tick += (s, e) =>
            {
                shot.Stop();
                Form shown = Program.OpenUpdates ? (Form)updatesForm : form;
                try { using (var bmp = new Bitmap(shown.Width, shown.Height)) { shown.DrawToBitmap(bmp, new Rectangle(0, 0, shown.Width, shown.Height)); bmp.Save(Program.ShotPath, ImageFormat.Png); } } catch { }
                ExitHub();
            };
            shot.Start();
        }
    }

    public Settings Cfg { get { return c; } }

    public void ShowHub()
    {
        if (form == null || form.IsDisposed) { form = new HubForm(this); form.Show(); }
        else { if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal; form.Show(); form.Activate(); }
    }

    public void Refresh()
    {
        if (form != null && !form.IsDisposed) form.RefreshRows();
        if (updatesForm != null && !updatesForm.IsDisposed) updatesForm.RefreshRows();
    }

    public void ShowUpdates()
    {
        if (updatesForm == null || updatesForm.IsDisposed) { updatesForm = new UpdatesForm(this); updatesForm.Show(); }
        else { if (updatesForm.WindowState == FormWindowState.Minimized) updatesForm.WindowState = FormWindowState.Normal; updatesForm.Activate(); }
        DateTime last;
        if (!DateTime.TryParse(c.LastCheck, null, DateTimeStyles.RoundtripKind, out last) || (DateTime.UtcNow - last).TotalHours >= 1) MaybeCheckUpdates(true);
    }

    public bool CheckingUpdates { get { return checkingUpdates; } }

    // Starts the apps one after another, 1.2 s apart, so they don't all fight for the disk at login.
    public void StartAll(List<AppDef> apps, Action done)
    {
        if (Jobs.Busy) { Jobs.ShowError("A suite operation is still running. Try again when it finishes."); return; }
        var queue = new Queue<AppDef>(apps.Where(a => !Running.Is(a)));
        var t = new System.Windows.Forms.Timer { Interval = 1200 };
        bool ended = false;
        Action next = null;
        next = () =>
        {
            if (Jobs.Busy) return;
            if (queue.Count == 0) { ended = true; t.Stop(); t.Dispose(); Refresh(); if (done != null) done(); return; }
            var a = queue.Dequeue();
            Jobs.Run(() => Running.StartVerified(a, Places.Installed(a), a.Admin) ? null : "Could not start " + a.Title + ".", error => { Jobs.ShowError(error); Refresh(); });
        };
        t.Tick += (s, e) => next();
        next();
        if (!ended) t.Start();
    }

    // Restarts the apps that are running now (stopping them all first, then starting them in turn).
    public void RestartRunning(IEnumerable<AppDef> apps, Action<AppDef> whileStopped)
    {
        var targets = apps.Where(a => !a.Admin).ToList();
        Jobs.Run(() => Running.WithStopped(targets, () => { if (whileStopped != null) foreach (var app in targets) whileStopped(app); }),
            error => { Jobs.ShowError(error); Refresh(); });
    }

    // Once a day (or now, when asked) checks GitHub for each app's latest release.
    bool checkingUpdates;
    public void MaybeCheckUpdates(bool now)
    {
        if (Program.Demo || checkingUpdates) return;
        DateTime last;
        if (!now && (!c.CheckUpdates || (DateTime.TryParse(c.LastCheck, null, DateTimeStyles.RoundtripKind, out last) && (DateTime.UtcNow - last).TotalHours < 20))) return;
        checkingUpdates = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string err = null;
            foreach (var a in AppDef.WithSelf.Where(x => x.Repo != null))
                try { found[a.Id] = Updates.Encode(Updates.Latest(a)); }
                catch (Exception ex) { err = ex.Message; }
            try
            {
                invoker.BeginInvoke((Action)(() =>
                {
                    checkingUpdates = false;
                    var map = Settings.Map(c.Latest);
                    foreach (var p in found) map[p.Key] = p.Value;
                    c.Latest = Settings.Unmap(map);
                    if (found.Count > 0) c.LastCheck = DateTime.UtcNow.ToString("o");
                    c.Save();
                    Refresh();
                    if (now && err != null && found.Count == 0) { MessageBox.Show("Couldn't check for updates: " + err, "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    // with "Install updates automatically", the apps that don't need an administrator prompt update by themselves
                    var auto = c.AutoInstall ? AppDef.All.Where(a => !a.Admin && UpdateFor(a) != null && UpdateFor(a).Zip != null).ToList() : new List<AppDef>();
                    if (auto.Count > 0 && !Jobs.Busy) { InstallUpdates(auto, true); return; }
                    var ups = AppDef.WithSelf.Where(a => UpdateFor(a) != null).Select(a => a.Title).ToList();
                    bool quiet = (form != null && !form.IsDisposed && form.Visible) || (updatesForm != null && !updatesForm.IsDisposed && updatesForm.Visible);
                    if (ups.Count > 0 && !quiet)
                        tray.ShowBalloonTip(8000, "Kinetik updates", (ups.Count == 1 ? "A new version of " + ups[0] + " is out." : "New versions are out for " + string.Join(", ", ups) + ".") + " Click to see them.", ToolTipIcon.None);
                }));
            }
            catch { }
        });
    }

    // The latest release found on GitHub (null if none, or not checked yet).
    public Release LatestFor(AppDef a)
    {
        string s;
        return Settings.Map(c.Latest).TryGetValue(a.Id, out s) ? Updates.Decode(s) : null;
    }
    public bool CheckedFor(AppDef a) { return Settings.Map(c.Latest).ContainsKey(a.Id); }
    public string ExeOf(AppDef a) { return a == AppDef.Self ? Application.ExecutablePath : Places.Installed(a); }
    public string SkippedTag(AppDef a) { string s; return Settings.Map(c.Skipped).TryGetValue(a.Id, out s) ? s : ""; }

    public void Skip(AppDef a, string tag)
    {
        var m = Settings.Map(c.Skipped);
        if (string.IsNullOrEmpty(tag)) m.Remove(a.Id); else m[a.Id] = tag;
        c.Skipped = Settings.Unmap(m); c.Save(); Refresh();
    }

    // The release newer than the installed copy, if there is one and it hasn't been skipped.
    public Release UpdateFor(AppDef a)
    {
        var r = LatestFor(a);
        var exe = ExeOf(a);
        if (r == null || exe == null || SkippedTag(a) == r.Tag) return null;
        var v = Places.VersionOf(exe);
        return v != null && r.V > v ? r : null;
    }

    public UpdateStatus StatusOf(AppDef a) { return UpdateStatus.Of(a, ExeOf(a), LatestFor(a), CheckedFor(a), SkippedTag(a)); }

    // Installs the given apps' releases one after another (the latest of each, or the chosen one), then reports. Hub
    // itself goes last, since it restarts. Silent runs (automatic updates) report in a tray balloon instead of a box.
    public void InstallUpdates(List<AppDef> apps, bool silent, Release chosen = null)
    {
        if (Jobs.Busy) { if (!silent) Jobs.ShowError("A suite operation is still running. Try again when it finishes."); return; }
        var plan = apps.Where(a => a != AppDef.Self).Select(a => Tuple.Create(a, chosen ?? LatestFor(a))).Where(p => p.Item2 != null && p.Item2.Zip != null).ToList();
        var self = apps.Contains(AppDef.Self) ? chosen ?? LatestFor(AppDef.Self) : null;
        var done = new List<string>(); var failed = new List<string>();
        Jobs.Run(() =>
        {
            foreach (var p in plan)
            {
                Jobs.Progress = "Installing " + p.Item1.Title + " " + p.Item2.Tag + " from GitHub…";
                var err = Deploy.FromRelease(p.Item1, p.Item2);
                if (err == null) done.Add(p.Item1.Title + " " + p.Item2.Tag); else failed.Add(err);
            }
            return failed.Count == 0 ? null : string.Join("\n\n", failed);
        }, error =>
        {
            Places.InvalidateFiles(); Refresh();
            if (silent)
            {
                if (done.Count > 0) tray.ShowBalloonTip(8000, "Kinetik updates", "Updated " + string.Join(", ", done) + ".", ToolTipIcon.None);
                if (error != null) tray.ShowBalloonTip(8000, "Kinetik updates", "Some updates didn't install. Open Updates in Kinetik Hub to try again.", ToolTipIcon.Warning);
            }
            else
            {
                if (error != null) MessageBox.Show((done.Count > 0 ? "Installed " + string.Join(", ", done) + ".\n\n" : "") + error, "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (done.Count > 0 && self == null) { if (form != null && !form.IsDisposed) form.Note("Installed " + string.Join(", ", done) + "."); }
            }
            if (self != null && self.Zip != null) UpdateSelf(self, silent);
        });
    }

    // Hub's own update: downloaded and checked off the UI thread, then swapped in and restarted.
    public void UpdateSelf(Release r, bool silent)
    {
        if (Program.Demo) return;
        Jobs.Run(() =>
        {
            Jobs.Progress = "Downloading Kinetik Hub " + r.Tag + "…";
            string temp = null;
            try
            {
                var exe = Updates.Download(AppDef.Self, r, out temp);
                var err = Updates.ReplaceSelf(exe, r.Tag);
                return err ?? "";
            }
            catch (Exception ex) { return "Couldn't update Kinetik Hub: " + ex.Message; }
            finally { Updates.CleanUp(temp); }
        }, error =>
        {
            if (error == "") { tray.Visible = false; ExitHub(); return; } // the new Hub takes over
            if (silent) tray.ShowBalloonTip(8000, "Kinetik Hub", error, ToolTipIcon.Warning);
            else Jobs.ShowError(error);
            Refresh();
        });
    }

    void BuildMenu()
    {
        var old = menu.Items.Cast<ToolStripItem>().ToList();
        menu.Items.Clear();
        foreach (var it in old) it.Dispose();
        Add(menu.Items, "Open Kinetik Hub", ShowHub);
        menu.Items.Add(new ToolStripSeparator());
        foreach (var a in AppDef.All)
        {
            var exe = Places.Installed(a);
            if (exe == null) continue;
            var app = a;
            bool on = Running.Is(a);
            var it = new ToolStripMenuItem(a.Title + (on ? "" : "   (not running)"));
            if (on) { Add(it.DropDownItems, "Restart", () => HubForm.Restart(this, app)); Add(it.DropDownItems, "Stop", () => HubForm.StopApp(app)); }
            else Add(it.DropDownItems, "Start", () => Jobs.Run(() => Running.StartVerified(app, Places.Installed(app), app.Admin) ? null : "Could not start " + app.Title, error => { Jobs.ShowError(error); Refresh(); }));
            menu.Items.Add(it);
        }
        menu.Items.Add(new ToolStripSeparator());
        Add(menu.Items, "Start all", () => StartAll(AppDef.All.Where(a => Places.Installed(a) != null && (c.Starts(a.Id) || a.Admin)).ToList(), null));
        Add(menu.Items, "Restart running apps", () => RestartRunning(AppDef.All, null));
        var pending = AppDef.WithSelf.Where(a => UpdateFor(a) != null && UpdateFor(a).Zip != null).ToList();
        if (pending.Count > 0) Add(menu.Items, "Install " + pending.Count + (pending.Count == 1 ? " update (" + pending[0].Title + ")" : " updates"), () => InstallUpdates(pending, false));
        Add(menu.Items, "Updates…", ShowUpdates);
        menu.Items.Add(new ToolStripSeparator());
        var st = new ToolStripMenuItem("Start Kinetik Hub at login") { Checked = Startup.IsOn };
        st.Click += (s, e) => { Startup.Set(!Startup.IsOn); Refresh(); };
        menu.Items.Add(st);
        menu.Items.Add(new ToolStripMenuItem("Kinetik Hub " + Program.Version) { Enabled = false });
        Add(menu.Items, "Exit", ExitHub);
    }

    static void Add(ToolStripItemCollection items, string text, Action click)
    {
        var it = new ToolStripMenuItem(text);
        it.Click += (s, e) => click();
        items.Add(it);
    }

    public void ExitHub()
    {
        if (Jobs.Busy) { Jobs.ShowError("A suite operation is still finishing. Close Hub after it completes."); return; }
        tray.Visible = false;
        if (form != null && !form.IsDisposed) form.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { showWait.Unregister(null); tray.Dispose(); menu.Dispose(); daily.Dispose(); showEvent.Dispose(); invoker.Dispose(); }
        base.Dispose(disposing);
    }
}

// ======================================================================= The window
static class UI
{
    public static Color Page { get { return ThemePicker.Page; } }
    public static Color Card { get { return ThemePicker.Card; } }
    public static Color CardHi { get { return ThemePicker.CardHi; } }
    public static Color Accent { get { return ThemePicker.Accent; } }
    public static Color Text { get { return ThemePicker.TextCol; } }
    public static Color Sub { get { return ThemePicker.SubCol; } }
    public static Color Field { get { return KinetikSuite.Look.Current.Hover; } }
    public static Color Good { get { return KinetikSuite.Look.Current.Good; } }
    public static Color Warn { get { return KinetikSuite.Look.Current.Light ? Color.FromArgb(176, 112, 0) : Color.FromArgb(255, 170, 60); } }
    public static Color Off { get { return KinetikSuite.Look.Current.Sub; } }
}

// A flat, rounded button in the window's dark style.
class FlatBtn : Control
{
    bool hot, down;
    public bool Primary;
    public FlatBtn(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = new Font(Draw.Family, 9f);
        BackColor = UI.Card;
    }
    public void Fit() { Width = TextRenderer.MeasureText(Text, Font).Width + (int)(22 * DeviceDpi / 96f); Height = (int)(28 * DeviceDpi / 96f); }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill = Primary ? (down ? Draw.Mix(UI.Accent, Color.Black, 0.2f) : hot ? Draw.Mix(UI.Accent, Color.White, 0.12f) : UI.Accent)
                             : (down ? UI.Field : hot ? Draw.Mix(UI.Field, Color.White, 0.08f) : Draw.Mix(UI.Field, UI.Card, 0.3f));
        if (!Enabled) fill = Draw.Mix(fill, UI.Card, 0.6f);
        using (var p = Draw.Round(r, 6 * DeviceDpi / 96f))
        using (var b = new SolidBrush(fill)) g.FillPath(b, p);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? (Primary ? KinetikSuite.Look.Current.OnAccent : UI.Text) : UI.Off, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

class HubForm : Form
{
    readonly HubContext hub;
    readonly Settings c;
    readonly Panel list = new Panel { AutoScroll = true };
    readonly List<AppRow> rows = new List<AppRow>();
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2000 };
    readonly Label foot = new Label { AutoSize = false };
    readonly CheckBox loginBox = new CheckBox(), trayBox = new CheckBox(), updBox = new CheckBox();
    readonly float k;

    int I(float v) { return (int)Math.Round(v * k); }

    public HubForm(HubContext hub)
    {

        HandleCreated += (s, e) => KinetikSuite.Look.Current.Window(Handle); // title bar in the Kinetik apps' theme
        this.hub = hub; c = hub.Cfg;
        k = DeviceDpi / 96f;
        Text = "Kinetik Hub";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = UI.Page; ForeColor = UI.Text;
        Font = new Font(Draw.Family, 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(I(760), I(640));

        var title = new Label { Text = "Kinetik Hub", Font = new Font(Draw.Family, 16f, FontStyle.Bold), AutoSize = true, Left = I(20), Top = I(16), ForeColor = UI.Text };
        var sub = new Label { Text = "Your Kinetik apps: start them at login, deploy new builds and updates, back up and theme them all at once.", AutoSize = true, Left = I(22), Top = I(50), ForeColor = UI.Sub };
        Controls.Add(title); Controls.Add(sub);

        var bar = new FlowLayoutPanel { Left = I(18), Top = I(78), Width = I(724), Height = I(36), BackColor = UI.Page, WrapContents = false };
        bar.Controls.Add(Btn("App theme…", () => ShowLookMenu(bar.Controls[0])));
        bar.Controls.Add(Btn("Widget theme…", ThemeAll));
        bar.Controls.Add(Btn("Suite settings…", () => { using (var f = new SuiteForm(this)) f.ShowDialog(this); }));
        bar.Controls.Add(Btn("Back up all", BackupAll));
        bar.Controls.Add(Btn("Restore all…", RestoreAll));
        bar.Controls.Add(Btn("Updates…", hub.ShowUpdates));
        bar.Controls.Add(Btn("Start all", () => hub.StartAll(AppDef.All.Where(a => Places.Installed(a) != null && (c.Starts(a.Id) || a.Admin)).ToList(), null)));
        Controls.Add(bar);

        list.SetBounds(I(16), I(122), I(728), I(430));
        KinetikSuite.Look.Current.ScrollBars(list);
        list.BackColor = UI.Page;
        Controls.Add(list);
        int y = 0;
        foreach (var a in AppDef.All)
        {
            var row = new AppRow(this, a, k) { Left = 0, Top = y, Width = I(708) };
            rows.Add(row); list.Controls.Add(row);
            y += row.Height; // the rows join into one card, as in Kinetik's settings
        }

        loginBox.Text = "Start Kinetik Hub at login (it then starts the apps ticked \"At login\")";
        loginBox.SetBounds(I(20), I(562), I(560), I(24)); loginBox.ForeColor = UI.Text;
        loginBox.Checked = Startup.IsOn;
        loginBox.CheckedChanged += (s, e) => { if (Program.Demo) return; Startup.Set(loginBox.Checked); };
        trayBox.Text = "Stay in the tray afterwards";
        trayBox.SetBounds(I(20), I(586), I(260), I(24)); trayBox.ForeColor = UI.Text;
        trayBox.Checked = c.StayInTray;
        trayBox.CheckedChanged += (s, e) => { c.StayInTray = trayBox.Checked; c.Save(); };
        updBox.Text = "Check GitHub for updates once a day";
        updBox.SetBounds(I(290), I(586), I(280), I(24)); updBox.ForeColor = UI.Text;
        updBox.Checked = c.CheckUpdates;
        updBox.CheckedChanged += (s, e) => { c.CheckUpdates = updBox.Checked; c.Save(); };
        foot.SetBounds(I(20), I(612), I(720), I(22)); foot.ForeColor = UI.Sub;
        Controls.AddRange(new Control[] { loginBox, trayBox, updBox, foot });

        poll.Tick += (s, e) => RefreshRows();
        poll.Start();
        RefreshRows();
    }

    FlatBtn Btn(string text, Action click)
    {
        var b = new FlatBtn(text) { Margin = new Padding(0, 0, I(8), 0) };
        b.Fit();
        b.Click += (s, e) => click();
        return b;
    }

    public void RefreshRows()
    {
        foreach (var r in rows) r.RefreshState();
        loginBox.Checked = Startup.IsOn;
        if (c.LastCheck != checkedAt) { checkedAt = c.LastCheck; noteUntil = DateTime.MinValue; }
        if (Jobs.Busy) { foot.Text = Jobs.Progress; return; }
        if (DateTime.Now < noteUntil) return;
        DateTime last;
        foot.Text = DateTime.TryParse(c.LastCheck, null, DateTimeStyles.RoundtripKind, out last)
            ? "Last checked for updates " + last.ToLocalTime().ToString("d MMM, h:mm tt") + "  ·  Kinetik Hub " + Program.Version
            : "Not checked for updates yet  ·  Kinetik Hub " + Program.Version;
    }

    // A message in the footer for a while, then back to the update line.
    DateTime noteUntil; string checkedAt;
    public void Note(string text) { foot.Text = text; noteUntil = DateTime.Now.AddSeconds(8); }

    // Closing the window leaves Hub in the tray (if it's set to stay there).
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !c.StayInTray && !Program.AtStartup) { hub.ExitHub(); return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing) { if (disposing) poll.Dispose(); base.Dispose(disposing); }

    public HubContext Hub { get { return hub; } }

    // ---- per-app actions (also used by the tray menu)
    public static void Restart(HubContext hub, AppDef a)
    {
        var exe = Places.Installed(a);
        Jobs.Run(() =>
        {
            if (a.Admin) return Elevated.Launch("restart", File.Exists(Places.BarRunning) ? Places.BarRunning : exe);
            if (!Running.Stop(a)) return "Could not close " + a.Title + " safely. Exit it from its tray menu first.";
            return Running.StartVerified(a, exe, false) ? null : a.Title + " did not restart.";
        }, error => { Jobs.ShowError(error); hub.Refresh(); });
    }

    public static void StopApp(AppDef a)
    {
        Jobs.Run(() => a.Admin ? Elevated.Launch("stop") : Running.Stop(a) ? null : a.Title + " could not close safely. Exit it from its tray menu.", Jobs.ShowError);
    }

    // ---- toolbar
    // A theme (and, ticked by default, a background opacity) for every app at once. Suite settings has the rest.
    // The theme of every Kinetik app's windows and menus, as a menu under the button.
    void ShowLookMenu(Control under)
    {
        var menu = new ContextMenuStrip { Renderer = KinetikSuite.Look.Current.MenuRenderer() };
        var before = KinetikSuite.Look.Current;
        KinetikSuite.Look.FillMenu(menu.Items, () => KinetikSuite.Look.Recolour(this, before));
        menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
        menu.Show(under, new Point(0, under.Height + 2));
    }

    void ThemeAll()
    {
        string chosen = c.ThemeName;
        var picker = new ThemePicker(c.ThemeName, n => chosen = n) { Text = "Theme for all Kinetik apps" };
        var useOp = new CheckBox { Text = "Background opacity", Checked = true, AutoSize = true, ForeColor = UI.Text, BackColor = picker.BackColor };
        var op = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = I(80), BackColor = UI.Field, ForeColor = UI.Text, FlatStyle = FlatStyle.Flat };
        foreach (var o in CommonMenu.Opacities) op.Items.Add(o + "%");
        op.SelectedIndex = Array.IndexOf(CommonMenu.Opacities, SuiteForm.Current("Opacity", CommonMenu.DefaultOpacity));
        if (op.SelectedIndex < 0) op.SelectedIndex = Array.IndexOf(CommonMenu.Opacities, CommonMenu.DefaultOpacity);
        op.SelectedIndexChanged += (s, e) => useOp.Checked = true;
        var apply = new FlatBtn("Apply to all apps") { Primary = true };
        apply.Fit();
        picker.Controls.AddRange(new Control[] { useOp, op, apply });
        int y = picker.ClientSize.Height + I(6);
        useOp.Left = I(16); useOp.Top = y + I(4);
        op.Left = useOp.Right + I(8); op.Top = y;
        apply.Left = picker.ClientSize.Width - apply.Width - I(16); apply.Top = y;
        picker.ClientSize = new Size(picker.ClientSize.Width, apply.Bottom + I(14));
        apply.Click += (s, e) =>
        {
            picker.Close();
            c.ThemeName = chosen; c.Save();
            var v = new Dictionary<string, object> { { "ThemeName", chosen } };
            if (useOp.Checked) v["Opacity"] = CommonMenu.Opacities[op.SelectedIndex];
            ApplySuite(v);
        };
        picker.Show(this);
    }

    // Sets the shared look settings in every app that has them. Running apps are stopped first, so none can save its old
    // settings over the new ones, then set, then started again. Kinetik's bar takes an administrator prompt.
    public void ApplySuite(Dictionary<string, object> v)
    {
        if (Program.Demo || v.Count == 0) return;
        var targets = AppDef.All.Where(a => a.HasLook && !a.Admin && Places.Installed(a) != null).ToList();
        var bar = AppDef.Get("KinetikStats"); var barExe = Places.Installed(bar);
        bool changeBar = barExe != null && (!Running.Is(bar) || MessageBox.Show("Changing Kinetik Stats' widget requires restarting it as administrator. Apply it too?", "Kinetik Hub", MessageBoxButtons.YesNo) == DialogResult.Yes);
        Jobs.Run(() => Running.WithStopped(targets, () =>
        {
                foreach (var a in targets)
                {
                    using (var existing = Registry.CurrentUser.OpenSubKey(a.Key)) if (existing == null) continue;
                    using (var key = Registry.CurrentUser.CreateSubKey(a.Key)) foreach (var pair in v)
                    {
                        var name = a.Field(pair.Key); if (name == null) continue;
                        if (pair.Value is string) key.SetValue(name, (string)pair.Value);
                        else key.SetValue(name, pair.Value is bool ? ((bool)pair.Value ? 1 : 0) : (int)pair.Value, RegistryValueKind.DWord);
                    }
                }
                if (changeBar)
                {
                    var pairs = v.Select(pair => (pair.Value is string ? "s:" : "d:") + bar.Field(pair.Key) + "=" + (pair.Value is bool ? ((bool)pair.Value ? "1" : "0") : pair.Value.ToString()));
                    var barError = Elevated.Launch(new[] { "settings", File.Exists(Places.BarRunning) ? Places.BarRunning : barExe }.Concat(pairs).ToArray());
                    if (barError != null) throw new IOException(barError);
                }
        }), error => { if (error != null) Jobs.ShowError(error); else Note("Applied shared settings" + (changeBar ? "." : " to the companion apps; the bar was left unchanged.")); hub.Refresh(); });
    }

    void BackupAll()
    {
        using (var dlg = new SaveFileDialog { Title = "Back up all Kinetik settings", Filter = "Registry file (*.reg)|*.reg", FileName = Path.GetFileName(SuiteBackup.DefaultFile), InitialDirectory = Path.GetDirectoryName(SuiteBackup.DefaultFile) })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var done = SuiteBackup.Save(dlg.FileName);
                MessageBox.Show("Backed up the settings of " + done.Count + " apps (" + string.Join(", ", done.Select(id => { var a = AppDef.Get(id); return a != null ? a.Title : "Kinetik Hub"; })) + ") to\n" + dlg.FileName,
                    "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Couldn't save the backup: " + ex.Message, "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    void RestoreAll()
    {
        using (var dlg = new OpenFileDialog { Title = "Restore Kinetik settings", Filter = "Registry file (*.reg)|*.reg", InitialDirectory = Path.GetDirectoryName(SuiteBackup.DefaultFile) })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var ids = SuiteBackup.Contents(dlg.FileName);
            if (ids == null) { MessageBox.Show("That file isn't a Kinetik settings backup (it holds other registry keys), so it wasn't used.", "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var names = ids.Select(id => { var a = AppDef.Get(id); return a != null ? a.Title : "Kinetik Hub"; });
            if (MessageBox.Show("Replace the settings of " + string.Join(", ", names) + " with the ones in this backup?\n\nApps that are running will restart.",
                    "Kinetik Hub", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var skip = new List<string>();
            var bar = AppDef.Get("KinetikStats");
            if (ids.Contains("KinetikStats") && Running.Is(bar))
            {
                skip.Add("KinetikStats");
                MessageBox.Show("Kinetik Stats is running as administrator, so its settings were left out. Exit it, then restore again to include them.", "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (ids.Contains("KinetikHub")) skip.Add("KinetikHub"); // Hub is running; its own settings are restored from its own Backup menu
            var apps = AppDef.All.Where(a => ids.Contains(a.Id) && !skip.Contains(a.Id)).ToList();
            // stopped first, so none of them can save over the restored settings; then imported; then started again
            var file = dlg.FileName;
            Jobs.Run(() => Running.WithStopped(apps, () =>
            {
                if (!SuiteBackup.Restore(file, skip)) throw new IOException("Could not restore the settings.");
            }), error => { if (error != null) Jobs.ShowError(error); else Note("Settings restored for " + string.Join(", ", apps.Select(app => app.Title)) + "."); hub.Refresh(); });
        }
    }
}

// One app: its icon, name, where it is, whether it's running, what's new for it, and its buttons.
class AppRow : Panel
{
    readonly HubForm owner;
    readonly AppDef a;
    readonly float k;
    readonly PictureBox pic = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom };
    readonly Label name = new Label { AutoSize = false }, where = new Label { AutoSize = false }, news = new Label { AutoSize = false };
    readonly CheckBox login = new CheckBox { Text = "At login" };
    readonly FlatBtn run, stop, deploy, more;
    readonly ContextMenuStrip moreMenu = KinetikSuite.MenuFocus.Attach(new ContextMenuStrip());
    string iconFor, whereTip;
    bool syncing;
    readonly ToolTip tips = new ToolTip();

    int I(float v) { return (int)Math.Round(v * k); }

    public AppRow(HubForm owner, AppDef a, float scale)
    {
        this.owner = owner; this.a = a; k = scale;
        Height = I(64); BackColor = UI.Card;
        DoubleBuffered = true;
        pic.SetBounds(I(14), I(16), I(32), I(32)); pic.BackColor = UI.Card;
        name.SetBounds(I(58), I(11), I(250), I(22)); name.Font = new Font(Draw.Family, 10.5f, FontStyle.Bold); name.ForeColor = UI.Text; name.BackColor = UI.Card;
        where.SetBounds(I(58), I(34), I(330), I(20)); where.Font = new Font(Draw.Family, 8.5f); where.ForeColor = UI.Sub; where.BackColor = UI.Card;
        news.SetBounds(I(250), I(11), I(150), I(22)); news.Font = new Font(Draw.Family, 8.5f, FontStyle.Bold); news.BackColor = UI.Card; news.TextAlign = ContentAlignment.MiddleLeft;
        login.SetBounds(I(360), I(20), I(84), I(24)); login.ForeColor = UI.Text; login.BackColor = UI.Card;
        run = new FlatBtn("Start"); stop = new FlatBtn("Stop"); deploy = new FlatBtn("Deploy") { Primary = true }; more = new FlatBtn("⋯");
        foreach (var b in new[] { run, stop, deploy, more }) b.Height = I(28);
        run.Width = I(70); stop.Width = I(58); deploy.Width = I(96); more.Width = I(32);
        more.Top = run.Top = stop.Top = deploy.Top = I(18);
        more.Left = I(708 - 14) - more.Width;
        stop.Left = more.Left - I(6) - stop.Width;
        run.Left = stop.Left - I(6) - run.Width;
        deploy.Left = run.Left - I(6) - deploy.Width;
        login.Left = deploy.Left - I(8) - login.Width;
        news.SetBounds(I(250), I(11), login.Left - I(258), I(22));
        where.Width = login.Left - where.Left - I(8);
        Controls.AddRange(new Control[] { pic, name, where, news, login, run, stop, deploy, more });

        run.Click += (s, e) => OnRun();
        stop.Click += (s, e) => { HubForm.StopApp(a); RefreshState(); };
        deploy.Click += (s, e) => OnDeploy();
        more.Click += (s, e) => { BuildMore(); moreMenu.Show(more, new Point(0, more.Height)); };
        login.CheckedChanged += (s, e) => { if (!syncing) OnLogin(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(UI.Page);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Rows join into one card: only the first row's top corners and the last row's bottom corners are round.
        var kids = Parent == null ? null : Parent.Controls.OfType<AppRow>().ToList();
        bool first = kids == null || kids.IndexOf(this) == 0, last = kids == null || kids.IndexOf(this) == kids.Count - 1;
        var r = new RectangleF(0, 0, Width - 1, last ? Height - 1 : Height);
        float d = I(10) * 2;
        using (var p = new GraphicsPath())
        {
            if (first) { p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); } else p.AddLine(r.X, r.Y, r.Right, r.Y);
            if (last) { p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); } else p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            using (var b = new SolidBrush(UI.Card)) g.FillPath(b, p);
        }
        if (!first) using (var pen = new Pen(KinetikSuite.Look.Current.Border, 1)) g.DrawLine(pen, I(16), 0, Width - I(16), 0);
    }

    Settings C { get { return owner.Hub.Cfg; } }

    public void RefreshState()
    {
        var exe = Places.Installed(a);
        var build = Places.Build(a);
        bool on = Running.Is(a);
        if (exe != iconFor)
        {
            iconFor = exe;
            var old = pic.Image; pic.Image = null; if (old != null) old.Dispose();
            if (exe != null) try { using (var ic = Icon.ExtractAssociatedIcon(exe)) pic.Image = ic.ToBitmap(); } catch { }
        }
        name.Text = a.Title;
        name.ForeColor = exe != null ? UI.Text : UI.Off;
        var v = exe != null ? Places.VersionOf(exe) : null;
        string state = exe == null ? "Not installed" : on ? (Running.Elevated(a) ? "Running as administrator" : "Running") : "Not running";
        where.Text = state + (exe != null ? "  ·  v" + Places.Short(v) + "  ·  " + Path.GetFileName(Path.GetDirectoryName(exe)) : "  ·  " + a.Blurb);
        if (exe != whereTip) { whereTip = exe; tips.SetToolTip(where, exe ?? ""); tips.SetToolTip(name, exe ?? ""); }
        where.ForeColor = on ? UI.Good : UI.Sub;

        var upd = owner.Hub.UpdateFor(a);
        // a new build: for the bar, one Program Files doesn't have yet; for an app running from its build, one that came after it started;
        // in the older two-folder layout, a build newer than the _Personal copy
        bool barNew = Places.BarPending(a, exe), restart = Places.RestartPending(a, exe), newer = exe != build && Places.BuildIsNewer(a, exe, build);
        if (barNew || newer) { news.Text = "New build " + File.GetLastWriteTime(barNew ? exe : build).ToString("d MMM, h:mm tt"); news.ForeColor = UI.Accent; }
        else if (restart) { news.Text = "New build: restart to use it"; news.ForeColor = UI.Accent; }
        else if (upd != null) { news.Text = "Update: " + upd.Tag; news.ForeColor = UI.Warn; }
        else news.Text = "";
        var latest = owner.Hub.LatestFor(a);
        bool canInstall = exe == null && latest != null && latest.Zip != null;
        if (canInstall) { news.Text = latest.Tag + " on GitHub"; news.ForeColor = UI.Accent; }
        deploy.Visible = canInstall || (exe != null && (barNew || newer || restart || (upd != null && upd.Zip != null)));
        deploy.Text = canInstall ? "Install" : barNew || newer ? "Deploy build" : restart ? "Restart now" : "Update";

        run.Enabled = exe != null;
        run.Text = on ? "Restart" : "Start";
        stop.Enabled = on;
        syncing = true;
        if (a.Admin)
        {
            string description = "Demo startup state"; int startupState = Program.Demo ? 1 : OwnStartup.BarState(out description);
            login.ThreeState = true;
            login.CheckState = startupState < 0 ? CheckState.Indeterminate : startupState > 0 ? CheckState.Checked : CheckState.Unchecked;
            tips.SetToolTip(login, description);
        }
        else login.Checked = C.Starts(a.Id);
        login.Enabled = exe != null && !a.Admin; // Kinetik's bar keeps its own administrator logon task
        syncing = false;
    }

    void OnRun()
    {
        if (Running.Is(a)) HubForm.Restart(owner.Hub, a);
        else Jobs.Run(() => Running.StartVerified(a, Places.Installed(a), a.Admin) ? null : "Could not start " + a.Title + ".", error => { Jobs.ShowError(error); owner.RefreshRows(); });
    }

    void OnLogin()
    {
        // read once: the 2-second refresh resets the box from the saved settings while the questions below are open
        bool on = login.Checked;
        if (Program.Demo) { C.SetStarts(a.Id, on); return; }
        var moved = Settings.Map(C.Moved);
        if (on)
        {
            if (OwnStartup.Has(a) && MessageBox.Show(a.Title + " also starts by itself at login. Let Kinetik Hub start it instead?\n\n" +
                    "Its own startup shortcut is kept, and put back if you untick \"At login\" here.", "Kinetik Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var what = OwnStartup.TakeOver(a);
                if (what != "") moved[a.Id] = what;
            }
            if (!Startup.IsOn && MessageBox.Show("Kinetik Hub isn't set to start at login yet, so it can't start " + a.Title + ". Turn that on?",
                    "Kinetik Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Startup.Set(true);
        }
        else
        {
            string what;
            if (moved.TryGetValue(a.Id, out what)) { OwnStartup.HandBack(what); moved.Remove(a.Id); }
        }
        C.Moved = Settings.Unmap(moved);
        C.SetStarts(a.Id, on);
        C.Save();
        owner.RefreshRows();
    }

    void OnDeploy()
    {
        var exe = Places.Installed(a);
        var build = Places.Build(a);
        if (exe == null)
        {
            var latest = owner.Hub.LatestFor(a);
            if (latest == null || latest.Zip == null) return;
            if (MessageBox.Show("Download " + a.Title + " " + latest.Tag + " from GitHub and install it in\n" + Path.GetDirectoryName(Places.InstallTarget(a)) + "?",
                    "Kinetik Hub", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            owner.Hub.InstallUpdates(new List<AppDef> { a }, false);
            return;
        }
        if (Places.BarPending(a, exe))
        {
            if (MessageBox.Show("Put the new build of Kinetik Stats (built " + File.GetLastWriteTime(exe).ToString("d MMM, h:mm tt") + ") in place?\n\n" +
                    "It runs as administrator, so Windows asks first. The current copy in Program Files is kept in Old Versions.", "Kinetik Hub", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Perform(() => Deploy.Bar(exe, "build-" + DateTime.Now.ToString("HHmm")), "Kinetik Stats is running its new build.");
            return;
        }
        if (Places.RestartPending(a, exe)) { HubForm.Restart(owner.Hub, a); RefreshState(); return; }
        if (exe != build && Places.BuildIsNewer(a, exe, build))
        {
            var bv = Places.VersionOf(build);
            if (MessageBox.Show("Install the new build of " + a.Title + " (v" + Places.Short(bv) + ", built " + File.GetLastWriteTime(build).ToString("d MMM, h:mm tt") + ")?\n\n" +
                    "The current one is kept in Old Versions" + (Running.Is(a) ? ", and " + a.Title + " restarts" : "") + ".", "Kinetik Hub", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Perform(() => Deploy.Install(a, exe, build, "build-" + DateTime.Now.ToString("HHmm")), "Deployed the new build of " + a.Title + ".");
            return;
        }
        var upd = owner.Hub.UpdateFor(a);
        if (upd == null || upd.Zip == null) return;
        if (MessageBox.Show("Download and install " + a.Title + " " + upd.Tag + " from GitHub?\n\nThe current one is kept in Old Versions.", "Kinetik Hub", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        owner.Hub.InstallUpdates(new List<AppDef> { a }, false);
    }

    void Perform(Func<string> work, string ok)
    { deploy.Enabled = false; Jobs.Run(work, error => { if (!IsDisposed) { deploy.Enabled = true; Finish(error, ok); } }); }

    void Finish(string err, string ok)
    {
        if (err != null) MessageBox.Show(err, "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else owner.Note(ok);
        owner.RefreshRows();
    }

    void BuildMore()
    {
        var old = moreMenu.Items.Cast<ToolStripItem>().ToList();
        moreMenu.Items.Clear();
        foreach (var it in old) it.Dispose();
        var exe = Places.Installed(a);
        Add("Open its folder", exe != null, () => Process.Start("explorer.exe", "/select,\"" + exe + "\""));
        var ov = exe != null ? Places.OldVersionsDir(exe) : null;
        Add("Open Old Versions", ov != null && Directory.Exists(ov), () => Process.Start("explorer.exe", "\"" + ov + "\""));
        Add("Locate the exe…", true, Locate);
        if (Settings.Map(C.Paths).ContainsKey(a.Id)) Add("Find it automatically again", true, () => { var m = Settings.Map(C.Paths); m.Remove(a.Id); C.Paths = Settings.Unmap(m); C.Save(); owner.RefreshRows(); });
        moreMenu.Items.Add(new ToolStripSeparator());
        Add("Back up its settings beside it", exe != null, () => { Deploy.ExportSettings(a, exe); MessageBox.Show("Saved " + a.Id + "-settings.reg beside " + Path.GetFileName(exe) + ".", "Kinetik Hub", MessageBoxButtons.OK, MessageBoxIcon.Information); });
        if (a.Repo != null)
        {
            Add("Versions and release notes…", true, () => VersionsForm.Open(owner, owner.Hub, a));
            Add("Releases on GitHub", true, () => { try { Process.Start("https://github.com/" + a.Repo + "/releases"); } catch { } });
        }
    }

    void Add(string text, bool enabled, Action click)
    {
        var it = new ToolStripMenuItem(text) { Enabled = enabled };
        it.Click += (s, e) => { try { click(); } catch { } };
        moreMenu.Items.Add(it);
    }

    void Locate()
    {
        using (var dlg = new OpenFileDialog { Title = "Where is " + a.Exe + "?", Filter = a.Exe + "|" + a.Exe, FileName = a.Exe })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var m = Settings.Map(C.Paths);
            m[a.Id] = dlg.FileName;
            C.Paths = Settings.Unmap(m);
            C.Save();
            owner.RefreshRows();
        }
    }
}


// ======================================================================= Suite settings: the shared look settings, for every app at once
// Each row has a tick box; only the ticked ones are changed. Picking a value ticks its row.
class SuiteForm : Form
{
    readonly HubForm owner;
    readonly float k;
    readonly List<Tuple<string, CheckBox, Func<object>>> rows = new List<Tuple<string, CheckBox, Func<object>>>();
    string theme;

    int I(float v) { return (int)Math.Round(v * k); }

    public static string Label(string key)
    {
        switch (key)
        {
            case "ThemeName": return "theme";
            case "Opacity": return "background opacity";
            case "Scale": return "size";
            case "FontName": return "font";
            case "Layer": return "placement";
            case "Locked": return "lock position";
            case "Snap": return "snap to screen edges";
            case "ClickThrough": return "click-through";
        }
        return key;
    }

    // What most of the installed apps have now, to start the rows from.
    public static int Current(string key, int fallback)
    {
        var vals = new List<int>();
        foreach (var a in AppDef.All.Where(x => x.HasLook))
            try { var f = a.Field(key); using (var r = Registry.CurrentUser.OpenSubKey(a.Key)) { var v = r == null || f == null ? null : r.GetValue(f); if (v is int) vals.Add((int)v); } } catch { }
        return vals.Count == 0 ? fallback : vals.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
    }
    static string CurrentText(string key, string fallback)
    {
        var vals = new List<string>();
        foreach (var a in AppDef.All.Where(x => x.HasLook && !x.Admin))
            try { using (var r = Registry.CurrentUser.OpenSubKey(a.Key)) { var v = r == null ? null : r.GetValue(a.Field(key)) as string; if (v != null) vals.Add(v); } } catch { }
        return vals.Count == 0 ? fallback : vals.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
    }

    public SuiteForm(HubForm owner)
    {

        HandleCreated += (s, e) => KinetikSuite.Look.Current.Window(Handle); // title bar in the Kinetik apps' theme
        this.owner = owner;
        k = owner.DeviceDpi / 96f;
        Text = "Kinetik suite settings";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = UI.Page; ForeColor = UI.Text; Font = new Font(Draw.Family, 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(I(520), I(470));

        Controls.Add(new Label { Text = "Settings for every Kinetik app at once", Font = new Font(Draw.Family, 12.5f, FontStyle.Bold), AutoSize = true, Left = I(18), Top = I(16) });
        Controls.Add(new Label { Text = "Tick what to change. Apps that are running restart to pick it up; Kinetik Stats asks for administrator.", AutoSize = false, Width = I(484), Height = I(36), Left = I(18), Top = I(44), ForeColor = UI.Sub });

        int y = I(88);
        theme = CurrentText("ThemeName", "Midnight");
        var themeBtn = new FlatBtn(ThemeText(theme)) { Width = I(200), Height = I(28) };
        var themeBox = Row("Theme", "ThemeName", themeBtn, ref y, () => theme);
        themeBtn.Click += (s, e) =>
        {
            var picker = new ThemePicker(theme, n => { theme = n; themeBtn.Text = ThemeText(n); themeBtn.Invalidate(); themeBox.Checked = true; }) { Text = "Theme for every Kinetik app" };
            picker.ShowDialog(this);
        };
        Combo("Background opacity", "Opacity", CommonMenu.Opacities.Select(o => o + "%").ToArray(), Array.IndexOf(CommonMenu.Opacities, Current("Opacity", CommonMenu.DefaultOpacity)), i => CommonMenu.Opacities[i], ref y);
        Combo("Size", "Scale", CommonMenu.Scales.Select(o => o + "%").ToArray(), Array.IndexOf(CommonMenu.Scales, Current("Scale", 100)), i => CommonMenu.Scales[i], ref y);
        var fonts = new[] { "Default" }.Concat(CommonMenu.Fonts.Where(HasFont)).ToArray();
        var font = CurrentText("FontName", "");
        Combo("Font", "FontName", fonts, Math.Max(0, Array.IndexOf(fonts, font == "" ? "Default" : font)), i => i == 0 ? "" : fonts[i], ref y);
        Combo("Placement", "Layer", new[] { "On the desktop", "Normal window", "Always on top" }, Current("Layer", 0), i => i, ref y);
        Combo("Lock position", "Locked", new[] { "Unlocked", "Locked" }, Current("Locked", 0) != 0 ? 1 : 0, i => i == 1, ref y);
        Combo("Click-through when locked", "ClickThrough", new[] { "Off", "On" }, Current("ClickThrough", 0) != 0 ? 1 : 0, i => i == 1, ref y);
        Combo("Snap to screen edges", "Snap", new[] { "Off", "On" }, Current("Snap", 1) != 0 ? 1 : 0, i => i == 1, ref y);

        var apply = new FlatBtn("Apply to all apps") { Primary = true }; apply.Fit();
        var close = new FlatBtn("Close"); close.Fit();
        close.Left = ClientSize.Width - close.Width - I(18); close.Top = ClientSize.Height - close.Height - I(16);
        apply.Left = close.Left - apply.Width - I(8); apply.Top = close.Top;
        close.Click += (s, e) => Close();
        apply.Click += (s, e) =>
        {
            var v = new Dictionary<string, object>();
            foreach (var r in rows) if (r.Item2.Checked) v[r.Item1] = r.Item3();
            if (v.Count == 0) { MessageBox.Show(this, "Tick at least one setting to change.", "Kinetik Hub"); return; }
            Close();
            if (v.ContainsKey("ThemeName")) { owner.Hub.Cfg.ThemeName = (string)v["ThemeName"]; owner.Hub.Cfg.Save(); }
            owner.ApplySuite(v);
        };
        Controls.Add(apply); Controls.Add(close);
    }

    static bool HasFont(string name) { try { using (var f = new FontFamily(name)) return f.Name == name; } catch { return false; } }
    static string ThemeText(string t) { return t == "Auto" ? "Match Windows" : t == "App" ? "Match app theme" : t; }

    CheckBox Row(string label, string key, Control input, ref int y, Func<object> value)
    {
        var box = new CheckBox { Text = label, Left = I(18), Top = y + I(4), Width = I(250), ForeColor = UI.Text, BackColor = UI.Page };
        input.Left = I(290); input.Top = y;
        Controls.Add(box); Controls.Add(input);
        rows.Add(Tuple.Create(key, box, value));
        y += I(40);
        return box;
    }

    void Combo(string label, string key, string[] items, int selected, Func<int, object> value, ref int y)
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = I(200), BackColor = UI.Field, ForeColor = UI.Text, FlatStyle = FlatStyle.Flat };
        cb.Items.AddRange(items);
        cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
        var box = Row(label, key, cb, ref y, () => value(cb.SelectedIndex));
        cb.SelectedIndexChanged += (s, e) => box.Checked = true;
    }
}

// ======================================================================= Shared with the other Kinetik apps
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string Name { get { return Backup.AppTitle; } }
    public static string Args = ""; // added after the exe path, e.g. " --startup"

    public static bool IsOn
    {
        get
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null; }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"" + Args);
                else k.DeleteValue(Name, false);
        }
        catch { }
    }
}

// ======================================================================= Themes (same set as Kinetik and Kinetik Calendar)
class Theme
{
    public readonly string Name;
    public readonly Color Bg, Text, Sub, Accent;
    public readonly bool Light;

    public Theme(string name, Color bg, Color text, Color sub, Color accent, bool light)
    {
        Name = name; Bg = bg; Text = text; Sub = sub; Accent = accent; Light = light;
    }

    public Color Line { get { return Term ? Color.FromArgb(110, Sub) : Light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(28, 255, 255, 255); } }

    static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

    // The terminal themes (the same green, amber and yellow as Kinetik's dashboard and Kinetik Screen) draw in a
    // monospace font unless a font is picked, and their divider lines glow in the theme's colour.
    public bool Term { get { return IsTerm(Name); } }
    public static bool IsTerm(string name) { return name == "Terminal" || name == "Amber terminal" || name == "Yellow terminal"; }
    static string mono;
    public static string Mono
    {
        get
        {
            if (mono == null)
            {
                mono = "Consolas";
                try { using (var f = new Font("Cascadia Mono", 10f)) if (f.Name == "Cascadia Mono") mono = "Cascadia Mono"; } catch { }
            }
            return mono;
        }
    }

    public static readonly Theme[] All =
    {
        new Theme("Midnight", C(22, 22, 26), C(242, 242, 246), C(168, 168, 180), C(240, 88, 88), false),
        new Theme("Navy", C(22, 27, 40), C(236, 240, 248), C(138, 150, 176), C(77, 166, 255), false),
        new Theme("Graphite", C(40, 40, 44), C(236, 236, 236), C(160, 160, 166), C(255, 159, 10), false),
        new Theme("Nord", C(46, 52, 64), C(236, 239, 244), C(152, 162, 182), C(136, 192, 208), false),
        new Theme("Dracula", C(40, 42, 54), C(248, 248, 242), C(150, 156, 192), C(255, 121, 198), false),
        new Theme("Solarized", C(0, 43, 54), C(238, 232, 213), C(131, 148, 150), C(203, 75, 22), false),
        new Theme("Forest", C(22, 33, 27), C(228, 240, 231), C(138, 166, 148), C(108, 201, 124), false),
        new Theme("Ocean", C(12, 29, 45), C(228, 242, 250), C(128, 160, 186), C(56, 198, 232), false),
        new Theme("Sunset", C(40, 23, 35), C(250, 236, 240), C(192, 150, 172), C(255, 138, 92), false),
        new Theme("Mono", C(10, 10, 10), C(245, 245, 245), C(140, 140, 140), C(245, 245, 245), false),
        new Theme("OLED black", C(0, 0, 0), C(242, 242, 245), C(136, 136, 146), C(76, 194, 255), false),
        new Theme("Mocha", C(30, 30, 46), C(205, 214, 244), C(147, 153, 178), C(203, 166, 247), false),
        new Theme("Terminal", C(3, 10, 5), C(90, 255, 120), C(46, 170, 80), C(205, 255, 215), false),
        new Theme("Amber terminal", C(12, 7, 0), C(255, 182, 40), C(185, 118, 22), C(255, 232, 180), false),
        new Theme("Yellow terminal", C(0, 0, 0), C(255, 226, 0), C(200, 178, 0), C(255, 255, 190), false),
        new Theme("Light", C(246, 246, 248), C(24, 24, 28), C(96, 96, 106), C(230, 70, 70), true),
        new Theme("Paper", C(250, 246, 236), C(52, 42, 32), C(132, 116, 96), C(204, 92, 52), true),
        new Theme("Frost", C(236, 242, 250), C(20, 30, 48), C(92, 106, 132), C(40, 120, 230), true),
    };

    public static Theme Get(string name)
    {
        // "App" follows the theme and accent colour every Kinetik app shares.
        if (name == "App") { var l = KinetikSuite.Look.Current; return new Theme("App", l.Card, l.Text, l.Sub, l.Accent, l.Light); }
        if (name == "Auto")
        {
            bool light = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    light = k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 1;
            }
            catch { }
            return Get(light ? "Light" : "Midnight");
        }
        foreach (var t in All) if (t.Name == name) return t;
        return All[0];
    }
}

// ======================================================================= Drawing helpers
static class Draw
{
    public static string Family = HasFont("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    public static readonly string[] FontChoices = { "Segoe UI Variable Text", "Segoe UI", "Bahnschrift", "Calibri", "Aptos", "Inter", "Cascadia Code", "Consolas", "Verdana", "Georgia" };

    public static bool HasFont(string name)
    {
        try { using (var f = new FontFamily(name)) return f.Name == name; } catch { return false; }
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2);
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// A premultiplied-alpha bitmap backed by a DIB, pushed to a layered window with UpdateLayeredWindow (as in Kinetik).
sealed class DibSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER
    {
        public int Size, Width, Height; public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLEND b, int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    IntPtr memDC, dib, oldObj;
    Bitmap bmp;
    int w, h;

    public Bitmap Canvas(int width, int height)
    {
        if (dib != IntPtr.Zero && width == w && height == h) return bmp;
        Free();
        memDC = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFOHEADER { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        IntPtr bits;
        dib = CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
        oldObj = SelectObject(memDC, dib);
        bmp = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        w = width; h = height;
        return bmp;
    }

    public void Push(IntPtr hwnd, int x, int y)
    {
        var size = new SIZE { CX = w, CY = h };
        var src = new POINT();
        var pos = new POINT { X = x, Y = y };
        var blend = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
        UpdateLayeredWindow(hwnd, IntPtr.Zero, ref pos, ref size, memDC, ref src, 0, ref blend, 2);
    }

    void Free()
    {
        if (bmp != null) { bmp.Dispose(); bmp = null; }
        if (memDC != IntPtr.Zero) { SelectObject(memDC, oldObj); DeleteDC(memDC); memDC = IntPtr.Zero; }
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
    }

    // The last frame, and the same as a PNG (for --shot screenshots).
    public Bitmap Current { get { return bmp; } }
    public void Save(string path) { if (bmp != null) bmp.Save(path, ImageFormat.Png); }

    public void Dispose() { Free(); }
}

// A card's soft shadow: stacked, slightly larger translucent shapes. Those anti-aliased fills over the whole card cost
// more than the rest of a frame put together, so they're drawn once into a bitmap and copied while nothing about them changes.
sealed class ShadowCache : IDisposable
{
    Bitmap bmp; string key;

    // Lays the shadow into the (cleared) w × h target. look sums up everything draw depends on.
    public void Paint(Graphics target, int w, int h, string look, Action<Graphics> draw)
    {
        var k = w + "x" + h + "|" + look + "|" + (int)target.SmoothingMode + (int)target.PixelOffsetMode;
        if (k != key || bmp == null)
        {
            if (bmp == null || bmp.Width != w || bmp.Height != h) { if (bmp != null) bmp.Dispose(); bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb); }
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = target.SmoothingMode; g.PixelOffsetMode = target.PixelOffsetMode;
                draw(g);
            }
            key = k;
        }
        bmp.SetResolution(target.DpiX, target.DpiY); // so it's copied 1:1
        var mode = target.CompositingMode;
        target.CompositingMode = CompositingMode.SourceCopy;
        target.DrawImageUnscaled(bmp, 0, 0);
        target.CompositingMode = mode;
    }

    public void Dispose() { if (bmp != null) bmp.Dispose(); bmp = null; key = null; }
}

class DarkColors : ProfessionalColorTable
{
    static Color Bg { get { return KinetikSuite.Look.Current.Card; } }
    static Color Hi { get { return KinetikSuite.Look.Current.Hover; } }
    static Color Edge { get { return KinetikSuite.Look.Current.Border; } }
    public override Color ToolStripDropDownBackground { get { return Bg; } }
    public override Color ImageMarginGradientBegin { get { return Bg; } }
    public override Color ImageMarginGradientMiddle { get { return Bg; } }
    public override Color ImageMarginGradientEnd { get { return Bg; } }
    public override Color MenuBorder { get { return Edge; } }
    public override Color MenuItemBorder { get { return Hi; } }
    public override Color MenuItemSelected { get { return Hi; } }
    public override Color MenuItemSelectedGradientBegin { get { return Hi; } }
    public override Color MenuItemSelectedGradientEnd { get { return Hi; } }
    public override Color MenuItemPressedGradientBegin { get { return Hi; } }
    public override Color MenuItemPressedGradientEnd { get { return Hi; } }
    public override Color SeparatorDark { get { return Edge; } }
    public override Color SeparatorLight { get { return Edge; } }
    public override Color CheckBackground { get { return KinetikSuite.Look.Current.Hover; } }
    public override Color CheckSelectedBackground { get { return KinetikSuite.Look.Current.Hover; } }
    public override Color CheckPressedBackground { get { return KinetikSuite.Look.Current.Hover; } }
}

class DarkMenu : ToolStripProfessionalRenderer
{
    public DarkMenu() : base(new DarkColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? KinetikSuite.Look.Current.Text : KinetikSuite.Look.Current.Sub;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = KinetikSuite.Look.Current.Sub;
        base.OnRenderArrow(e);
    }
}

// A grid of theme tiles, the same picker as the other Kinetik apps.
class ThemePicker : Form
{
    public static Color Page { get { return KinetikSuite.Look.Current.Bg; } }
    public static Color Card { get { return KinetikSuite.Look.Current.Card; } }
    public static Color CardHi { get { return KinetikSuite.Look.Current.Hover; } }
    public static Color Accent { get { return KinetikSuite.Look.Current.Accent; } }
    public static Color TextCol { get { return KinetikSuite.Look.Current.Text; } }
    public static Color SubCol { get { return KinetikSuite.Look.Current.Sub; } }

    public ThemePicker(string current, Action<string> chosen)
    {

        HandleCreated += (s, e) => KinetikSuite.Look.Current.Window(Handle); // title bar in the Kinetik apps' theme
        Text = Backup.AppTitle + " theme";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; TopMost = true; ShowInTaskbar = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = Page;
        float k = DeviceDpi / 96f;
        var flow = new FlowLayoutPanel { Left = (int)(16 * k), Top = (int)(16 * k), Width = (int)(3 * 118 * k), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximumSize = new Size((int)(3 * 118 * k), 0), WrapContents = true, BackColor = Page };
        foreach (var name in new[] { "Auto", "App" }.Concat(Theme.All.Select(t => t.Name)))
        {
            var tile = new ThemeTile(name, name == current, k);
            tile.Click += (s, e) =>
            {
                foreach (ThemeTile o in flow.Controls) o.SetSelected(o == tile);
                chosen(tile.ThemeName);
            };
            flow.Controls.Add(tile);
        }
        Controls.Add(flow);
        ClientSize = new Size(flow.Right + (int)(8 * k), flow.Bottom + (int)(8 * k));
    }
}

class ThemeTile : Control
{
    public readonly string ThemeName;
    bool selected, hot;
    float sel;
    readonly float k;
    readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 15 };
    static readonly Color[] Bars = { Color.FromArgb(77, 166, 255), Color.FromArgb(56, 198, 180), Color.FromArgb(240, 88, 88) };

    public ThemeTile(string name, bool isSelected, float scale)
    {
        ThemeName = name; selected = isSelected; sel = isSelected ? 1 : 0; k = scale;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(I(110), I(84));
        Margin = new Padding(0, 0, I(8), I(8));
        Cursor = Cursors.Hand;
        BackColor = ThemePicker.Page;
        t.Tick += (s, e) =>
        {
            float target = selected ? 1 : 0;
            sel += (target - sel) * 0.3f;
            if (Math.Abs(target - sel) < 0.02f) { sel = target; t.Stop(); }
            Invalidate();
        };
    }

    int I(float v) { return (int)Math.Round(v * k); }
    public void SetSelected(bool v) { if (selected == v) return; selected = v; t.Start(); }
    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }

    static void Fill(Graphics g, RectangleF r, float rad, Color c) { using (var p = Draw.Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new RectangleF(1, 1, Width - 3, Height - 3);
        Fill(g, outer, I(10), hot ? ThemePicker.CardHi : ThemePicker.Card);
        if (sel > 0) using (var p = Draw.Round(outer, I(10))) using (var pen = new Pen(Color.FromArgb((int)(255 * sel), ThemePicker.Accent), 2)) g.DrawPath(pen, p);
        var inner = new RectangleF(I(8), I(8), Width - I(16), Height - I(34));
        if (ThemeName == "Auto")
        {
            Mini(g, inner, Theme.Get("Midnight"));
            var st = g.Save();
            g.SetClip(new RectangleF(inner.X + inner.Width / 2, inner.Y, inner.Width, inner.Height));
            Mini(g, inner, Theme.Get("Light"));
            g.Restore(st);
        }
        else Mini(g, inner, Theme.Get(ThemeName));
        using (var f = new Font(Draw.Family, 9f))
            TextRenderer.DrawText(g, ThemeName == "Auto" ? "Match Windows" : ThemeName == "App" ? "Match app theme" : ThemeName, f, new Rectangle(0, Height - I(26), Width, I(20)), selected ? ThemePicker.TextCol : ThemePicker.SubCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    void Mini(Graphics g, RectangleF r, Theme th)
    {
        Fill(g, r, I(6), th.Bg);
        float x = r.X + I(7), w = r.Width - I(14), y = r.Y + I(7);
        using (var b = new SolidBrush(th.Accent)) g.FillEllipse(b, x, y, I(7), I(7));
        Fill(g, new RectangleF(x + I(11), y + I(1), w * 0.45f, I(4)), I(2), th.Text);
        for (int i = 0; i < 3; i++)
        {
            float yy = y + I(13) + i * I(9);
            Fill(g, new RectangleF(x, yy, I(3), I(6)), 1.5f * k, Bars[i]);
            Fill(g, new RectangleF(x + I(7), yy + I(1), w * (0.62f - i * 0.12f), I(4)), I(2), Color.FromArgb(150, th.Sub));
            Fill(g, new RectangleF(x + w - I(14), yy + I(1), I(14), I(4)), I(2), Color.FromArgb(200, th.Text));
        }
    }
}

// ======================================================================= Settings backup
// Copies the app's settings (HKCU\Software\<KeyName>) to "<KeyName>-settings.reg" next to the exe, so they survive a
// Windows reinstall when the app folder is kept (on OneDrive, a USB stick…). It's a normal .reg export, so
// double-clicking the file restores the settings as well. A restore runs after the app has closed, so nothing the app
// saves on the way out can overwrite it, and then the app starts again with the restored settings.
// Same code in every Kinetik app.
static class Backup
{
    public static string KeyName, AppTitle;
    public static Action BeforeExit; // e.g. hide the tray icon, as the app's own Exit does
    static bool restoreAfterExit;

    public static string FilePath
    {
        get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), KeyName + "-settings.reg"); }
    }
    public static bool Exists { get { return System.IO.File.Exists(FilePath); } }
    public static string SavedText
    {
        get { return Exists ? System.IO.File.GetLastWriteTime(FilePath).ToString("d MMM yyyy, h:mm tt") : "never"; }
    }

    static bool HasSettings
    {
        get
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\" + KeyName)) return k != null; }
            catch { return true; }
        }
    }

    static bool Reg(string args) { return KinetikSuite.RegistryFiles.Run(args); }

    static bool FileIsOurs()
    {
        try
        {
            string own = "[HKEY_CURRENT_USER\\Software\\" + KeyName;
            bool any = false;
            foreach (var line in System.IO.File.ReadAllLines(FilePath))
            {
                var l = line.Trim();
                if (!l.StartsWith("[")) continue;
                if (!l.StartsWith(own + "]", StringComparison.OrdinalIgnoreCase) && !l.StartsWith(own + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                any = true;
            }
            return any;
        }
        catch { return false; }
    }

    static bool Import()
    {
        try { return FileIsOurs() && KinetikSuite.RegistryFiles.Replace(FilePath, new[] { KeyName }); }
        catch (Exception ex) { Say(ex.Message, MessageBoxIcon.Warning); return false; }
    }

    static void Say(string text, MessageBoxIcon icon) { MessageBox.Show(text, AppTitle, MessageBoxButtons.OK, icon); }

    // At launch: no settings in the registry (a fresh Windows) but a backup beside the exe, so offer it.
    public static void OfferRestore()
    {
        if (HasSettings || !Exists) return;
        if (MessageBox.Show("There are saved " + AppTitle + " settings in this folder, from " + SavedText + ".\n\nRestore them?",
                AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!Import()) Say("Couldn't restore the settings from\n" + FilePath, MessageBoxIcon.Warning);
    }

    public static void SaveNow()
    {
        if (Reg("export \"HKCU\\Software\\" + KeyName + "\" \"" + FilePath + "\" /y"))
            Say("Settings backed up to\n" + FilePath + "\n\nKeep this file with the app. After reinstalling Windows, " + AppTitle + " offers to restore it the first time it starts.", MessageBoxIcon.Information);
        else
            Say("Couldn't save the backup to\n" + FilePath + "\n\nCheck that the folder isn't read-only.", MessageBoxIcon.Warning);
    }

    public static void RestoreNow()
    {
        if (!Exists) { Say("There's no backup yet. Use \"Back up settings\" first.\n\nIt would be saved as\n" + FilePath, MessageBoxIcon.Information); return; }
        if (!FileIsOurs()) { Say("The backup file isn't a " + AppTitle + " settings file, so it wasn't used.", MessageBoxIcon.Warning); return; }
        if (MessageBox.Show("Replace the current settings with the backup from " + SavedText + "?\n\n" + AppTitle + " will restart.",
                AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        restoreAfterExit = true;
        if (BeforeExit != null) try { BeforeExit(); } catch { }
        Application.Exit();
    }

    // Called from Main once the app has closed and released its single-instance lock.
    public static void AfterExit()
    {
        if (!restoreAfterExit) return;
        if (!Import()) Say("Couldn't restore the settings from\n" + FilePath, MessageBoxIcon.Warning);
        try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
    }
}

// ======================================================================= The right-click menu every Kinetik widget shares
// The look, placement, startup and backup options, in the same order and the same words in every Kinetik app, so they
// feel like one set. Each app adds its own items above. Same code in every Kinetik app.
#pragma warning disable 0649 // the hooks an app has no use for stay null
class CommonMenu
{
    public static readonly int[] Scales = { 80, 90, 100, 115, 125, 150 };
    public static readonly int[] Opacities = { 100, 95, 80, 65, 50, 30, 0 };
    public const int DefaultOpacity = 95;
    public static readonly string[] Fonts = { "Segoe UI Variable Text", "Segoe UI", "Bahnschrift", "Calibri", "Aptos", "Inter", "Cascadia Code", "Consolas", "Verdana", "Georgia" };

    // Settings saved before the shared default changed from 92% to 95% move with it; any other choice is kept.
    public static int Migrate(int opacity) { return opacity == 92 ? DefaultOpacity : opacity; }

    public string Title, Version;
    public Action OpenTheme;                                        // opens the theme picker (or the page with it)
    public Action LookChanged;                                      // optional: after the App theme changes
    public Func<int> Scale; public Action<int> SetScale;
    public int[] Widths; public Func<int> CardWidth; public Action<int> SetCardWidth; // optional: Narrow, Normal, Wide
    public Func<string> FontName; public Action<string> SetFontName;   // optional; "" = the default font
    public Func<int> Opacity; public Action<int> SetOpacity;
    public Func<int> Layer; public Action<int> SetLayer;
    public Func<bool> Locked; public Action<bool> SetLocked;
    public Func<bool> ClickThrough; public Action<bool> SetClickThrough; // optional
    public Func<bool> Snap; public Action<bool> SetSnap;
    public string LockText = "Lock position";
    public Action OpenSettings;                                     // optional: the app's Settings window
    public Func<bool> RunAtStartup; public Action<bool> SetRunAtStartup;   // optional
    public Action BackUp, Restore; public Func<string> LastBackup; // optional
    public Action Exit;                                         // optional

    static ToolStripMenuItem Item(string text, bool check, Action click)
    {
        var it = new ToolStripMenuItem(text) { Checked = check };
        it.Click += (s, e) => click();
        return it;
    }

    static bool HasFont(string name)
    {
        try { using (var f = new FontFamily(name)) return f.Name == name; } catch { return false; }
    }

    // Appearance and placement: Theme, Size, Font, Background opacity, Placement.
    public void AddLook(ToolStripItemCollection items)
    {
        if (OpenTheme != null) items.Add(Item("Widget theme…", false, OpenTheme));
        items.Add(KinetikSuite.Look.MenuItem(LookChanged)); // the theme of every Kinetik app's windows and menus

        var size = new ToolStripMenuItem("Size");
        foreach (var s in Scales) { int v = s; size.DropDownItems.Add(Item(v + "%", Scale() == v, () => SetScale(v))); }
        if (Widths != null)
        {
            size.DropDownItems.Add(new ToolStripSeparator());
            string[] names = { "Narrow", "Normal", "Wide" };
            for (int i = 0; i < Widths.Length && i < 3; i++) { int v = Widths[i]; size.DropDownItems.Add(Item(names[i], CardWidth() == v, () => SetCardWidth(v))); }
        }
        items.Add(size);

        if (FontName != null)
        {
            var font = new ToolStripMenuItem("Font");
            font.DropDownItems.Add(Item("Default", FontName() == "", () => SetFontName("")));
            foreach (var n in Fonts.Where(HasFont)) { string v = n; font.DropDownItems.Add(Item(v, FontName() == v, () => SetFontName(v))); }
            if (FontName() != "" && !Fonts.Contains(FontName())) font.DropDownItems.Add(Item(FontName(), true, () => { }));
            items.Add(font);
        }

        var opacity = new ToolStripMenuItem("Background opacity");
        foreach (var o in Opacities) { int v = o; opacity.DropDownItems.Add(Item(v + "%", Opacity() == v, () => SetOpacity(v))); }
        if (!Opacities.Contains(Opacity())) opacity.DropDownItems.Add(Item(Opacity() + "%", true, () => { }));
        items.Add(opacity);

        var place = new ToolStripMenuItem("Placement");
        string[] layers = { "On the desktop", "Normal window", "Always on top" };
        for (int i = 0; i < 3; i++) { int v = i; place.DropDownItems.Add(Item(layers[i], Layer() == v, () => SetLayer(v))); }
        place.DropDownItems.Add(new ToolStripSeparator());
        place.DropDownItems.Add(Item(LockText, Locked(), () => SetLocked(!Locked())));
        if (ClickThrough != null) place.DropDownItems.Add(Item("Click-through when locked", ClickThrough(), () => SetClickThrough(!ClickThrough())));
        place.DropDownItems.Add(Item("Snap to screen edges", Snap(), () => SetSnap(!Snap())));
        items.Add(place);
    }

    // The rest: Settings…, Run at startup, Backup, then the version and Exit.
    public void AddEnd(ToolStripItemCollection items)
    {
        items.Add(new ToolStripSeparator());
        if (OpenSettings != null) items.Add(Item("Settings…", false, OpenSettings));
        if (RunAtStartup != null) items.Add(Item("Run at startup", RunAtStartup(), () => SetRunAtStartup(!RunAtStartup())));
        if (BackUp != null)
        {
            var backup = new ToolStripMenuItem("Backup");
            backup.DropDownItems.Add(Item("Back up settings", false, BackUp));
            backup.DropDownItems.Add(Item("Restore settings…", false, Restore));
            backup.DropDownItems.Add(new ToolStripMenuItem("Last backup: " + LastBackup()) { Enabled = false });
            items.Add(backup);
        }
        items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem(Title + " " + Version) { Enabled = false });
        if (Exit != null) items.Add(Item("Exit", false, Exit));
    }

    // The same options as the shared pages of a settings window (Look, Placement, General, About).
    public KinetikSuite.SettingsWindow.Common ForSettings()
    {
        return new KinetikSuite.SettingsWindow.Common
        {
            Title = Title, Version = Version, OpenTheme = OpenTheme,
            Scale = Scale, SetScale = SetScale, Scales = Scales, Widths = Widths, CardWidth = CardWidth, SetCardWidth = SetCardWidth,
            FontName = FontName, SetFontName = SetFontName, Fonts = Fonts.Where(HasFont).ToArray(),
            Opacity = Opacity, SetOpacity = SetOpacity, Opacities = Opacities,
            Layer = Layer, SetLayer = SetLayer, Locked = Locked, SetLocked = SetLocked, LockText = LockText,
            ClickThrough = ClickThrough, SetClickThrough = SetClickThrough, Snap = Snap, SetSnap = SetSnap,
            RunAtStartup = RunAtStartup, SetRunAtStartup = SetRunAtStartup, BackUp = BackUp, Restore = Restore, LastBackup = LastBackup,
        };
    }

    public void AddAll(ToolStripItemCollection items) { AddLook(items); AddEnd(items); }
}
#pragma warning restore 0649

// ======================================================================= Updates window
// Every app (and Hub itself) against its latest GitHub release: install what isn't here, update what's behind, pick
// another version, or skip one. Each download is checked against the release's SHA-256 before anything is replaced, and
// the version it replaces is kept in Old Versions.
class UpdatesForm : Form
{
    readonly HubContext hub;
    readonly Settings c;
    readonly float k;
    readonly Panel list = new Panel { AutoScroll = true };
    readonly List<Tuple<AppDef, Label, Label, FlatBtn, FlatBtn, FlatBtn>> rows = new List<Tuple<AppDef, Label, Label, FlatBtn, FlatBtn, FlatBtn>>();
    readonly FlatBtn checkNow, updateAll;
    readonly Label foot = new Label { AutoSize = false };
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 1000 };

    int I(float v) { return (int)Math.Round(v * k); }

    public UpdatesForm(HubContext hub)
    {

        HandleCreated += (s, e) => KinetikSuite.Look.Current.Window(Handle); // title bar in the Kinetik apps' theme
        this.hub = hub; c = hub.Cfg;
        k = DeviceDpi / 96f;
        Text = "Kinetik Hub · Updates";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = UI.Page; ForeColor = UI.Text;
        Font = new Font(Draw.Family, 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label { Text = "Updates", Font = new Font(Draw.Family, 16f, FontStyle.Bold), AutoSize = true, Left = I(20), Top = I(16), ForeColor = UI.Text };
        var sub = new Label
        {
            Text = "From each app's releases on GitHub. Every download is checked against its SHA-256 before it's installed, and the version it replaces is kept in Old Versions.",
            AutoSize = false, Left = I(22), Top = I(50), Width = I(680), Height = I(36), ForeColor = UI.Sub
        };
        checkNow = new FlatBtn("Check now"); checkNow.Fit();
        updateAll = new FlatBtn("Update all") { Primary = true }; updateAll.Fit(); updateAll.Width += I(30);
        checkNow.Top = updateAll.Top = I(90);
        checkNow.Left = I(20); updateAll.Left = checkNow.Right + I(8);
        checkNow.Click += (s, e) => hub.MaybeCheckUpdates(true);
        updateAll.Click += (s, e) => UpdateAll();
        Controls.AddRange(new Control[] { title, sub, checkNow, updateAll });

        list.SetBounds(I(16), I(130), I(696), I(10));
        list.BackColor = UI.Page;
        int y = 0;
        foreach (var a in AppDef.WithSelf)
        {
            var app = a;
            var row = new Panel { Left = 0, Top = y, Width = I(676), Height = I(50), BackColor = UI.Card };
            var name = new Label { Text = a.Title, Font = new Font(Draw.Family, 10f, FontStyle.Bold), ForeColor = UI.Text, BackColor = UI.Card, AutoSize = false, Left = I(14), Top = I(6), Width = I(250), Height = I(20) };
            var installed = new Label { ForeColor = UI.Sub, BackColor = UI.Card, AutoSize = false, Left = I(14), Top = I(26), Width = I(150), Height = I(18), Font = new Font(Draw.Family, 8.5f) };
            var status = new Label { BackColor = UI.Card, AutoSize = false, Left = I(160), Top = I(26), Width = I(250), Height = I(18), Font = new Font(Draw.Family, 8.5f, FontStyle.Bold) };
            var act = new FlatBtn("Update") { Primary = true }; var versions = new FlatBtn("Versions…"); var skip = new FlatBtn("Skip");
            foreach (var b in new[] { act, versions, skip }) { b.Height = I(28); b.Top = I(11); }
            act.Width = I(76); versions.Width = I(84); skip.Width = I(60);
            skip.Left = row.Width - I(12) - skip.Width; versions.Left = skip.Left - I(6) - versions.Width; act.Left = versions.Left - I(6) - act.Width;
            act.Click += (s, e) => Act(app);
            versions.Click += (s, e) => VersionsForm.Open(this, hub, app);
            skip.Click += (s, e) => { var st = hub.StatusOf(app); hub.Skip(app, hub.SkippedTag(app) != "" && st.Release != null && hub.SkippedTag(app) == st.Release.Tag ? "" : st.Release != null ? st.Release.Tag : ""); };
            row.Controls.AddRange(new Control[] { name, installed, status, act, versions, skip });
            row.Paint += (s, e) => { e.Graphics.Clear(UI.Page); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var p = Draw.Round(new RectangleF(0, 0, row.Width - 1, row.Height - 1), I(9))) using (var b = new SolidBrush(UI.Card)) e.Graphics.FillPath(b, p); };
            list.Controls.Add(row);
            rows.Add(Tuple.Create(a, installed, status, act, versions, skip));
            y += row.Height + I(6);
        }
        list.Height = Math.Min(y, I(560));
        Controls.Add(list);

        var daily = new CheckBox { Text = "Check GitHub for updates once a day", Checked = c.CheckUpdates, ForeColor = UI.Text, AutoSize = true, Left = I(20), Top = list.Bottom + I(12) };
        daily.CheckedChanged += (s, e) => { c.CheckUpdates = daily.Checked; c.Save(); };
        var auto = new CheckBox { Text = "Install updates automatically (Kinetik Stats and Kinetik Hub still ask first)", Checked = c.AutoInstall, ForeColor = UI.Text, AutoSize = true, Left = I(20), Top = daily.Top + I(26) };
        auto.CheckedChanged += (s, e) => { c.AutoInstall = auto.Checked; c.Save(); };
        foot.SetBounds(I(20), auto.Top + I(30), I(690), I(22)); foot.ForeColor = UI.Sub;
        Controls.AddRange(new Control[] { daily, auto, foot });
        ClientSize = new Size(I(728), foot.Bottom + I(12));

        poll.Tick += (s, e) => RefreshFoot();
        poll.Start();
        RefreshRows();
    }

    protected override void Dispose(bool disposing) { if (disposing) poll.Dispose(); base.Dispose(disposing); }

    public void RefreshRows()
    {
        int pending = 0;
        foreach (var r in rows)
        {
            var a = r.Item1;
            var exe = hub.ExeOf(a);
            var st = hub.StatusOf(a);
            r.Item2.Text = exe == null ? "Not installed" : "Installed v" + Places.Short(Places.VersionOf(exe));
            r.Item3.Text = st.Text; r.Item3.ForeColor = st.Col;
            r.Item4.Visible = st.Action != null; r.Item4.Text = st.Action ?? "";
            r.Item5.Visible = a.Repo != null;
            bool behind = st.Action == "Update";
            r.Item6.Visible = behind;
            r.Item6.Text = behind && hub.SkippedTag(a) == st.Release.Tag ? "Unskip" : "Skip";
            if (hub.UpdateFor(a) != null && st.Action == "Update") pending++;
        }
        updateAll.Text = pending > 0 ? "Update all (" + pending + ")" : "Update all";
        updateAll.Enabled = pending > 0 && !Jobs.Busy;
        RefreshFoot();
    }

    void RefreshFoot()
    {
        checkNow.Enabled = !hub.CheckingUpdates && !Jobs.Busy;
        if (Jobs.Busy) { foot.Text = Jobs.Progress; return; }
        if (hub.CheckingUpdates) { foot.Text = "Checking GitHub…"; return; }
        DateTime last;
        foot.Text = DateTime.TryParse(c.LastCheck, null, DateTimeStyles.RoundtripKind, out last)
            ? "Last checked " + last.ToLocalTime().ToString("d MMM, h:mm tt") + "  ·  Kinetik Hub " + Program.Version
            : "Not checked yet  ·  Kinetik Hub " + Program.Version;
    }

    void Act(AppDef a)
    {
        var st = hub.StatusOf(a);
        if (st.Release == null || st.Action == null) return;
        string question = st.Action == "Install"
            ? "Download " + a.Title + " " + st.Release.Tag + " from GitHub and install it in\n" + Path.GetDirectoryName(Places.InstallTarget(a)) + "?"
            : a == AppDef.Self ? "Update Kinetik Hub to " + st.Release.Tag + "? It restarts, and this version is kept in Old Versions."
            : "Update " + a.Title + " to " + st.Release.Tag + "?\n\nThe current one is kept in Old Versions" + (Running.Is(a) ? ", and " + a.Title + " restarts" : "") + "." + (a.Admin ? "\n\nIt runs as administrator, so Windows asks first." : "");
        if (MessageBox.Show(question, "Kinetik Hub", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        hub.InstallUpdates(new List<AppDef> { a }, false, st.Release);
    }

    void UpdateAll()
    {
        var apps = AppDef.WithSelf.Where(a => hub.UpdateFor(a) != null && hub.UpdateFor(a).Zip != null).ToList();
        if (apps.Count == 0) return;
        var names = apps.Select(a => a.Title + " " + hub.UpdateFor(a).Tag);
        if (MessageBox.Show("Install " + string.Join(", ", names) + "?\n\nRunning apps restart, and each replaced version is kept in Old Versions." +
                (apps.Any(a => a.Admin) ? " Kinetik Stats runs as administrator, so Windows asks for it." : "") + (apps.Contains(AppDef.Self) ? " Kinetik Hub updates last and restarts." : ""),
                "Kinetik Hub", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        hub.InstallUpdates(apps, false);
    }
}

// Every release of one app on GitHub with its notes, to install any of them (a newer one, or an older one to go back).
class VersionsForm : Form
{
    readonly HubContext hub;
    readonly AppDef a;
    readonly ListBox list = new ListBox { IntegralHeight = false, BorderStyle = BorderStyle.None };
    readonly TextBox notes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
    readonly FlatBtn install, page;
    readonly Label status = new Label { AutoSize = false };
    List<Release> releases = new List<Release>();

    public static void Open(IWin32Window owner, HubContext hub, AppDef a)
    {
        var f = new VersionsForm(hub, a);
        f.Show(owner);
    }

    VersionsForm(HubContext hub, AppDef a)
    {
        this.hub = hub; this.a = a;
        float k = DeviceDpi / 96f;
        Func<float, int> I = v => (int)Math.Round(v * k);
        Text = a.Title + " · versions on GitHub";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = UI.Page; ForeColor = UI.Text; Font = new Font(Draw.Family, 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(I(640), I(430));

        list.SetBounds(I(16), I(16), I(200), I(350)); list.BackColor = UI.Card; list.ForeColor = UI.Text;
        notes.SetBounds(I(228), I(16), I(396), I(350)); notes.BackColor = UI.Card; notes.ForeColor = UI.Text;
        install = new FlatBtn("Install this version") { Primary = true }; install.Fit();
        page = new FlatBtn("Open on GitHub"); page.Fit();
        install.Top = page.Top = I(380);
        page.Left = ClientSize.Width - I(16) - page.Width; install.Left = page.Left - I(8) - install.Width;
        status.SetBounds(I(16), I(384), install.Left - I(24), I(22)); status.ForeColor = UI.Sub;
        Controls.AddRange(new Control[] { list, notes, install, page, status });
        install.Enabled = false;

        list.SelectedIndexChanged += (s, e) => ShowRelease();
        install.Click += (s, e) => Install();
        page.Click += (s, e) => { var r = Selected; try { Process.Start(r != null && !string.IsNullOrEmpty(r.Page) ? r.Page : "https://github.com/" + a.Repo + "/releases"); } catch { } };

        status.Text = "Getting the releases from GitHub…";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            List<Release> got = null; string err = null;
            try { got = Updates.List(a, 20); } catch (Exception ex) { err = ex.Message; }
            try { BeginInvoke((Action)(() => Loaded(got, err))); } catch { }
        });
    }

    Release Selected { get { int i = list.SelectedIndex; return i >= 0 && i < releases.Count ? releases[i] : null; } }

    void Loaded(List<Release> got, string err)
    {
        if (IsDisposed) return;
        if (err != null) { status.Text = "Couldn't reach GitHub: " + err; return; }
        releases = got;
        var exe = hub.ExeOf(a);
        var v = exe != null ? Places.VersionOf(exe) : null;
        foreach (var r in releases)
            list.Items.Add(r.Tag + (r.Published != DateTime.MinValue ? "   " + r.Published.ToLocalTime().ToString("d MMM yyyy") : "") + (v != null && r.V == v ? "   (installed)" : ""));
        status.Text = releases.Count == 0 ? "No releases on GitHub yet." : exe == null ? "Not installed on this PC." : "Installed: v" + Places.Short(v);
        if (releases.Count > 0) list.SelectedIndex = 0;
    }

    void ShowRelease()
    {
        var r = Selected;
        if (r == null) { notes.Text = ""; install.Enabled = false; return; }
        notes.Text = (r.Notes == "" ? "(No release notes.)" : r.Notes).Replace("\n", "\r\n");
        var exe = hub.ExeOf(a);
        var v = exe != null ? Places.VersionOf(exe) : null;
        bool verified = r.Zip != null && (r.ZipSha != null || r.ExeSha != null);
        install.Enabled = verified && !(v != null && r.V == v);
        install.Text = v == null ? "Install this version" : r.V > v ? "Update to " + r.Tag : r.V < v ? "Go back to " + r.Tag : "Installed";
        install.Fit(); install.Left = page.Left - (int)(8 * DeviceDpi / 96f) - install.Width;
        if (!verified) status.Text = "This release has no verified download, so Hub won't install it.";
    }

    void Install()
    {
        var r = Selected;
        if (r == null) return;
        var exe = hub.ExeOf(a);
        var v = exe != null ? Places.VersionOf(exe) : null;
        string what = v == null ? "Install " + a.Title + " " + r.Tag + "?" : r.V < v
            ? "Go back to " + a.Title + " " + r.Tag + "? Your current v" + Places.Short(v) + " is kept in Old Versions.\n\nIt will be offered again as an update unless you skip it."
            : "Update " + a.Title + " to " + r.Tag + "? The current one is kept in Old Versions.";
        if (a == AppDef.Self) what += "\n\nKinetik Hub restarts.";
        if (MessageBox.Show(what, "Kinetik Hub", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        hub.InstallUpdates(new List<AppDef> { a }, false, r);
        Close();
    }
}
