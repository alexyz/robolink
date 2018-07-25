using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace com.github.alexyz.robolink {
    
    public class Program {
        static List<string> SOURCES = new List<string>();
        static bool RECURSE, PURGE, FLATTEN, COMMIT, QUIET;
        static List<RS> INCLUDE_FILES = new List<RS>(), INCLUDE_DIRS = new List<RS>(), EXCLUDE_FILES = new List<RS>(), EXCLUDE_DIRS = new List<RS>();
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
            //Console.WriteLine("create");
            foreach (string sp in SOURCE_MAP.Keys) {
                string dp = SOURCE_MAP[sp];
                string parent = Path.GetDirectoryName(dp);
                if (!Directory.Exists(parent)) {
                    //Console.WriteLine("  mkdir " + parent);
                    if (COMMIT) {
                        Directory.CreateDirectory(parent);
                    }
                }
                if (!File.Exists(dp)) {
                    //Console.WriteLine("  mklink " + sp + " => " + dp);
                    if (COMMIT && !CreateHardLink(dp, sp, IntPtr.Zero)) {
                        throw new Exception("could not create hardlink " + sp + " => " + dp);
                    }
                }
            }
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink (string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        static void Purge (string destdir) {
            //Console.WriteLine("purge {0}", destdir);
            if (RECURSE) {
                foreach (string dp in Directory.EnumerateDirectories(destdir)) {
                    //Console.WriteLine("  purge dir dp={0}", dp);
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
                //Console.WriteLine("  purge file dp={0}", dp);
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
                        //Console.WriteLine("delete " + dp);
                        if (COMMIT) {
                            File.Delete(dp);
                        }
                    }
                }
            }
        }

        static void Find (string sourcedir, string subdir) {
            //Console.WriteLine("find sourcedir={0} subdir={1}", sourcedir, subdir);

            if (RECURSE) {
                foreach (string sp in Directory.EnumerateDirectories(sourcedir + subdir)) {
                    //Console.WriteLine("  find dir={0}", sp);
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
                //Console.WriteLine("  sp={0}", sp, dp);
                //Console.WriteLine("  dp={0}", dp);

                if (Matches(INCLUDE_FILES, spnamelower, true) && !Matches(EXCLUDE_FILES, spnamelower, false)) {
                    if (DEST_MAP.ContainsKey(dplower)) {
                        string exdp = DEST_MAP[dplower];
                        if (FileEqual(exdp, sp)) {
                            Console.WriteLine("multiple destination mappings for {0} (files equal)", dp);
                        } else if (QUIET) {
                            Console.WriteLine("ignoring multiple destination mappings for {0} (files unequal)", dp);
                        } else {
                            throw new Exception(String.Format("multiple destination mappings for {0} (first is {1} second is {2})", dp, exdp, sp));
                        }
                    } else {
                        SOURCE_MAP[splower] = dp;
                        DEST_MAP[dplower] = sp;
                    }
                }
            }
        }

        private static bool FileEqual (string f1, string f2) {
            FileInfo i1 = new FileInfo(f1);
            FileInfo i2 = new FileInfo(f2);
            if (i1.Length != i2.Length) {
                //Console.WriteLine("different length");
                return false;
            } else {
                byte[] a1 = new byte[4096];
                byte[] a2 = new byte[4096];
                using (FileStream s1 = File.OpenRead(f1)) {
                    using (FileStream s2 = File.OpenRead(f2)) {
                        while (true) {
                            int b1 = s1.Read(a1, 0, a1.Length);
                            int b2 = s2.Read(a2, 0, a2.Length);
                            if (b1 == 0 && b2 == 0) {
                                // end of file
                                break;
                            } else if (b1 != b2 || !ArrayEqual(a1, a2)) {
                                return false;
                            }
                        }
                    }
                }
                return true;
            }
        }

        private static bool ArrayEqual (byte[] a1, byte[] a2) {
            for (int n = 0; n < a1.Length; n++) {
                if (a1[n] != a2[n]) {
                    return false;
                }
            }
            return true;
        }

        private static bool Matches (List<RS> list, string name, bool matchempty) {
            if (list.Count == 0) {
                return matchempty;
            } else {
                foreach (RS r in list) {
                    if (r.regex.IsMatch(name)) {
                        return true;
                    }
                }
                return false;
            }
        }

        static void GetArgs (string[] args) {
            for (int n = 0; n < args.Length; n++) {
                string a = args[n].ToUpper();
                List<RS> l = null;
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
                        l.Add(new RS(args[++n].ToLower()));
                    } else {
                        throw new Exception("XF/XD/IF/ID without arg");
                    }
                }
            }
            if (SOURCES.Count < 2) {
                throw new Exception("require at least one source and one dest");
            }
            DEST = SOURCES[SOURCES.Count - 1];
            SOURCES.RemoveAt(SOURCES.Count - 1);
            foreach (string p in SOURCES) {
                if (!Directory.Exists(p)) {
                    throw new Exception("source or destination doesn't exist: " + p);
                }
            }
            Console.WriteLine("recurse = {0} purge = {1} flatten = {2} commit = {3} quiet = {4}", RECURSE, PURGE, FLATTEN, COMMIT, QUIET);
            foreach (string dir in SOURCES) {
                Console.WriteLine("source = {0}", dir);
            }
            Console.WriteLine("dest = {0}", DEST);
            foreach (RS r in INCLUDE_FILES) {
                Console.WriteLine("include file = {0}", r.str);
            }
            foreach (RS r in INCLUDE_DIRS) {
                Console.WriteLine("include dir = {0}", r.str);
            }
            foreach (RS r in EXCLUDE_FILES) {
                Console.WriteLine("exclude file = {0}", r.str);
            }
            foreach (RS r in EXCLUDE_DIRS) {
                Console.WriteLine("exclude dir = {0}", r.str);
            }
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

        public static Regex ToRegex (string p) {
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

    public class RS {
        public readonly Regex regex;
        public readonly string str;
        public RS (string str) {
            this.regex = Program.ToRegex(str);
            this.str = str;
        }
    }
}
