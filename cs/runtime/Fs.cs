// Fs.cs - std::filesystem::path and the filesystem functions the engine calls (POSIX paths, as libstdc++ on
// Linux), and the C library functions the SWMM5 runtime did not need: getenv, ctype in the C locale, strncpy,
// strncmp, memcmp, atol, rename, setvbuf and the char-array helpers of the C# back end.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.IO;

namespace Cpp
{
    public sealed class FsPath : ICopy<FsPath>, IComparable<FsPath>
    {
        public Str p;
        public FsPath() { p = new Str(); }
        public FsPath(Str s) { p = s.Clone(); }
        public FsPath(CPtr s) { p = new Str(s); }
        public FsPath(string s) { p = new Str(s); }
        public FsPath Clone() => new FsPath(p);
        public void clear() { p = new Str(); }
        public void Assign(FsPath o) { p = o.p.Clone(); }
        public void Assign(Str s) { p = s.Clone(); }
        public void MoveFrom(FsPath o) { p = o.p; o.p = new Str(); }
        public int CompareTo(FsPath? o) => p.compare(o!.p);
        public int compare(FsPath o) => p.compare(o.p);
        string S => p.ToString();

        public Str @string() => p.Clone();
        public Str generic_string() => p.Clone();
        public Str native() => p.Clone();
        public CPtr c_str() => p.c_str();
        public Str u8string() => p.Clone();
        public bool empty() => p.n == 0;
        public bool is_absolute() => p.n > 0 && p.b[0] == (byte)'/';
        public bool is_relative() => !is_absolute();
        public bool has_filename() => filename().p.n > 0;
        public bool has_parent_path() => parent_path().p.n > 0;
        public bool has_extension() => extension().p.n > 0;

        /// <summary>The last element (libstdc++: the part after the last '/'; "" when the path ends in '/').</summary>
        public FsPath filename()
        {
            string s = S;
            int i = s.LastIndexOf('/');
            return new FsPath(i < 0 ? s : s.Substring(i + 1));
        }
        public FsPath parent_path()
        {
            string s = S;
            int i = s.LastIndexOf('/');
            if (i < 0) return new FsPath("");
            if (i == 0) return new FsPath("/");
            int j = i;
            while (j > 0 && s[j - 1] == '/') j--;
            return new FsPath(s.Substring(0, j == 0 ? 1 : j));
        }
        public FsPath stem()
        {
            string f = filename().S;
            if (f == "." || f == "..") return new FsPath(f);
            int i = f.LastIndexOf('.');
            return new FsPath(i <= 0 ? f : f.Substring(0, i));
        }
        public FsPath extension()
        {
            string f = filename().S;
            if (f == "." || f == "..") return new FsPath("");
            int i = f.LastIndexOf('.');
            return new FsPath(i <= 0 ? "" : f.Substring(i));
        }
        public FsPath replace_extension(FsPath? ext = null)
        {
            string e = extension().S;
            string s = S.Substring(0, S.Length - e.Length);
            if (ext != null && ext.p.n > 0) s += (ext.p.b[0] == (byte)'.' ? "" : ".") + ext.S;
            p = new Str(s);
            return this;
        }
        public FsPath replace_extension(Str ext) => replace_extension(new FsPath(ext));
        public FsPath replace_filename(FsPath f) { var par = parent_path(); p = par.Join(f).p; return this; }
        public FsPath remove_filename() { string s = S; int i = s.LastIndexOf('/'); p = new Str(i < 0 ? "" : s.Substring(0, i + 1)); return this; }

        /// <summary>operator/ : an absolute right side replaces the path; otherwise a separator is added.</summary>
        public FsPath Join(FsPath o)
        {
            if (o.is_absolute()) return o.Clone();
            string s = S;
            if (s.Length == 0) return o.Clone();
            if (!s.EndsWith("/") && o.p.n > 0) s += "/";
            return new FsPath(s + o.S);
        }
        public FsPath Join(Str o) => Join(new FsPath(o));
        public FsPath Join(CPtr o) => Join(new FsPath(o));

        public FsPath lexically_normal()
        {
            string s = S;
            if (s.Length == 0) return new FsPath("");
            bool abs = s.StartsWith("/");
            var parts = new System.Collections.Generic.List<string>();
            foreach (var x in s.Split('/'))
            {
                if (x == "" || x == ".") continue;
                if (x == ".." && parts.Count > 0 && parts[^1] != "..") { parts.RemoveAt(parts.Count - 1); continue; }
                if (x == ".." && abs) continue;
                parts.Add(x);
            }
            string r = (abs ? "/" : "") + string.Join("/", parts);
            if (s.EndsWith("/") && parts.Count > 0) r += "/";
            if (r == "") r = ".";
            return new FsPath(r);
        }
        public override string ToString() => S;
    }

