// Hand_PluginFactory.cs - PluginFactory's five platform helpers, written by hand (tools/cpp2x/emit_cs.py HAND).
// SWMM6 finds plugins by dlopen'ing shared libraries next to the engine (src/engine/plugins/PluginFactory.cpp).
// A .NET port can load no C++ plugin, so it behaves like the engine's own __EMSCRIPTEN__ build: no library
// directory, so discover() scans nothing, and an explicit load fails with dlopen's message. The reference run
// gives the same result by another road: the only ".so" beside the engine is the engine itself, which exports no
// openswmm_plugin_info, so discover() opens it, finds nothing and closes it again.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
namespace openswmm
{
    public partial class PluginFactory
    {
        static global::Cpp.Str last_load_path_ = new global::Cpp.Str();

        public static global::Cpp.Str get_library_directory() => new global::Cpp.Str();

        public static object platform_load(global::Cpp.Str path)
        {
            last_load_path_ = new global::Cpp.Str(path);
            return null;
        }

        public static void platform_unload(object handle) { }

        public static object platform_sym(object handle, global::Cpp.CPtr sym) => null;

        // what glibc's dlerror() says after dlopen() of a file it cannot load
        public static global::Cpp.Str platform_error() =>
            global::Cpp.Str.Cat(last_load_path_,
                global::Cpp.R.Lit(global::System.IO.File.Exists(last_load_path_.ToString())
                    ? ": invalid ELF header" : ": cannot open shared object file: No such file or directory"));
    }
}
