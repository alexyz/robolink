using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace com.github.alexyz.robolink {

    class Program {
        static List<string> SOURCES = new List<string>();
        static bool RECURSE, PURGE, FLATTEN, COMMIT, QUIET;
        static List<Regex> INCLUDE_FILES = new List<Regex>(), INCLUDE_DIRS = new List<Regex>(), EXCLUDE_FILES = new List<Regex>(), EXCLUDE_DIRS = new List<Regex>();
        static string DEST;
        static Dictionary<string, string> SOURCE_MAP = new Dictionary<string, string>();
        static Dictionary<string, string> DEST_MAP = new Dictionary<string, string>();

        static void Main (string[] args) {
            // source1\* -> dest\*
            try {
                GetArgs(args);
                foreach (string s in SOURCES) {
                    Find(s, "\\");
                }
                if (PURGE) {
                    // recurse dest
                    // if dest file is older or missing, remove
                    Purge(DEST);
                }
                Create();
            } catch (Exception e) {
                Console.WriteLine("exception: " + e.ToString());
                Environment.Exit(1);
            }
        }

        static void Create () {
            Console.WriteLine("create");
            foreach (string sp in SOURCE_MAP.Keys) {
                string dp = SOURCE_MAP[sp];
                string parent = Path.GetDirectoryName(dp);
                if (!Directory.Exists(parent)) {
                    Console.WriteLine("  mkdir " + parent);
                    if (COMMIT) {
                        Directory.CreateDirectory(parent);
                    }
                }
                if (!File.Exists(dp)) {
                    Console.WriteLine("  mklink " + sp + " => " + dp);
                    if (COMMIT && !CreateHardLink(dp, sp, IntPtr.Zero)) {
                        throw new Exception("could not create hardlink " + sp + " => " + dp);
                    }
                }
            }
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink (string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        static void Purge (string destdir) {
            Console.WriteLine("purge {0}", destdir);
            if (RECURSE) {
                foreach (string dp in Directory.EnumerateDirectories(destdir)) {
                    Console.WriteLine("  purge dir dp={0}", dp);
                    string dpname = Path.GetFileName(dp);
                    string dpnamelower = dpname.ToLower();
                    if (Matches(INCLUDE_DIRS, dpnamelower, true) && !Matches(EXCLUDE_DIRS, dpnamelower, false)) {
                        Purge(dp);
                    }
                }
            }
            foreach (string dp in Directory.EnumerateFiles(destdir)) {
                string dplower = dp.ToLower();
                string dpname = Path.GetFileName(dp);
                string dpnamelower = dpname.ToLower();
                Console.WriteLine("  purge file dp={0}", dp);
                if (Matches(INCLUDE_FILES, dpnamelower, true) && !Matches(EXCLUDE_FILES, dpnamelower, false)) {
                    bool del = false;
                    if (DEST_MAP.TryGetValue(dplower, out string sp)) {
                        DateTime dt = File.GetLastWriteTime(dp);
                        DateTime st = File.GetLastWriteTime(sp);
                        if (st > dt) {
                            del = true;
                        }
                    } else {
                        del = true;
                    }
                    if (del) {
                        Console.WriteLine("delete " + dp);
                        if (COMMIT) {
                            File.Delete(dp);
                        }
                    }
                }
            }
        }

        static void Find (string sourcedir, string subdir) {
            Console.WriteLine("find sourcedir={0} subdir={1}", sourcedir, subdir);
            if (RECURSE) {
                foreach (string sp in Directory.EnumerateDirectories(sourcedir + subdir)) {
                    Console.WriteLine("  find dir={0}", sp);
                    string spname = Path.GetFileName(sp);
                    string spnamelower = spname.ToLower();
                    if (Matches(INCLUDE_DIRS, spnamelower, true) && !Matches(EXCLUDE_DIRS, spnamelower, false)) {
                        Find(sourcedir, subdir + spname + "\\");
                    }
                }
            }
            foreach (string sp in Directory.EnumerateFiles(sourcedir + subdir)) {
                string splower = sp.ToLower();
                string spname = Path.GetFileName(sp);
                string spnamelower = spname.ToLower();
                string dp = DEST + (FLATTEN ? "\\" : subdir) + spname;
                string dplower = dp.ToLower();
                Console.WriteLine("  find file sp={0} dp={1}", sp, dp);
                if (Matches(INCLUDE_FILES, spnamelower, true) && !Matches(EXCLUDE_FILES, spnamelower, false)) {
                    if (!QUIET && DEST_MAP.ContainsKey(dplower)) {
                        throw new Exception("multiple destination mappings for " + dp + " (first is " + DEST_MAP[dp] + " second is " + sp + ")");
                    }
                    SOURCE_MAP[splower] = dp;
                    DEST_MAP[dplower] = sp;
                }
            }
        }


        static bool Matches (List<Regex> list, string name, bool matchempty) {
            if (list.Count == 0) {
                return matchempty;
            } else {
                foreach (Regex r in list) {
                    if (r.IsMatch(name)) {
                        return true;
                    }
                }
                return false;
            }
        }

        static void GetArgs (string[] args) {
            for (int n = 0; n < args.Length; n++) {
                string a = args[n].ToUpper();
                List<Regex> l = null;
                switch (a) {
                    case "/R": RECURSE = true; break;
                    case "/P": PURGE = true; break;
                    case "/F": FLATTEN = true; break;
                    case "/C": COMMIT = true; break;
                    case "/Q": QUIET = true; break;
                    case "/XF": l = EXCLUDE_FILES; break;
                    case "/XD": l = EXCLUDE_DIRS; break;
                    case "/IF": l = INCLUDE_FILES; break;
                    case "/ID": l = INCLUDE_DIRS; break;
                    case "/?": Usage(); break;
                    default:
                        if (a.StartsWith("/")) {
                            throw new Exception("unrecognised command " + a);
                        } else {
                            SOURCES.Add(args[n]);
                        }
                        break;
                }
                if (l != null) {
                    if (n < args.Length + 1 && !args[n + 1].StartsWith("/")) {
                        l.Add(ToRegex(args[++n].ToLower()));
                    } else {
                        throw new Exception("XF/XD/IF/ID without arg");
                    }
                }
            }
            if (SOURCES.Count < 2) {
                throw new Exception("require at least one source and one dest");
            }
            foreach (string p in SOURCES) {
                if (!Directory.Exists(p)) {
                    throw new Exception("source or destination doesn't exist: " + p);
                }
            }
            DEST = SOURCES[SOURCES.Count - 1];
            SOURCES.RemoveAt(SOURCES.Count - 1);

            Console.WriteLine("recurse={0} purge={1} flatten={2} commit={3} quiet={4}", RECURSE, PURGE, FLATTEN, COMMIT, QUIET);
            Console.WriteLine("if={0} id={1} xf={2} xd={3} sources={4} dest={5}", INCLUDE_FILES.Count, INCLUDE_DIRS.Count, EXCLUDE_FILES.Count, EXCLUDE_DIRS.Count, SOURCES.Count, DEST);

        }

        static void Usage () {
            Console.WriteLine("usage: robolink.exe source1 [source2] dest [opts]");
            Console.WriteLine("  /R - recurse into subdirectories of sources and destination");
            Console.WriteLine("  /P - purge files from destination missing or newer in sources");
            Console.WriteLine("  /F - flatten subdirectories of sources");
            Console.WriteLine("  /C - commit changes");
            Console.WriteLine("  /Q - ignore duplicate source files");
            Console.WriteLine("  /IF pattern - include files matching pattern");
            Console.WriteLine("  /ID pattern - include directories matching pattern");
            Console.WriteLine("  /XF pattern - exclude files matching pattern");
            Console.WriteLine("  /XD pattern - exclude directories matching pattern");
            Console.WriteLine("  /? - usage information and exit");
            Environment.Exit(0);
        }

        static Regex ToRegex (string p) {
            StringBuilder sb = new StringBuilder();
            for (int n = 0; n < p.Length; n++) {
                char c = p[n];
                if (c == '*') {
                    sb.Append(".*");
                } else if (c == '?') {
                    sb.Append(".{0,1}");
                } else {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            return new Regex(sb.ToString(), RegexOptions.Compiled);
        }
    }


}