    public static class Fs
    {
        public static bool exists(FsPath p) => File.Exists(p.ToString()) || Directory.Exists(p.ToString());
        public static bool exists(FsPath p, ErrorCode ec) => exists(p);
        public static bool is_regular_file(FsPath p) => File.Exists(p.ToString());
        public static bool is_directory(FsPath p) => Directory.Exists(p.ToString());
        public static ulong file_size(FsPath p) => (ulong)new FileInfo(p.ToString()).Length;
        /// <summary>file_size(p, ec): libstdc++ sets ENOENT for a missing file, EISDIR for a directory and
        /// returns static_cast&lt;uintmax_t&gt;(-1) on error.</summary>
        public static ulong file_size(FsPath p, ErrorCode ec)
        {
            string s = p.ToString();
            if (File.Exists(s)) { ec.v = 0; return (ulong)new FileInfo(s).Length; }
            ec.v = Directory.Exists(s) ? 21 : 2;
            return ulong.MaxValue;
        }
        public static bool remove(FsPath p) { try { if (File.Exists(p.ToString())) { File.Delete(p.ToString()); return true; } } catch (Exception) { } return false; }
        public static bool create_directories(FsPath p) { try { Directory.CreateDirectory(p.ToString()); return true; } catch (Exception) { return false; } }
        public static FsPath current_path() => new FsPath(Directory.GetCurrentDirectory());
        public static FsPath absolute(FsPath p) => p.is_absolute() ? p.Clone() : current_path().Join(p);
        public static FsPath canonical(FsPath p) => new FsPath(Path.GetFullPath(p.ToString()));
        public static FsPath weakly_canonical(FsPath p) => new FsPath(Path.GetFullPath(p.ToString()));
        public static FsPath temp_directory_path() => new FsPath(Environment.GetEnvironmentVariable("TMPDIR") ?? "/tmp");
        public static void rename(FsPath a, FsPath b) { File.Move(a.ToString(), b.ToString(), true); }
        public static bool equivalent(FsPath a, FsPath b) => Path.GetFullPath(a.ToString()) == Path.GetFullPath(b.ToString());
        public static bool equivalent(FsPath a, FsPath b, ErrorCode ec)
        {
            if (!exists(a) || !exists(b)) { ec.v = 2; return false; }
            ec.v = 0; return equivalent(a, b);
        }
        // the error-code overloads of the checkpoint and atomic-write helpers (ENOENT = 2, EEXIST = 17)
        public static bool remove(FsPath p, ErrorCode ec)
        {
            ec.v = 0; string s = p.ToString();
            try { if (File.Exists(s)) { File.Delete(s); return true; } if (Directory.Exists(s)) { Directory.Delete(s); return true; } }
            catch (Exception) { ec.v = 13; }
            return false;
        }
        public static void rename(FsPath a, FsPath b, ErrorCode ec)
        {
            try { File.Move(a.ToString(), b.ToString(), true); ec.v = 0; } catch (FileNotFoundException) { ec.v = 2; } catch (Exception) { ec.v = 13; }
        }
        public static void create_hard_link(FsPath target, FsPath link, ErrorCode ec)
        {
            if (File.Exists(link.ToString())) { ec.v = 17; return; }
            try { File.Copy(target.ToString(), link.ToString()); ec.v = 0; } catch (Exception) { ec.v = 2; }
        }
        public static FsPath weakly_canonical(FsPath p, ErrorCode ec) { ec.v = 0; return weakly_canonical(p); }
        public static FsPath canonical(FsPath p, ErrorCode ec) { if (!exists(p)) { ec.v = 2; return new FsPath(); } ec.v = 0; return canonical(p); }
        public static FileStatus status(FsPath p) => FileStatus.Of(p.ToString());
        public static FileStatus status(FsPath p, ErrorCode ec) { ec.v = 0; return FileStatus.Of(p.ToString()); }
        public static FileStatus symlink_status(FsPath p) => FileStatus.Of(p.ToString());
        public static FileStatus symlink_status(FsPath p, ErrorCode ec) { ec.v = 0; return FileStatus.Of(p.ToString()); }
        public static bool exists(FileStatus s) => s.kind != FileStatus.NotFound;
        public static bool is_regular_file(FileStatus s) => s.kind == FileStatus.Regular;
        public static bool is_directory(FileStatus s) => s.kind == FileStatus.Directory;
        public static bool is_symlink(FileStatus s) => false;
        public static bool create_directories(FsPath p, ErrorCode ec) { ec.v = 0; return create_directories(p); }
    }

