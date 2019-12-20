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
        private readonly List<string> sources = new List<string>();
        private readonly List<RS> includeFiles = new List<RS>();
        private readonly List<RS> includeDirs = new List<RS>();
        private readonly List<RS> excludeFiles = new List<RS>();
        private readonly List<RS> excludeDirs = new List<RS>();
        private readonly Dictionary<string, string> sourceMap = new Dictionary<string, string>();
        private readonly Dictionary<string, string> destMap = new Dictionary<string, string>();
        private bool recurse, purge, flatten, commit, quiet, verbose;
        private string destination;
        int created, deleted;

        static void Main (string[] args) {
            try {
                // source1\* -> dest\*
                Program p = new Program();
                p.Run(args);
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
                Environment.Exit(1);
            }
        }

        private void Run (string[] args) {
            GetArgs(args);
            foreach (string s in sources) {
                Find(s, "\\");
            }
            if (purge) {
                // recurse dest
                // if dest file is older or missing, remove
                Purge(destination);
            }
            Create();
            Console.WriteLine("Created = {0} Deleted = {1}", created, deleted);
        }

        private void Create () {
            //Console.WriteLine("create");
            foreach (string sp in sourceMap.Keys) {
                string dp = sourceMap[sp];
                string parent = Path.GetDirectoryName(dp);
                if (!Directory.Exists(parent)) {
                    if (commit) {
                        Directory.CreateDirectory(parent);
                    }
                }
                if (!File.Exists(dp)) {
                    if (verbose) {
                        Console.WriteLine("mklink {0} = {1}", dp, sp);
                    }
                    if (commit && !CreateHardLink(dp, sp, IntPtr.Zero)) {
                        throw new Exception("could not create hardlink " + sp + " => " + dp);
                    }
                    created++;
                }
            }
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink (string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        private void Purge (string destdir) {
            //Console.WriteLine("purge {0}", destdir);
            if (recurse) {
                foreach (string dp in Directory.EnumerateDirectories(destdir)) {
                    //Console.WriteLine("  purge dir dp={0}", dp);
                    string dpname = Path.GetFileName(dp);
                    string dpnamelower = dpname.ToLower();
                    if (Matches(includeDirs, dpnamelower, true) && !Matches(excludeDirs, dpnamelower, false)) {
                        Purge(dp);
                    }
                }
            }
            foreach (string dp in Directory.EnumerateFiles(destdir)) {
                string dplower = dp.ToLower();
                string dpname = Path.GetFileName(dp);
                string dpnamelower = dpname.ToLower();
                //Console.WriteLine("  purge file dp={0}", dp);
                if (Matches(includeFiles, dpnamelower, true) && !Matches(excludeFiles, dpnamelower, false)) {
                    bool del = false;
                    if (destMap.TryGetValue(dplower, out string sp)) {
                        DateTime dt = File.GetLastWriteTime(dp);
                        DateTime st = File.GetLastWriteTime(sp);
                        if (st > dt) {
                            del = true;
                        }
                    } else {
                        del = true;
                    }
                    if (del) {
                        if (verbose) {
                            Console.WriteLine("del " + dp);
                        }
                        if (commit) {
                            File.Delete(dp);
                        }
                        deleted++;
                    }
                }
            }
        }

        private void Find (string sourcedir, string subdir) {
            //Console.WriteLine("find sourcedir={0} subdir={1}", sourcedir, subdir);

            if (recurse) {
                foreach (string sp in Directory.EnumerateDirectories(sourcedir + subdir)) {
                    //Console.WriteLine("  find dir={0}", sp);
                    string spname = Path.GetFileName(sp);
                    string spnamelower = spname.ToLower();
                    if (Matches(includeDirs, spnamelower, true) && !Matches(excludeDirs, spnamelower, false)) {
                        Find(sourcedir, subdir + spname + "\\");
                    }
                }
            }

            foreach (string sp in Directory.EnumerateFiles(sourcedir + subdir)) {
                string splower = sp.ToLower();
                string spname = Path.GetFileName(sp);
                string spnamelower = spname.ToLower();
                string dp = destination + (flatten ? "\\" : subdir) + spname;
                string dplower = dp.ToLower();
                //Console.WriteLine("  sp={0}", sp, dp);
                //Console.WriteLine("  dp={0}", dp);

                if (Matches(includeFiles, spnamelower, true) && !Matches(excludeFiles, spnamelower, false)) {
                    if (destMap.ContainsKey(dplower)) {
                        string exdp = destMap[dplower];
                        if (FileEqual(exdp, sp)) {
                            Console.WriteLine("ignoring identical source files for {0}", dp);
                        } else if (quiet) {
                            Console.WriteLine("ignoring different source files for {0}\n  current: {1}\n  ignored: {2}", dp, exdp, sp);
                        } else {
                            throw new Exception(String.Format("multiple source files for {0} (first is {1} second is {2})", dp, exdp, sp));
                        }
                    } else {
                        sourceMap[splower] = dp;
                        destMap[dplower] = sp;
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

        private void GetArgs (string[] args) {
            for (int n = 0; n < args.Length; n++) {
                string a = args[n].ToUpper();
                List<RS> l = null;
                switch (a) {
                    case "/R": recurse = true; break;
                    case "/P": purge = true; break;
                    case "/F": flatten = true; break;
                    case "/C": commit = true; break;
                    case "/Q": quiet = true; break;
                    case "/XF": l = excludeFiles; break;
                    case "/XD": l = excludeDirs; break;
                    case "/IF": l = includeFiles; break;
                    case "/ID": l = includeDirs; break;
                    case "/V": verbose = true; break;
                    case "/?": Usage(); break;
                    default:
                        if (a.StartsWith("/")) {
                            throw new Exception("unrecognised command " + a);
                        } else {
                            sources.Add(args[n]);
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
            if (sources.Count < 2) {
                throw new Exception("require at least one source and one dest");
            }
            destination = sources[sources.Count - 1];
            sources.RemoveAt(sources.Count - 1);
            foreach (string p in sources) {
                if (!Directory.Exists(p)) {
                    throw new Exception("source or destination doesn't exist: " + p);
                }
            }
            Console.WriteLine("recurse = {0} purge = {1} flatten = {2} commit = {3} quiet = {4}", recurse, purge, flatten, commit, quiet);
            foreach (string dir in sources) {
                Console.WriteLine("source = {0}", dir);
            }
            Console.WriteLine("dest = {0}", destination);
            foreach (RS r in includeFiles) {
                Console.WriteLine("include file = {0}", r.str);
            }
            foreach (RS r in includeDirs) {
                Console.WriteLine("include dir = {0}", r.str);
            }
            foreach (RS r in excludeFiles) {
                Console.WriteLine("exclude file = {0}", r.str);
            }
            foreach (RS r in excludeDirs) {
                Console.WriteLine("exclude dir = {0}", r.str);
            }
        }

        static void Usage () {
            Console.WriteLine("RoboLink - Program to mass create NTFS hard links");
            Console.WriteLine("  https://github.com/alexyz/robolink");
            Console.WriteLine("Usage: robolink.exe source1 [source2...n] dest [opts]");
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