    /// <summary>std::filesystem::file_status, as far as the engine asks: not found, a regular file or a directory.</summary>
    public sealed class FileStatus
    {
        public const int NotFound = -1, Regular = 1, Directory = 2, Other = 3;
        public int kind;
        public static FileStatus Of(string s) => new FileStatus { kind = File.Exists(s) ? Regular : System.IO.Directory.Exists(s) ? Directory : NotFound };
        public int type() => kind;
        public FileStatus Clone() => new FileStatus { kind = kind };
    }

    public static partial class R
    {
        public static int errno_;
        public static CFile stderr_ = new CFile(Console.OpenStandardError(), true);
        public static CFile stdin_ = new CFile(Console.OpenStandardInput(), true);

        public static CPtr getenv(CPtr name)
        {
            var v = Environment.GetEnvironmentVariable(Str(name));
            return v == null ? CPtr.Null : Lit(v);
        }
        public static int isdigit(int c) => (c >= '0' && c <= '9') ? 1 : 0;
        public static int isalpha(int c) => ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) ? 1 : 0;
        public static int isalnum(int c) => (isdigit(c) != 0 || isalpha(c) != 0) ? 1 : 0;
        public static int isspace(int c) => (c == ' ' || (c >= 9 && c <= 13)) ? 1 : 0;
        public static int isupper(int c) => (c >= 'A' && c <= 'Z') ? 1 : 0;
        public static int islower(int c) => (c >= 'a' && c <= 'z') ? 1 : 0;
        public static int ispunct(int c) => (c > 32 && c < 127 && isalnum(c) == 0) ? 1 : 0;
        public static int isprint(int c) => (c >= 32 && c < 127) ? 1 : 0;
        public static int isxdigit(int c) => (isdigit(c) != 0 || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')) ? 1 : 0;
        public static int tolower(int c) => (c >= 'A' && c <= 'Z') ? c + 32 : c;
        public static long atol(CPtr s) { var e = s; return strtol(s, ref e, 10); }
        public static long strtol0(CPtr s, int b = 10) { var e = s; return strtol(s, ref e, b); }
        public static int ferror(CFile f) => f != null && f.Err ? 1 : 0;
        public static int fileno(CFile f) => 3;
        public static int setvbuf(CFile f, CPtr b, int mode, ulong size) => 0;
        public static int rename(CPtr a, CPtr b) { try { File.Move(Str(a), Str(b), true); return 0; } catch (Exception) { return -1; } }
        public static int fgetc(CFile f) { int c = f.S.ReadByte(); if (c < 0) f.ReadFailed(); return c; }
        public static long time(object? p) => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static CPtr strncpy(CPtr d, CPtr s, ulong n)
        {
            int i = 0;
            for (; i < (int)n && s.B[s.O + i] != 0; i++) d.B[d.O + i] = s.B[s.O + i];
            for (; i < (int)n; i++) d.B[d.O + i] = 0;
            return d;
        }
        public static int strncmp(CPtr a, CPtr b, ulong n)
        {
            for (int i = 0; i < (int)n; i++)
            {
                int x = a.B[a.O + i], y = b.B[b.O + i];
                if (x != y) return x - y;
                if (x == 0) return 0;
            }
            return 0;
        }
        public static int memcmp(CPtr a, CPtr b, ulong n)
        {
            for (int i = 0; i < (int)n; i++) { int x = a.B[a.O + i], y = b.B[b.O + i]; if (x != y) return x - y; }
            return 0;
        }
        /// <summary>char buf[n] = { ... }: n bytes, the given ones first, the rest zero.</summary>
        public static CPtr BufOf(int n, int[] vals) { var b = new byte[Math.Max(n, 1)]; for (int i = 0; i < vals.Length && i < b.Length; i++) b[i] = (byte)vals[i]; return new CPtr(b, 0); }
        /// <summary>Copy-assigning a char array member: all its bytes.</summary>
        public static void memcpyBuf(CPtr d, CPtr s) { Array.Copy(s.B, s.O, d.B, d.O, Math.Min(d.B.Length - d.O, s.B.Length - s.O)); }
        public static int sprintf(CPtr buf, CPtr fmt, params object[] args)
        {
            var b = Format(fmt, args);
            Array.Copy(b, 0, buf.B, buf.O, b.Length);
            buf.B[buf.O + b.Length] = 0;
            return b.Length;
        }
    }
}
