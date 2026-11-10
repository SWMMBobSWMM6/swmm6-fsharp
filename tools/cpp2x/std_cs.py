"""The C++ standard library for the C# back end: types, construction, member calls, operators and free functions
of std (and the C library), mapped onto cs/runtime. Anything not mapped raises Todo with a name that says what.
Written by Claude (Anthropic); see AI-USE.md."""
import re

import ctypes as C
from model import kids, tstr, FUNC_KINDS, body_of
from names import ident

R = 'global::Cpp.R'
CPP = 'global::Cpp'

STD_REF_CLASSES = {
    'std::stack', 'std::vector', 'std::basic_string', 'std::basic_string_view', 'std::pair', 'std::optional', 'std::unordered_map',
    'std::map', 'std::set', 'std::unordered_set', 'std::multimap', 'std::deque', 'std::list', 'std::tuple',
    'std::basic_ostringstream', 'std::basic_istringstream', 'std::basic_stringstream', 'std::basic_ifstream',
    'std::basic_ofstream', 'std::basic_fstream', 'std::basic_ostream', 'std::basic_istream', 'std::basic_ios',
    'std::filesystem::path', 'std::filesystem::__cxx11::path', 'std::runtime_error', 'std::exception',
    'std::logic_error', 'std::invalid_argument', 'std::out_of_range', 'std::mutex', 'std::atomic', 'std::queue',
    'std::condition_variable', 'std::thread', 'std::variant', 'std::once_flag', 'std::__exception_ptr::exception_ptr',
    'std::priority_queue', 'std::bitset', 'std::basic_streambuf', 'std::shared_mutex', 'std::recursive_mutex',
}
VALUE_TYPES = {'__gnu_cxx::__normal_iterator', 'std::chrono::duration', 'std::chrono::time_point',
               'std::__detail::_Node_iterator', 'std::__detail::_Node_const_iterator', 'std::_Rb_tree_iterator',
               'std::_Rb_tree_const_iterator', 'std::__detail::_Node_iterator_base', 'std::_Bit_reference'}
COPYABLE = {'std::stack', 'std::queue', 'std::variant', 'std::vector', 'std::basic_string', 'std::pair', 'std::optional', 'std::unordered_map', 'std::map',
            'std::set', 'std::unordered_set', 'std::tuple', 'std::array', 'std::deque', 'std::list',
            'std::filesystem::path', 'std::filesystem::__cxx11::path', 'std::basic_string_view'}


def base_name(t):
    n = t.name
    n = n.replace('std::__cxx11::', 'std::').replace('std::filesystem::__cxx11::', 'std::filesystem::')
    if n in ('std::chrono::steady_clock::time_point', 'std::chrono::system_clock::time_point',
             'std::chrono::_V2::steady_clock::time_point', 'std::chrono::_V2::system_clock::time_point',
             'std::chrono::high_resolution_clock::time_point'):
        n = 'std::chrono::time_point'
    if n in ('std::__atomic_base', 'std::__atomic_float'):
        n = 'std::atomic'          # atomic<int> and atomic<double> inherit their members from these
    return n


def is_std_class(t):
    return isinstance(t, C.Rec) and base_name(t) in STD_REF_CLASSES | {'std::unique_ptr', 'std::shared_ptr', 'std::function'}


def is_file(t):
    return isinstance(t, C.Rec) and t.name in ('FILE', '_IO_FILE')


def is_value_type(t):
    return isinstance(t, C.Rec) and base_name(t) in VALUE_TYPES


def is_copyable_class(t):
    return isinstance(t, C.Rec) and base_name(t) in COPYABLE


def cs_string(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n').replace('\r', '\\r').replace('\t', '\\t') + '"'


def c_unescape(lit):
    """The bytes of a C string literal as clang spells it (with its quotes)."""
    s = lit
    if s.startswith('u8'):
        s = s[2:]
    assert s[0] == '"' and s[-1] == '"', lit
    s = s[1:-1]
    out = bytearray()
    i = 0
    while i < len(s):
        ch = s[i]
        if ch != '\\':
            out += ch.encode('utf-8')
            i += 1
            continue
        i += 1
        e = s[i]
        if e in 'ntrabfv0\\\'"?':
            if e == '0' and i + 1 < len(s) and s[i + 1] in '01234567':
                j = i
                while j < len(s) and j < i + 3 and s[j] in '01234567':
                    j += 1
                out.append(int(s[i:j], 8) & 0xFF)
                i = j
                continue
            out.append({'n': 10, 't': 9, 'r': 13, 'a': 7, 'b': 8, 'f': 12, 'v': 11, '0': 0, '\\': 92, "'": 39,
                        '"': 34, '?': 63}[e])
            i += 1
        elif e == 'x':
            j = i + 1
            while j < len(s) and s[j] in '0123456789abcdefABCDEF':
                j += 1
            out.append(int(s[i + 1:j], 16) & 0xFF)
            i = j
        elif e in '1234567':
            j = i
            while j < len(s) and j < i + 3 and s[j] in '01234567':
                j += 1
            out.append(int(s[i:j], 8) & 0xFF)
            i = j
        else:
            out += e.encode()
            i += 1
    return bytes(out)


def bytes_cs(b):
    """A C# string literal whose chars are the bytes b (R.Lit encodes chars as Latin-1 bytes)."""
    out = []
    for x in b:
        c = chr(x)
        if c == '\\':
            out.append('\\\\')
        elif c == '"':
            out.append('\\"')
        elif 32 <= x < 127:
            out.append(c)
        else:
            out.append('\\u%04x' % x)
    return '"' + ''.join(out) + '"'


def literal(fn, value):
    return '%s.Lit(%s)' % (R, bytes_cs(c_unescape(value)))


# ---------------------------------------------------------------------------------------------- types
def cs_type(cs, t):
    n = base_name(t)
    a = t.args

    def arg(i, role='elem'):
        return cs.cs(a[i], role)
    if n == 'std::vector':
        return '%s.Vec<%s>' % (CPP, arg(0))
    if n in ('std::basic_string', 'std::basic_string_view'):
        return '%s.Str' % CPP
    if n == 'std::array':
        return '%s[]' % arg(0)
    if n == 'std::pair':
        return '%s.Pair<%s, %s>' % (CPP, arg(0), arg(1))
    if n == 'std::tuple' and len(a) > 3:
        return '%s.TupN' % CPP
    if n == 'std::tuple':
        return '%s.Tup<%s>' % (CPP, ', '.join(cs.cs(x, 'elem') for x in a))
    if n == 'std::optional':
        return '%s.Opt<%s>' % (CPP, arg(0))
    if n in ('std::unique_ptr', 'std::shared_ptr', 'std::weak_ptr'):
        inner = a[0]
        if isinstance(inner, C.Arr):
            return '%s[]' % cs.cs(inner.t, 'elem')
        if is_file(inner):
            return '%s.CFile' % CPP
        return cs.cs(inner, 'elem')
    if n == 'std::function':
        f = a[0]
        return cs.delegate(f)
    def user_fn(i):
        # a hasher or key-equal of the engine's own (CiHash, CiEqual), not std::hash / std::equal_to, whose
        # operator() is translated (a container in code the engine never runs keeps the default hashing)
        if not (len(a) > i and isinstance(a[i], C.Rec)):
            return False
        rk = cs.m.key_of_type(a[i])
        return rk in cs.m.records and any(
            cs.m.d[mk].get('name') == 'operator()' and (cs.m.reachable is None or mk in cs.m.reachable)
            for mk in cs.m.methods.get(rk, []))
    if n == 'std::unordered_map':
        if user_fn(2):
            eq = arg(3) if user_fn(3) else '%s.StdEq<%s>' % (CPP, arg(0))
            return '%s.UMapC<%s, %s, %s, %s>' % (CPP, arg(0), arg(1), arg(2), eq)
        return '%s.UMap<%s, %s>' % (CPP, arg(0), arg(1))
    if n == 'std::map':
        return '%s.Map<%s, %s>' % (CPP, arg(0), arg(1))
    if n == 'std::unordered_set':
        if user_fn(1):
            eq = arg(2) if user_fn(2) else '%s.StdEq<%s>' % (CPP, arg(0))
            return '%s.USetC<%s, %s, %s>' % (CPP, arg(0), arg(1), eq)
        return '%s.USet<%s>' % (CPP, arg(0))
    if n == 'std::set':
        return '%s.Set<%s>' % (CPP, arg(0))
    if n == 'std::initializer_list':
        return '%s[]' % arg(0)
    if n == 'std::stack':
        return '%s.Stk<%s>' % (CPP, arg(0))
    if n == 'std::queue':
        return '%s.Que<%s>' % (CPP, arg(0))
    if n in ('std::__detail::_Node_iterator', 'std::__detail::_Node_const_iterator', 'std::_Rb_tree_iterator',
             'std::_Rb_tree_const_iterator', 'std::__detail::_Node_iterator_base'):
        el = a[0]
        if isinstance(el, C.Rec) and base_name(el) == 'std::pair':
            return '%s.MIt<%s.Pair<%s, %s>>' % (CPP, CPP, cs.cs(el.args[0], 'elem'), cs.cs(el.args[1], 'elem'))
        return '%s.MIt<%s>' % (CPP, cs.cs(el, 'elem'))
    if n in ('std::__shared_ptr_access', 'std::__shared_ptr'):
        return cs.cs(a[0], 'elem')
    if n == 'std::_Bit_reference':
        return 'bool'
    if n == 'std::numeric_limits':
        return 'object'
    if n == 'std::span':
        return '%s.CSpan<%s>' % (CPP, arg(0))
    if n == '__gnu_cxx::__normal_iterator':
        p = a[0]
        if isinstance(p, C.Ptr):
            el = p.t
            if isinstance(el, C.Builtin) and el.name == 'char':
                return '%s.Iter<byte>' % CPP
            return '%s.Iter<%s>' % (CPP, cs.cs(el, 'elem'))
    if n == 'std::reverse_iterator':
        inner = a[0]
        if isinstance(inner, C.Rec) and inner.args and isinstance(inner.args[0], C.Ptr):
            return '%s.RevIter<%s>' % (CPP, cs.cs(inner.args[0].t, 'elem'))
    if n in ('std::basic_ostringstream', 'std::basic_stringstream'):
        return '%s.OStringStream' % CPP
    if n == 'std::basic_istringstream':
        return '%s.IStringStream' % CPP
    if n == 'std::basic_ifstream':
        return '%s.IFStream' % CPP
    if n == 'std::basic_ofstream':
        return '%s.OFStream' % CPP
    if n == 'std::basic_fstream':
        return '%s.FStream' % CPP
    if n in ('std::basic_ostream',):
        return '%s.OStream' % CPP
    if n in ('std::basic_istream',):
        return '%s.IStream' % CPP
    if n == 'std::basic_ios':
        return '%s.IosBase' % CPP
    if n in ('std::filesystem::path',):
        return '%s.FsPath' % CPP
    if n in ('std::exception',):
        return '%s.CppException' % CPP
    if n in ('std::runtime_error',):
        return '%s.RuntimeError' % CPP
    if n in ('std::logic_error',):
        return '%s.LogicError' % CPP
    if n in ('std::invalid_argument',):
        return '%s.InvalidArgument' % CPP
    if n in ('std::out_of_range',):
        return '%s.OutOfRange' % CPP
    if n == 'std::atomic':
        return '%s.Atomic<%s>' % (CPP, arg(0))
    if n in ('std::mutex', 'std::recursive_mutex', 'std::shared_mutex'):
        return '%s.Mutex' % CPP
    if n == 'std::once_flag':
        return '%s.OnceFlag' % CPP
    if n == 'std::variant':
        return '%s.Variant' % CPP
    if n in ('std::__exception_ptr::exception_ptr', 'std::exception_ptr'):
        return 'global::System.Exception'
    if n in ('std::condition_variable', 'std::condition_variable_any'):
        return '%s.CondVar' % CPP
    if n == 'std::thread':
        return '%s.Thread' % CPP
    if n in ('std::lock_guard', 'std::unique_lock', 'std::scoped_lock', 'std::shared_lock'):
        return '%s.Lock' % CPP
    if n == 'std::chrono::duration':
        return '%s.Duration' % CPP
    if n == 'std::chrono::time_point':
        return '%s.TimePoint' % CPP
    if n in ('std::_Ios_Openmode', 'std::_Ios_Fmtflags', 'std::_Ios_Iostate', 'std::_Ios_Seekdir'):
        return 'int'
    if n in ('std::byte',):
        return 'byte'
    if n == 'std::errc':
        return 'int'
    if n == 'std::locale':
        return 'object'          # only ever std::locale::classic(), handed to imbue(), which the runtime ignores
    if n == 'std::from_chars_result':
        return '%s.FromCharsResult' % CPP
    if n == 'std::nullopt_t':
        return 'object'
    if n == 'std::default_delete':
        return 'object'
    if n == 'std::hash':
        return '%s.Hash<%s>' % (CPP, arg(0))
    if n == 'std::equal_to':
        return '%s.EqualTo<%s>' % (CPP, arg(0))
    if n == 'std::less':
        return '%s.Less<%s>' % (CPP, arg(0) if a else 'object')
    if n == 'std::filesystem::directory_iterator':
        return '%s.DirIter' % CPP
    if n == 'std::filesystem::file_status':
        return '%s.FileStatus' % CPP
    if n == 'std::error_code':
        return '%s.ErrorCode' % CPP
    return None


def elem_maker(cs, el):
    """A C# lambda making one new element of type el, for a container whose element is an array (std::array or
    T[n]): the C# element type (double[]) does not carry the size. None when the element type says enough."""
    el = el.strip() if isinstance(el, (C.LRef, C.RRef)) else el
    if isinstance(el, C.Arr) and el.n:
        return '() => %s' % cs.default_value(el)
    if isinstance(el, C.Rec) and base_name(el) == 'std::array':
        return '() => %s' % default_value(cs, el)
    return None


def default_value(cs, t):
    n = base_name(t)
    if n in ('std::unique_ptr', 'std::shared_ptr', 'std::weak_ptr', 'std::function'):
        return 'null'
    if n == 'std::array':
        el = t.args[0]
        cnt = t.args[1].v if isinstance(t.args[1], C.Value) else '0'
        if isinstance(el, C.Arr):
            return '%s.Arr.Make2<%s>(%s, %d)' % (CPP, cs.cs(el.t, 'elem'), cnt, el.n)
        if isinstance(el, C.Rec) and base_name(el) == 'std::array':
            # std::array<std::array<T, m>, n>: n rows, each made like the inner array
            return '%s.Arr.MakeWith<%s>(%s, () => %s)' % (CPP, cs.cs(el, 'elem'), re.sub(r'[^0-9]', '', str(cnt)),
                                                         default_value(cs, el))
        return '%s.Arr.Make<%s>(%s)' % (CPP, cs.cs(el, 'elem'), re.sub(r'[^0-9]', '', str(cnt)))
    if n in ('__gnu_cxx::__normal_iterator', 'std::chrono::duration', 'std::chrono::time_point'):
        return 'default(%s)' % cs.cs(t)
    if n in ('std::_Ios_Openmode', 'std::_Ios_Fmtflags', 'std::_Ios_Iostate', 'std::errc', 'std::byte'):
        return '0'
    if n == 'std::initializer_list':
        return 'null'
    if n == 'std::span':
        return 'default(%s)' % cs.cs(t)
    s = cs.cs(t)
    if s and s.startswith('global::Cpp.') and not s.endswith('[]'):
        if n == 'std::vector' and t.args:
            mk = elem_maker(cs, t.args[0])
            if mk:
                return 'new %s(%s)' % (s, mk)
        if n in ('std::unordered_map', 'std::map') and len(t.args) > 1:
            mk = elem_maker(cs, t.args[1])
            if mk:
                return 'new %s(mk: %s)' % (s, mk)
        return 'new %s()' % s
    return None


def copy_of(fn, code, t):
    """A copy of the object 'code' of class type t (C++ copy construction)."""
    t = t.strip()
    if isinstance(t, C.Arr):
        return '%s.Arr.Clone(%s)' % (CPP, code)
    if isinstance(t, C.Rec):
        n = base_name(t)
        if n == 'std::array':
            return '%s.Arr.Clone(%s)' % (CPP, code)
        if n in ('std::unique_ptr', 'std::shared_ptr', 'std::function', 'std::initializer_list', 'std::span',
                 'std::__exception_ptr::exception_ptr', 'std::exception_ptr', 'std::thread', 'std::mutex'):
            return code
        return '%s.Clone()' % code
    return code


def copy_of_field(cs, code, t):
    if isinstance(t, (C.LRef, C.RRef)):
        return code
    if isinstance(t, C.Ptr) or isinstance(t, C.Builtin):
        return code
    if isinstance(t, C.Rec) and (cs.m.key_of_type(t) in cs.m.enums or t.name in cs.m.enums):
        return code
    if isinstance(t, C.Arr):
        if isinstance(t.t, C.Builtin) and t.t.name == 'char':
            return '%s.CopyBuf(%s)' % (R, code)
        return '%s.Arr.Clone(%s)' % (CPP, code)
    if isinstance(t, C.Rec):
        n = base_name(t)
        if n in ('std::unique_ptr', 'std::shared_ptr', 'std::function', 'std::weak_ptr', 'std::atomic',
                 'std::mutex', 'std::chrono::duration', 'std::chrono::time_point', '__gnu_cxx::__normal_iterator'):
            return code
        if cs.m.key_of_type(t) in cs.m.records or is_copyable_class(t):
            if n == 'std::array':
                return '%s.Arr.Clone(%s)' % (CPP, code)
            return '%s?.Clone()' % code
    return code


def assign_field(cs, dst, src, t):
    if isinstance(t, (C.LRef, C.RRef)):
        return '%s = %s;' % (dst, src)
    if isinstance(t, C.Arr):
        if isinstance(t.t, C.Builtin) and t.t.name == 'char':
            return '%s.memcpyBuf(%s, %s);' % (R, dst, src)
        return '%s.Arr.Assign(%s, %s);' % (CPP, dst, src)
    if isinstance(t, C.Rec):
        n = base_name(t)
        if n == 'std::array':
            return '%s.Arr.Assign(%s, %s);' % (CPP, dst, src)
        if n in ('std::unique_ptr', 'std::shared_ptr', 'std::function', 'std::weak_ptr', 'std::chrono::duration',
                 'std::chrono::time_point', '__gnu_cxx::__normal_iterator', 'std::atomic', 'std::mutex'):
            return '%s = %s;' % (dst, src)
        if cs.m.key_of_type(t) in cs.m.records or is_copyable_class(t):
            return '%s.Assign(%s);' % (dst, src)
    return '%s = %s;' % (dst, src)


def move_field(cs, dst, src, t):
    if isinstance(t, C.Rec) and not (cs.m.key_of_type(t) in cs.m.enums or t.name in cs.m.enums):
        n = base_name(t)
        if n in ('std::vector', 'std::basic_string') or cs.m.key_of_type(t) in cs.m.records or is_copyable_class(t):
            if n == 'std::array':
                return '%s = %s;' % (dst, src)
            return '%s = %s; %s = %s;' % (dst, src, src, cs.default_value(t))
        if n in ('std::unique_ptr',):
            return '%s = %s; %s = null;' % (dst, src, src)
    return '%s = %s;' % (dst, src)


# ---------------------------------------------------------------------------------------------- helpers
def E(*a, **k):
    from emit_cs import E as _E
    return _E(*a, **k)


def todo(fn, what, n=None):
    fn.cs.todo(what, n)


def strip(n):
    from cs_func import strip_trans
    return strip_trans(n)


def is_class_obj(fn, t):
    return fn.cs.is_class(t)


def std_name(t):
    t = t.strip() if isinstance(t, C.Type) else t
    return base_name(t) if isinstance(t, C.Rec) else None


def nonnull(fn, e):
    t = e.t.strip() if e.t is not None else None
    if isinstance(t, C.Ptr):
        p = t.t
        cst = fn.cs.cs(t)
        if cst.startswith('global::Cpp.Ptr<') or cst == 'global::Cpp.CPtr':
            return '!%s.IsNull' % fn.obj(e)
    if isinstance(t, C.Rec) and base_name(t) == 'std::span':
        return '!%s.IsNull' % fn.obj(e)
    return '(%s != null)' % e.code


def exception_type(fn, et):
    if isinstance(et, C.Rec):
        k = fn.m.key_of_type(et)
        if k in fn.m.records:
            return fn.cs.rec_name(k)
        s = cs_type(fn.cs, et)
        if s:
            return s
    return 'global::System.Exception'


def exception_base(em, rk):
    """An engine class deriving from a std exception derives from the runtime's exception class."""
    r = em.m.records[rk]
    for b in r.get('bases', []):
        bt = C.parse(tstr(b.get('type')))
        if isinstance(bt, C.Rec) and base_name(bt) in ('std::runtime_error', 'std::exception', 'std::logic_error',
                                                       'std::invalid_argument', 'std::out_of_range'):
            return cs_type(em.cs, bt)
    # a plain struct the engine throws (throw GwfFail{...}) must be an Exception in C#
    if not r.get('bases') and r.get('name') in thrown_names(em.m):
        return CPP + '.CppException'
    return None


_THROWN = {}


def thrown_names(m):
    """The last name component of every class type thrown in the reachable code."""
    if id(m) not in _THROWN:
        names = set()
        for f in m.funcs.values():
            stack = [f]
            while stack:
                x = stack.pop()
                if x.get('kind') == 'CXXThrowExpr' and x.get('inner'):
                    tn = tstr(x['inner'][0].get('type')).replace('const ', '').strip()
                    names.add(re.sub(r'<.*', '', tn).split('::')[-1])
                stack.extend(x.get('inner', ()))
        _THROWN[id(m)] = names
    return _THROWN[id(m)]


def float_to_int(fn, e, st, tt):
    tcs = fn.cs.cs(tt)
    scs = fn.cs.cs(st)
    src = e.code if scs == 'double' else '(double)%s' % e.code
    f = {'int': 'D2I', 'long': 'D2L', 'uint': 'D2U', 'ulong': 'D2UL', 'short': 'D2S', 'ushort': 'D2US',
         'sbyte': 'D2SB', 'byte': 'D2B'}.get(tcs)
    if is_enum_t(fn, tt):
        return '((%s)%s.D2I(%s))' % (tcs, CPP + '.Conv', src)
    if not f:
        return todo(fn, 'float to ' + tcs)
    return '%s.Conv.%s(%s)' % (CPP, f, src)


def is_enum_t(fn, t):
    return isinstance(t, C.Rec) and (fn.m.key_of_type(t) in fn.m.enums or t.name in fn.m.enums)


def decay(fn, e, st, t, sub):
    """An array (or string literal) used as a pointer."""
    sst = st.strip() if isinstance(st, (C.LRef, C.RRef)) else st
    if isinstance(sst, C.Arr) and isinstance(sst.t, C.Builtin) and sst.t.name == 'char':
        return E(e.code, t)          # char arrays are CPtr already
    if isinstance(sst, C.Arr):
        if isinstance(sst.t, C.Arr) and isinstance(sst.t.t, C.Builtin) and sst.t.t.name == 'char':
            return E('new %s.Ptr<%s.CPtr>(%s, 0)' % (CPP, CPP, e.code), t)     # char a[n][m]: rows are CPtr
        if isinstance(sst.t, C.Arr):
            return E('new %s.Ptr<%s[]>(%s, 0)' % (CPP, fn.cs.cs(sst.t.t, 'elem'), e.code), t)
        return E('new %s.Ptr<%s>(%s, 0)' % (CPP, fn.cs.cs(sst.t, 'elem'), e.code), t)
    return todo(fn, 'decay of ' + repr(sst), sub)


def bitcast(fn, n, sub):
    t = C.of(n).strip()
    st = C.of(sub).strip()
    # static_cast<T*>(calloc(n, size)) / malloc(size): a new zeroed array of T
    inner = strip(sub)
    if inner.get('kind') == 'CallExpr' and isinstance(t, C.Ptr):
        cal = unwrap_callee(kids(inner)[0])
        nm = (cal.get('referencedDecl') or {}).get('name')
        if nm in ('calloc', 'malloc'):
            av = [fn.x(x).code for x in kids(inner)[1:]]
            el = fn.cs.cs(t.t, 'elem')
            if nm == 'calloc':
                return E('%s.Mem.Calloc<%s>((ulong)(%s), (ulong)(%s))' % (CPP, el, av[0], av[1]), t)
            return E('%s.Mem.Calloc<%s>(1UL, (ulong)(%s))' % (CPP, el, av[0]), t)
    e = fn.x(sub)
    tcs = fn.cs.cs(t)
    if tcs == fn.cs.cs(st):
        return E(e.code, t, e.lv)
    if tcs == 'object' or fn.cs.cs(st) == 'object':
        if tcs == 'object':
            return E(e.code, t)
        return E('((%s)%s)' % (tcs, e.code), t)
    # (char*)&x handed to a reader of bytes (the f2c do_lio of the UEB code): a copy of x's bytes. Writers through
    # such a pointer (stream reads, fread) are translated before this point, so a copy is safe here.
    if tcs == CPP + '.CPtr' and fn.cs.cs(st).startswith(CPP + '.Ptr<'):
        return E('%s.Bytes.View(%s)' % (CPP, e.code), t)
    return todo(fn, 'bit cast %s -> %s' % (fn.cs.cs(st), tcs), n)


def reinterpret(fn, n, sub):
    """reinterpret_cast: the engine uses it to view an object's bytes for binary reads and writes."""
    t = C.of(n).strip()
    inner = strip(sub)
    if isinstance(t, C.Ptr) and isinstance(t.t, C.Builtin) and t.t.name in ('char', 'unsigned char'):
        # (const) char* view of an object: &x, a pointer, or v.data()
        e = fn.x(sub)
        st = C.of(sub).strip()
        if isinstance(st, C.Ptr):
            return E('%s.Bytes.View(%s)' % (CPP, e.code), t)
        return todo(fn, 'reinterpret_cast of non-pointer to char*', n)
    e = fn.x(sub)
    tcs = fn.cs.cs(t)
    if tcs == 'object' or fn.cs.cs(C.of(sub).strip()) == 'object':
        return E('((%s)%s)' % (tcs, e.code) if tcs != 'object' else e.code, t)
    return todo(fn, 'reinterpret_cast to ' + repr(t), n)


def pointer_op(fn, op, ea, eb, at, bt, t, n):
    if op in ('==', '!='):
        cs = fn.cs.cs(at if isinstance(at, C.Ptr) else bt)
        if cs.startswith('global::Cpp.Ptr<') or cs == 'global::Cpp.CPtr':
            a, b = ea.code, eb.code
            if a == 'null':
                a = 'default(%s)' % cs
            if b == 'null':
                b = 'default(%s)' % cs
            return E('(%s %s %s)' % (a, op, b), t)
        if cs == 'object' or cs.startswith('global::Gen.') or cs.startswith('global::System.'):
            return E('(%s %s %s)' % (ea.code, op, eb.code), t)
        return E('(%s(object)%s %s (object)%s)' % ('' if op == '==' else '', ea.code, op, eb.code), t)
    if op in ('+', '-') and isinstance(at, C.Ptr) and not isinstance(bt, C.Ptr):
        return E('(%s %s (long)(%s))' % (ea.code, op, eb.code), t)
    if op == '+' and isinstance(bt, C.Ptr):
        return E('(%s + (long)(%s))' % (eb.code, ea.code), t)
    if op == '-' and isinstance(at, C.Ptr) and isinstance(bt, C.Ptr):
        if fn.cs.cs(at) == 'global::Cpp.CPtr':
            return E('((long)%s.CPtr.Diff(%s, %s))' % (CPP, ea.code, eb.code), t)
        an, bn = kids(n)[0], kids(n)[1]
        pa, pb = is_ptr_valued(fn, an), is_ptr_valued(fn, bn)
        if isinstance(at.t, C.Rec) and fn.m.key_of_type(at.t) in fn.m.records and pa != pb:
            # &element - v.data(): one side is an object reference, the other an array pointer
            return E('%s.Mem.Diff(%s, %s)' % (CPP, ea.code, eb.code), t)
        return E('(%s - %s)' % (ea.code, eb.code), t)
    if op in ('<', '>', '<=', '>='):
        return E('(%s %s %s)' % (ea.code, op, eb.code), t)
    return todo(fn, 'pointer operator ' + op, n)


def pointer_compound(fn, op, ea, eb, at, n):
    if op in ('+', '-'):
        return E('(%s = %s %s (long)(%s))' % (ea.code, ea.code, op, eb.code), at, True)
    return todo(fn, 'pointer compound ' + op, n)


def pointer_incdec(fn, op, e, at, post, n):
    d = '+ 1' if op == '++' else '- 1'
    if post:
        return E('%s.Mv.PostStep(ref %s, %d)' % (CPP, e.code, 1 if op == '++' else -1), at)
    return E('(%s = %s %s)' % (e.code, e.code, d), at, True)


def class_compare(fn, op, ea, eb, at, n):
    return todo(fn, 'built-in comparison of class values', n)


def class_assign(fn, ea, eb, at, b):
    return E('%s.Assign(%s)' % (fn.obj(ea), eb.code), at)


def class_compound(fn, op, ea, eb, at, n):
    return todo(fn, 'compound assignment on a class', n)


def deref(fn, e, at, n):
    t = C.of(n)
    if isinstance(at, C.Ptr):
        cs = fn.cs.cs(at)
        if cs.startswith('global::Cpp.Ptr<'):
            return E('%s.Deref' % fn.obj(e), t, True)
        if cs == 'global::Cpp.CPtr':
            return E('%s[0]' % fn.obj(e), t, 'set')
        return E(e.code, t, True)        # an object pointer: the object itself
    return todo(fn, 'deref of ' + repr(at), n)


def subscript(fn, ea, ei, at, t, n):
    if isinstance(at, C.Ptr):
        cs = fn.cs.cs(at)
        if cs == 'global::Cpp.CPtr':
            if isinstance(at.t, C.Builtin) and at.t.name == 'unsigned char':
                return E('((byte)%s[(int)(%s)])' % (fn.obj(ea), ei.code), t, 'ucptr:%s[(int)(%s)]' % (fn.obj(ea), ei.code))
            return E('((sbyte)%s[(int)(%s)])' % (fn.obj(ea), ei.code), t, 'cptr:%s[(int)(%s)]' % (fn.obj(ea), ei.code))
        return E('%s[%s]' % (fn.obj(ea), ei.code), t, True)
    if isinstance(at, C.Arr):
        if isinstance(at.t, C.Builtin) and at.t.name == 'char':
            return E('((sbyte)%s[(int)(%s)])' % (fn.obj(ea), ei.code), t, 'cptr:%s[(int)(%s)]' % (fn.obj(ea), ei.code))
        return E('%s[%s]' % (fn.obj(ea), ei.code), t, True)
    return todo(fn, 'subscript of ' + repr(at), n)


def address_of(fn, a, n):
    t = fn.T(n)
    inner = strip(a)
    at = C.of(a).strip()
    k = inner.get('kind')
    # &C::field -> a pointer to data member: a function returning a reference to that field of its argument
    if isinstance(t, C.MemPtr):
        rd = inner.get('referencedDecl') or {}
        mk = rd.get('key')
        if inner.get('kind') == 'DeclRefExpr' and mk in fn.m.fields:
            cls = fn.cs.cs(t.cls)
            return E('new %s.MemPtr<%s, %s>((%s o__) => ref o__.%s)' % (CPP, cls, fn.cs.cs(t.t, 'elem'), cls,
                                                                       fn.cs.field_name(mk, rd.get('name'))), t)
        return fn.todo('pointer to member', n)
    # &object -> the object (pointers to objects are references)
    if fn.cs.cs(t) not in ('object',) and isinstance(t, C.Ptr) and isinstance(t.t, C.Rec) and \
            (fn.m.key_of_type(t.t) in fn.m.records or is_std_class(t.t)):
        e = fn.x(a)
        return E(e.code, t)
    if isinstance(t, C.Ptr) and isinstance(t.t, C.Func):
        e = fn.x(a)
        return E(e.code, t)
    # &v[i] / &arr[i] / &p[i] -> a pointer into the array
    if k == 'CXXOperatorCallExpr' and op_name(inner) == 'operator[]':
        obj = kids(inner)[1]
        idx = kids(inner)[2]
        ot = C.of(obj).strip()
        oe, ie = fn.x(obj), fn.x(idx)
        if std_name(ot) == 'std::vector':
            return E('new %s.Ptr<%s>(%s.a, (long)(%s))' % (CPP, fn.cs.cs(ot.args[0], 'elem'), fn.obj(oe), ie.code), t)
        if std_name(ot) == 'std::array':
            return E('new %s.Ptr<%s>(%s, (long)(%s))' % (CPP, fn.cs.cs(ot.args[0], 'elem'), oe.code, ie.code), t)
        if std_name(ot) in ('std::basic_string',):
            return E('new %s.CPtr(%s.b, (int)(%s))' % (CPP, fn.obj(oe), ie.code), t)
    if k == 'ArraySubscriptExpr':
        base, idx = kids(inner)
        bt = C.of(base).strip()
        be, ie = fn.x(base), fn.x(idx)
        if isinstance(bt, C.Ptr) and fn.cs.cs(bt).startswith('global::Cpp.Ptr<'):
            return E('(%s + (long)(%s))' % (be.code, ie.code), t)
        if fn.cs.cs(bt) == 'global::Cpp.CPtr':
            return E('(%s + (int)(%s))' % (be.code, ie.code), t)
        return todo(fn, 'address of element of ' + repr(bt), n)
    if k == 'UnaryOperator' and inner.get('opcode') == '*':
        return fn.x(kids(inner)[0])
    if isinstance(at, C.Arr):
        e = fn.x(a)
        return E('new %s.Ptr<%s[]>(new[] { %s }, 0)' % (CPP, fn.cs.cs(at.t, 'elem'), e.code), t)
    # &obj.field, a scalar field of an engine object: a pointer to the field itself (the object, and how to reach
    # the field from it), so a pointer kept after the call (const double* terms[] = {&mb.x, ...}) still sees the
    # field change. The object is evaluated once, here.
    if k == 'MemberExpr':
        mk = inner.get('referencedMemberDecl')
        if mk and mk in fn.m.fields:
            rk, fdecl = fn.m.fields[mk]
            ftd = C.parse(tstr(fdecl.get('type')))
            base_n = kids(inner)[0]
            bt = C.of(base_n).strip()
            if inner.get('isArrow') and isinstance(bt, C.Ptr):
                bt = bt.t.strip() if isinstance(bt.t, (C.LRef, C.RRef)) else bt.t
            if not isinstance(ftd, (C.LRef, C.RRef)) and isinstance(bt, C.Rec) and fn.m.key_of_type(bt) in fn.m.records:
                b = fn.x(base_n)
                if inner.get('isArrow') and fn.arrptr_ref(base_n):
                    bcode = '%s.Deref' % fn.obj(b)
                else:
                    bcode = fn.obj(b)
                cls = fn.cs.cs(bt)
                ct = fn.cs.cs(at, 'elem')
                return E('new %s.Ptr<%s>(%s, (object o__) => ref ((%s)o__).%s)' % (
                    CPP, ct, bcode, cls, fn.cs.field_name(mk, inner.get('name'))), t)
    # &global or &Class::static_member: a pointer to that variable
    if k == 'DeclRefExpr':
        vk = (inner.get('referencedDecl') or {}).get('key')
        if vk and vk in fn.m.vars and vk not in fn.names:
            ct = fn.cs.cs(at, 'elem')
            return E('new %s.Ptr<%s>(null, (object o__) => ref %s)' % (CPP, ct, fn.cs.var_name(fn.m.vars[vk])), t)
    # &scalar for an out-parameter: a one-element box, copied back into the variable right after the call
    # (Fn.wrap_backs) - C# cannot hand out a pointer to a local
    e = fn.x(a)
    if e.lv is not True:
        return todo(fn, 'address of a scalar that is not an lvalue (%s)' % k, n)
    ct = fn.cs.cs(at, 'elem')
    b = fn.tmp('b')
    fn.pre.append('var %s = new %s[] { %s };' % (b, ct, e.code))
    fn.backs.append((e.code, b))
    return E('new %s.Ptr<%s>(%s, 0)' % (CPP, ct, b), t)


def op_name(n):
    c = unwrap_callee(kids(n)[0])
    rd = c.get('referencedDecl') or {}
    return rd.get('name')


def range_iter(fn, rt, rv, iv, n):
    nm = std_name(rt)
    if nm == 'std::vector':
        return ('%s.n' % rv, '%s.a[%s]' % (rv, iv), 'ref')
    if nm in ('std::array', 'std::initializer_list') or isinstance(rt, C.Arr):
        return ('%s.Length' % rv, '%s[%s]' % (rv, iv), 'ref')
    if nm in ('std::basic_string', 'std::basic_string_view'):
        return ('%s.n' % rv, '((sbyte)%s.b[%s])' % (rv, iv), 'value')
    if nm in ('std::map', 'std::unordered_map', 'std::set', 'std::unordered_set'):
        return ('%s.Items()' % rv, None, 'map')
    if nm == 'std::span':
        return ('%s.n' % rv, '%s.a[%s.o + %s]' % (rv, rv, iv), 'ref')
    return todo(fn, 'range-for over ' + repr(rt), n)


def std_enumerator(fn, rd, n):
    nm = rd.get('name')
    t = C.parse(tstr(rd.get('type'))) if rd.get('type') else None
    m = {'binary': '%s.Ios.binary', 'in': '%s.Ios.@in', 'out': '%s.Ios.@out', 'app': '%s.Ios.app', 'trunc': '%s.Ios.trunc',
         'ate': '%s.Ios.ate', 'beg': '%s.Ios.beg', 'cur': '%s.Ios.cur', 'end': '%s.Ios.end', 'fixed': '%s.Ios.@fixed',
         'scientific': '%s.Ios.scientific'}
    if nm in m:
        return E(m[nm] % CPP, t)
    return todo(fn, 'std enumerator ' + str(nm), n)


def std_declref(fn, rd, n):
    nm = rd.get('name')
    t = C.of(n)
    kind = rd.get('kind')
    if nm == 'npos':
        return E('%s.Str.npos' % CPP, t)
    if nm in ('stdout', 'stderr', 'stdin'):
        return E('%s.%s_' % (R, nm), t)
    if kind in FUNC_KINDS and nm in ('toupper', 'tolower', 'isspace', 'isdigit', 'isalpha', 'isalnum', 'isupper',
                                     'islower', 'ispunct', 'isprint', 'isxdigit'):
        # a <cctype> function passed as a value (std::transform(s.begin(), s.end(), s.begin(), ::toupper)): over
        # the chars of a string
        return E('(global::System.Func<byte, byte>)(c__ => (byte)%s.%s((int)c__))' % (R, nm), t)
    if kind in FUNC_KINDS:
        # a library function used as a value (callee of a call is handled in call())
        return E('__libfn:' + nm, t)
    if nm == 'nullopt':
        return E('null', t)
    if nm in ('cout', 'cerr', 'clog'):
        return E('%s.Streams.%s' % (CPP, nm), t)
    if nm == 'errno':
        return E('%s.errno_' % R, t, True)
    return todo(fn, 'std name ' + str(nm), n)


def std_member(fn, n, base_n):
    nm = n.get('name')
    bt = C.of(base_n).strip()
    t = C.of(n)
    b = fn.x(base_n)
    if isinstance(bt, C.Ptr):
        bt = bt.t.strip()
    sn = std_name(bt)
    if sn == 'std::pair' and nm in ('first', 'second'):
        return E('%s.%s' % (fn.obj(b), nm), t, True)
    if sn in ('std::from_chars_result',) and nm in ('ptr', 'ec'):
        return E('%s.%s' % (fn.obj(b), nm), t, True)
    if nm in ('tm_year', 'tm_mon', 'tm_mday', 'tm_hour', 'tm_min', 'tm_sec', 'tm_wday', 'tm_yday', 'tm_isdst'):
        return E('%s.%s' % (fn.obj(b), nm), t, True)
    return todo(fn, 'std member %s.%s' % (sn or repr(bt), nm), n)


# ---------------------------------------------------------------------------------------------- calls
def params_of(decl):
    return [p for p in kids(decl) if p.get('kind') == 'ParmVarDecl']


def default_arg(fn, param, n):
    init = [c for c in kids(param)]
    if not init:
        return todo(fn, 'default argument with no expression', n)
    return fn.x(init[0])


def box_of(fn, node, t):
    """A Cpp.Box<T> for binding a reference member to the lvalue node (of type t)."""
    cs_t = fn.cs.cs(t)
    e = fn.x(node)
    if e.code.endswith('.V'):
        return e.code[:-2]                       # already a reference member: the same place
    x = strip(node)
    while x.get('kind') in ('ImplicitCastExpr', 'ParenExpr') and x.get('castKind') in (None, 'NoOp', 'LValueToRValue') and kids(x):
        x = strip(kids(x)[0])
    if x.get('kind') == 'MemberExpr':
        base = kids(x)[0]
        b = fn.x(base)
        if b.code == 'this' or base.get('implicit'):
            return '%s.Box<%s>.Of(() => ref %s)' % (CPP, cs_t, e.code)
        tv = fn.tmp('bx')
        fn.pre.append('var %s = %s;' % (tv, b.code))
        return '%s.Box<%s>.Of(() => ref %s.%s)' % (CPP, cs_t, tv, e.code.split('.')[-1])
    if x.get('kind') == 'DeclRefExpr':
        rd = x.get('referencedDecl') or {}
        if rd.get('key') in fn.m.vars:
            return '%s.Box<%s>.Of(() => ref %s)' % (CPP, cs_t, e.code)
    if x.get('kind') in ('CXXOperatorCallExpr', 'ArraySubscriptExpr'):
        return todo(fn, 'reference member bound to an element', node)
    return todo(fn, 'reference member bound to a local', node)


def is_ptr_valued(fn, node):
    """node's C# value is already a Cpp.Ptr<T>: an array pointer, v.data(), or arithmetic on one of those."""
    x = strip(node)
    while x.get('kind') in ('ImplicitCastExpr', 'ParenExpr') and x.get('castKind') in (None, 'NoOp', 'LValueToRValue') and kids(x):
        x = strip(kids(x)[0])
    if x.get('_arrptr'):
        return True
    if x.get('kind') == 'CXXMemberCallExpr':
        me = strip(kids(x)[0])
        return me.get('name') == 'data' and bool(kids(me)) and std_name(C.of(kids(me)[0]).strip()) == 'std::vector'
    if x.get('kind') == 'BinaryOperator' and x.get('opcode') in ('+', '-') and len(kids(x)) == 2:
        return is_ptr_valued(fn, kids(x)[0]) or is_ptr_valued(fn, kids(x)[1])
    if x.get('kind') == 'ImplicitCastExpr' and x.get('castKind') == 'ArrayToPointerDecay':
        return True
    return False


def ptr_to_ref(fn, node, e):
    """An array-pointer valued expression where one object (a reference) is wanted: the element it points at
    (c ? p + i : nullptr becomes c ? (p + i).Deref : null). None when node is not array-pointer valued."""
    x = strip(node)
    if x.get('kind') == 'ConditionalOperator':
        c, a, b = kids(x)
        ea, eb = fn.x(a), fn.x(b)
        ra = ptr_to_ref(fn, a, ea) if is_ptr_valued(fn, a) else (None if not is_null(b) else None)
        rb = ptr_to_ref(fn, b, eb) if is_ptr_valued(fn, b) else None
        if ra is None and rb is None:
            return None
        ra = ra if ra is not None else ('null' if is_null(a) else ea.code)
        rb = rb if rb is not None else ('null' if is_null(b) else eb.code)
        return '(%s ? %s : %s)' % (fn.cond(c), ra, rb)
    if is_ptr_valued(fn, node):
        return '(%s).Deref' % e.code
    return None


def is_null(n):
    x = strip(n)
    while x.get('kind') in ('ImplicitCastExpr',) and kids(x):
        if x.get('castKind') == 'NullToPointer':
            return True
        x = strip(kids(x)[0])
    return x.get('kind') in ('CXXNullPtrLiteralExpr', 'GNUNullExpr')


def as_arrptr(fn, node, e, elem):
    """An object pointer expression as a Cpp.Ptr<T> (for an array-pointer parameter or local)."""
    if fn.arrptr_ref(node) or e.code.startswith('new %s.Ptr<' % CPP) or e.code.startswith('default('):
        return e.code
    if is_ptr_valued(fn, node):
        return e.code
    cs_el = fn.cs.cs(elem, 'elem')
    x = strip(node)
    while x.get('kind') in ('ImplicitCastExpr', 'ParenExpr', 'CXXStaticCastExpr', 'CStyleCastExpr') and kids(x):
        if x.get('castKind') == 'NullToPointer':
            return 'default(%s.Ptr<%s>)' % (CPP, cs_el)
        x = strip(kids(x)[0])
    if x.get('kind') in ('CXXNullPtrLiteralExpr', 'GNUNullExpr'):
        return 'default(%s.Ptr<%s>)' % (CPP, cs_el)
    if x.get('kind') == 'CXXMemberCallExpr':
        me = strip(kids(x)[0])
        if me.get('name') == 'data' and kids(me) and std_name(C.of(kids(me)[0]).strip()) == 'std::vector':
            return 'new %s.Ptr<%s>(%s.a, 0)' % (CPP, cs_el, fn.obj(fn.x(kids(me)[0])))
    if x.get('kind') == 'UnaryOperator' and x.get('opcode') == '&':
        inner = strip(kids(x)[0])
        if inner.get('kind') == 'CXXOperatorCallExpr' and op_name(inner) == 'operator[]':
            obj, idx = kids(inner)[1], kids(inner)[2]
            on = std_name(C.of(obj).strip())
            if on == 'std::vector':
                return 'new %s.Ptr<%s>(%s.a, (long)(%s))' % (CPP, cs_el, fn.obj(fn.x(obj)), fn.x(idx).code)
            if on == 'std::array':
                return 'new %s.Ptr<%s>(%s, (long)(%s))' % (CPP, cs_el, fn.x(obj).code, fn.x(idx).code)
        if inner.get('kind') == 'ArraySubscriptExpr':
            base, idx = kids(inner)
            bt = C.of(base).strip()
            if isinstance(bt, C.Arr) or (isinstance(strip(base), dict) and strip(base).get('castKind') == 'ArrayToPointerDecay'):
                b0 = strip(base)
                arr = kids(b0)[0] if b0.get('castKind') == 'ArrayToPointerDecay' else base
                return 'new %s.Ptr<%s>(%s, (long)(%s))' % (CPP, cs_el, fn.x(arr).code, fn.x(idx).code)
    # one object: a one-element array holding it (writes through the pointer reach the object's fields)
    return 'new %s.Ptr<%s>(new %s[] { %s }, 0)' % (CPP, cs_el, cs_el, e.code)


def pass_arg(fn, pt, a, e, n):
    """An argument for a parameter of C++ type pt."""
    ptt = pt.strip() if isinstance(pt, C.Type) else None
    if std_name(ptt) == 'std::function' and isinstance(C.of(a).strip(), C.Closure) and \
            fn.cs.cs(C.of(a).strip()) == 'object':
        # a closure held as object (returned by a generic helper) passed as a std::function: its delegate
        return '((%s)%s)' % (fn.cs.cs(ptt), e.code)
    if isinstance(pt, (C.LRef, C.RRef)) and fn.cs.by_ref(pt.t) and not (pt.t.const and isinstance(pt.t, (C.Builtin, C.Ptr))):
        if pt.t.const and isinstance(pt.t, C.Rec) and not fn.cs.cs(pt).startswith('ref '):
            return e.code
        if e.lv is True:
            return 'ref ' + e.code
        if isinstance(pt, C.RRef) or pt.t.const:
            tv = fn.tmp('a')
            fn.pre.append('var %s = %s;' % (tv, e.code))
            return 'ref ' + tv
        return todo(fn, 'non-lvalue passed by reference', n)
    return e.code


def call_args(fn, decl, arg_nodes, n):
    """Arguments for an engine function or method decl."""
    ps = params_of(decl)
    arrp = fn.cs.arrptr_params(decl) if decl.get('key') else set()
    out = []
    for i, a in enumerate(arg_nodes):
        p = ps[i] if i < len(ps) else None
        if a.get('kind') == 'CXXDefaultArgExpr':
            if p is None:
                return todo(fn, 'default argument past the parameters', n)
            e = default_arg(fn, p, n)
        else:
            e = fn.x(a)
        if p is None:
            out.append(e.code)
            continue
        pt0 = C.parse(tstr(p.get('type')))
        if i in arrp and isinstance(pt0, C.Ptr):
            out.append(as_arrptr(fn, a, e, pt0.t))
            continue
        if fn.arrptr_ref(a) and isinstance(pt0, C.Ptr):
            out.append('%s.Deref' % fn.obj(e))       # an element of the array, where one object is wanted
            continue
        pt = C.parse(tstr(p.get('type')))
        from emit_cs import param_type
        out.append(pass_arg(fn, param_type(pt), a, e, n))
    return out


def lib_args(fn, arg_nodes):
    """Arguments for a library function: default arguments are left to the runtime's optional parameters."""
    out = []
    for a in arg_nodes:
        if a.get('kind') == 'CXXDefaultArgExpr':
            break
        out.append(fn.x(a))
    return out


def unwrap_callee(c):
    while c.get('kind') in ('ImplicitCastExpr', 'ParenExpr', 'MaterializeTemporaryExpr', 'CXXBindTemporaryExpr') and kids(c):
        c = kids(c)[0]
    return c


def callee_decl(fn, n):
    c = unwrap_callee(kids(n)[0])
    rd = c.get('referencedDecl') or {}
    k = rd.get('key')
    return fn.m.funcs.get(k) if k else None, rd


def call(fn, n):
    t = C.of(n)
    callee = kids(n)[0]
    args = kids(n)[1:]
    decl, rd = callee_decl(fn, n)
    if decl is not None:
        a = call_args(fn, decl, args, n)
        if decl.get('parent') and decl.get('storageClass') == 'static':
            return E('%s.%s(%s)' % (fn.cs.rec_name(decl['parent']), fn.cs.fn_name(decl), ', '.join(a)), t,
                     ret_lv(fn, decl))
        return E('%s(%s)' % (fn.cs.fn_name(decl), ', '.join(a)), t, ret_lv(fn, decl))
    nm = rd.get('name')
    if nm:
        import std_calls
        return std_calls.free(fn, nm, rd, args, n)
    # a call through a function pointer or a std::function value
    ce = fn.x(callee)
    a = [fn.x(x).code for x in args]
    return E('%s(%s)' % (fn.obj(ce), ', '.join(a)), t)


def ret_lv(fn, decl):
    ft = C.parse(tstr(decl.get('type')))
    if isinstance(ft, C.Func) and isinstance(ft.ret, C.LRef) and fn.cs.by_ref(ft.ret.t) and not ft.ret.t.const:
        return True
    if isinstance(ft, C.Func) and isinstance(ft.ret, C.LRef):
        return True     # a reference to an object: the object
    return False


def member_call(fn, n):
    t = C.of(n)
    me = kids(n)[0]
    args = kids(n)[1:]
    if me.get('kind') not in ('MemberExpr',):
        me2 = strip(me)
        if me2.get('kind') == 'MemberExpr':
            me = me2
        else:
            return todo(fn, 'member call through ' + me.get('kind', '?'), n)
    mk = me.get('referencedMemberDecl')
    obj_n = kids(me)[0]
    if (mk and mk in fn.m.funcs and fn.m.funcs[mk].get('name') == 'operator=' and
            (fn.m.funcs[mk].get('isImplicit') or fn.m.funcs[mk].get('explicitlyDefaulted') or
             body_of(fn.m.funcs[mk]) is None)) or (mk and mk not in fn.m.funcs and me.get('name') == 'operator='):
        # an implicit or defaulted assignment operator of an engine class: the class's Assign / MoveFrom
        ob = fn.x(obj_n)
        src = args[0] if args else None
        e = fn.x(src)
        ps = params_of(fn.m.funcs[mk]) if mk in fn.m.funcs else []
        moving = bool(ps) and isinstance(C.parse(tstr(ps[0].get('type'))), C.RRef)
        return E('%s.%s(%s)' % (fn.obj(ob), 'MoveFrom' if moving else 'Assign', e.code), t)
    if mk and mk in fn.m.funcs:
        decl = fn.m.funcs[mk]
        a = call_args(fn, decl, args, n)
        ob = fn.x(obj_n)
        name = fn.cs.fn_name(decl)
        if decl.get('kind') == 'CXXConversionDecl':
            name = fn.cs.fn_name(decl)
        if decl.get('storageClass') == 'static':
            return E('%s.%s(%s)' % (fn.cs.rec_name(decl['parent']), name, ', '.join(a)), t, ret_lv(fn, decl))
        if ob.code == 'this' and obj_n.get('implicit'):
            if is_base_qualified(fn, decl):
                return E('base.%s(%s)' % (name, ', '.join(a)), t, ret_lv(fn, decl))
            return E('%s(%s)' % (name, ', '.join(a)), t, ret_lv(fn, decl))
        return E('%s.%s(%s)' % (fn.obj(ob), name, ', '.join(a)), t, ret_lv(fn, decl))
    import std_calls
    return std_calls.method(fn, me, obj_n, args, n)


def is_base_qualified(fn, decl):
    """A call Base::m() from a class that overrides m: clang names Base::m only when qualified."""
    if not fn.rec or decl.get('parent') == fn.rec or not decl.get('virtual'):
        return False
    nm = decl.get('name')
    for mk in fn.m.methods.get(fn.rec, []):
        if fn.m.d[mk].get('name') == nm:
            return True
    return False


def operator_call(fn, n):
    t = C.of(n)
    args = kids(n)[1:]
    decl, rd = callee_decl(fn, n)
    nm = rd.get('name')
    if decl is not None:
        if decl.get('parent') and decl['kind'] in ('CXXMethodDecl', 'CXXConversionDecl'):
            ob = fn.x(args[0])
            if (decl.get('isImplicit') or decl.get('explicitlyDefaulted')) and nm == 'operator=':
                return implicit_assign(fn, decl, ob, args[1], t, n)
            a = call_args(fn, decl, args[1:], n)
            return E('%s.%s(%s)' % (fn.obj(ob), fn.cs.fn_name(decl), ', '.join(a)), t, ret_lv(fn, decl))
        a = call_args(fn, decl, args, n)
        return E('%s(%s)' % (fn.cs.fn_name(decl), ', '.join(a)), t, ret_lv(fn, decl))
    import std_calls
    return std_calls.operator(fn, nm, rd, args, n)


def implicit_assign(fn, decl, ob, src_n, t, n):
    """The implicit copy or move assignment of an engine class."""
    ps = params_of(decl)
    pt = C.parse(tstr(ps[0].get('type'))) if ps else None
    se = fn.x(src_n)
    if isinstance(pt, C.RRef):
        if src_n.get('valueCategory') == 'prvalue' or strip(src_n).get('kind') in ('CXXConstructExpr', 'CallExpr', 'CXXTemporaryObjectExpr', 'InitListExpr'):
            return E('%s.MoveFrom(%s)' % (fn.obj(ob), se.code), t)
        return E('%s.MoveFrom(%s)' % (fn.obj(ob), se.code), t)
    return E('%s.Assign(%s)' % (fn.obj(ob), se.code), t)


# ---------------------------------------------------------------------------------------------- construction
def sig_params(s):
    try:
        f = C.parse(s)
    except Exception:
        return None
    return f.params if isinstance(f, C.Func) else None


def same_shape(a, b):
    """Two parameter types alike up to namespaces (ctorType prints names relative to the scope)."""
    def norm(t):
        r = repr(t)
        r = re.sub(r'\b[A-Za-z_][A-Za-z_0-9]*::', '', r)
        r = r.replace('basic_string<char>', 'string').replace('basic_string_view<char>', 'string_view')
        return re.sub(r'\s+', ' ', r)
    return norm(a) == norm(b)


def find_ctor(fn, rk, ctor_type):
    ps = sig_params(tstr(ctor_type))
    cands = []
    for c in fn.m.ctors(rk):
        cps = [C.parse(tstr(p.get('type'))) for p in params_of(c)]
        if ps is not None and len(cps) == len(ps) and all(same_shape(x, y) for x, y in zip(cps, ps)):
            cands.append(c)
    if len(cands) == 1:
        return cands[0]
    if ps is not None:
        same_n = [c for c in fn.m.ctors(rk) if len(params_of(c)) == len(ps)]
        if len(same_n) == 1:
            return same_n[0]
    return cands[0] if cands else None


def construct(fn, n):
    t = C.of(n).strip()
    args = kids(n)
    k = fn.m.key_of_type(t)
    if k in fn.m.records:
        return construct_engine(fn, n, k, t, args)
    if isinstance(t, C.Arr) and not args:
        # T a[n] (or a[n][m]) of a class: each element default-constructed
        if isinstance(t.t, C.Arr) and isinstance(t.t.t, C.Builtin) and t.t.t.name == 'char':
            return E('%s.R.Bufs(%d, %d)' % (CPP, t.n, t.t.n), t)
        if isinstance(t.t, C.Arr):
            return E('%s.Arr.Make2<%s>(%d, %d)' % (CPP, fn.cs.cs(t.t.t, 'elem'), t.n, t.t.n), t)
        return E('%s.Arr.Make<%s>(%d)' % (CPP, fn.cs.cs(t.t, 'elem'), t.n), t)
    if isinstance(t, C.Arr):
        return todo(fn, 'construct array', n)
    import std_calls
    return std_calls.construct(fn, t, n, args)


def construct_engine(fn, n, rk, t, args):
    ps = sig_params(tstr(n.get('ctorType')))
    cls = fn.cs.rec_name(rk)
    if ps is not None and len(ps) == 1 and isinstance(ps[0], (C.LRef, C.RRef)) and same_shape(ps[0].t.strip(), t) \
            and args and args[0].get('kind') != 'CXXDefaultArgExpr':
        a0 = args[0]
        e = fn.x(a0)
        if isinstance(ps[0], C.RRef):
            if not e.lv or strip(a0).get('kind') in ('CXXConstructExpr', 'CallExpr', 'CXXTemporaryObjectExpr', 'InitListExpr', 'CXXMemberCallExpr', 'CXXOperatorCallExpr'):
                if strip(a0).get('kind') == 'CallExpr' and is_std_move(strip(a0)):
                    return E('%s.MoveNew(%s)' % (cls, e.code), t)
                return E(e.code, t)          # a temporary: it is the new object
            return E('%s.MoveNew(%s)' % (cls, e.code), t)
        if not e.lv:
            return E(e.code, t)              # copying a temporary is the temporary
        return E('%s.Clone()' % fn.obj(e), t)
    if not ps:
        return E('new %s()' % cls, t)
    c = find_ctor(fn, rk, n.get('ctorType'))
    if c is None:
        return todo(fn, 'no matching constructor for %s' % rk, n)
    from cs_record import RecordEmitter
    nm = RecordEmitter(fn.cs).ctor_name(c)
    a = call_args(fn, c, args, n)
    return E('%s.New%s(%s)' % (cls, '' if nm == 'd' else '_' + nm, ', '.join(a)), t)


def is_std_move(call_n):
    # clang 18 makes std::move a builtin: its callee is reached through a BuiltinFnToFnPtr cast
    c = unwrap_callee(kids(call_n)[0]) if kids(call_n) else {}
    return (c.get('referencedDecl') or {}).get('name') in ('move', 'forward')


def delegating_ctor(fn, rk, init):
    c = find_ctor(fn, rk, init.get('ctorType'))
    if c is None:
        return todo(fn, 'delegating constructor', init)
    from cs_record import RecordEmitter
    nm = RecordEmitter(fn.cs).ctor_name(c)
    a = call_args(fn, c, kids(init), init)
    return '__init_%s(%s)' % (nm, ', '.join(a))


def base_ctor_call(fn, base, init):
    if init is None:
        return 'base.__init_d()'
    init = strip(init)
    if init.get('kind') not in ('CXXConstructExpr',):
        return todo(fn, 'base initializer ' + init.get('kind', '?'), init)
    ps = sig_params(tstr(init.get('ctorType')))
    if not ps:
        return 'base.__init_d()'
    c = find_ctor(fn, base, init.get('ctorType'))
    if c is None:
        return todo(fn, 'base constructor of ' + base, init)
    from cs_record import RecordEmitter
    nm = RecordEmitter(fn.cs).ctor_name(c)
    a = call_args(fn, c, kids(init), init)
    return 'base.__init_%s(%s)' % (nm, ', '.join(a))   # base.: the derived class has an __init_ of that name too


def init_list(fn, n):
    t = C.of(n).strip()
    items = kids(n)
    if isinstance(t, C.Arr):
        el = t.t
        vals = []
        for i, c in enumerate(items):
            if c.get('kind') in ('ImplicitValueInitExpr',):
                vals.append(fn.cs.default_value(el))
            elif c.get('kind') == 'ArrayFiller':
                continue
            else:
                vals.append(fn.value_for(el, fn.x(c), c))
        if isinstance(el, C.Builtin) and el.name == 'char':
            # char buf[N] = {...}: bytes, zero-filled to N
            vals_b = [v for v in vals]
            return E('%s.BufOf(%d, new int[] { %s })' % (R, t.n or len(vals_b), ', '.join(vals_b)), t)
        while t.n is not None and len(vals) < t.n:
            vals.append(fn.cs.default_value(el))
        if isinstance(el, C.Ptr) and isinstance(el.t, C.Rec) and fn.m.key_of_type(el.t) in fn.m.records and \
                items and all(is_ptr_valued(fn, c) for c in items if c.get('kind') not in ('ImplicitValueInitExpr', 'ArrayFiller')):
            # {arr1, arr2, ...} of arrays of objects: a list of array pointers
            return E('new %s.Ptr<%s>[] { %s }' % (CPP, fn.cs.cs(el.t, 'elem'), ', '.join(vals)), t)
        return E('new %s[] { %s }' % (fn.cs.cs(el, 'elem'), ', '.join(vals)), t)
    k = fn.m.key_of_type(t)
    if k in fn.m.records:
        # aggregate initialization: default-construct, then set the fields given
        fields = [f for f in fn.m.records[k].get('fields', [])]
        sets = []
        for f, c in zip(fields, items):
            if c.get('kind') in ('CXXDefaultInitExpr', 'ImplicitValueInitExpr'):
                continue
            ft = C.parse(tstr(f.get('type')))
            if isinstance(ft, C.LRef) and fn.cs.by_ref(ft.t):
                sets.append('%s = %s' % (fn.cs.field_name(f.get('key'), f.get('name')), box_of(fn, c, ft.t)))
                continue
            v = fn.value_for(ft, fn.x(c), c)
            sets.append('%s = %s' % (fn.cs.field_name(f.get('key'), f.get('name')), v))
        if not sets:
            return E('new %s()' % fn.cs.rec_name(k), t)
        return E('new %s() { %s }' % (fn.cs.rec_name(k), ', '.join(sets)), t)
    if isinstance(t, C.Builtin) or (isinstance(t, C.Ptr)) or is_enum_t(fn, t):
        if not items:
            return E(fn.cs.default_value(t), t)
        return fn.x(items[0])
    sn = std_name(t)
    if sn == 'std::array':
        # std::array's one field is the C array; its list is the elements
        if len(items) == 1 and strip(items[0]).get('kind') == 'InitListExpr':
            inner = fn.x(strip(items[0]))
            return E(inner.code, t)
        el = t.args[0]
        if len(items) == 1 and strip(items[0]).get('kind') == 'ImplicitValueInitExpr' and \
                tstr(items[0].get('type')).rstrip().endswith(']'):
            items = []                      # array<T, N> x{}: the C array member value-initialized, all zero
        cnt = int(re.sub(r'[^0-9]', '', str(t.args[1].v)))
        vals = [fn.value_for(el, fn.x(c), c) for c in items]
        while len(vals) < cnt:
            vals.append(fn.cs.default_value(el))
        return E('new %s[] { %s }' % (fn.cs.cs(el, 'elem'), ', '.join(vals)), t)
    if sn == 'std::pair' and len(items) == 2:
        a = [fn.value_for(t.args[i], fn.x(c), c) for i, c in enumerate(items)]
        return E('new %s.Pair<%s, %s>(%s, %s)' % (CPP, fn.cs.cs(t.args[0], 'elem'), fn.cs.cs(t.args[1], 'elem'), a[0], a[1]), t)
    if not items:
        return E(fn.cs.default_value(t), t)
    return todo(fn, 'list initialization of ' + (sn or repr(t)), n)


def lambda_expr(fn, n):
    """A C++ lambda as a C# lambda: by-copy captures are copied into fresh locals before the statement. A generic
    lambda (auto parameters) used with several argument types becomes an object[] of one C# lambda per
    instantiation; a call picks the instantiation by its operator() signature."""
    ch = kids(n)
    rec = ch[0]
    calls = [c for c in kids(rec) if c.get('kind') == 'CXXMethodDecl' and c.get('name') == 'operator()']
    generic = False
    if not calls:
        tmpl = [c for c in kids(rec) if c.get('kind') == 'FunctionTemplateDecl' and c.get('name') == 'operator()']
        if tmpl:
            specs = [c for c in kids(tmpl[0]) if c.get('kind') == 'CXXMethodDecl']
            calls = specs[1:]
            if not calls:
                return todo(fn, 'generic lambda never called', n)
            generic = len(calls) > 1
        else:
            return todo(fn, 'lambda without operator()', n)
    fields = [c for c in kids(rec) if c.get('kind') == 'FieldDecl']
    inits = [c for c in ch[1:] if c.get('kind') != 'CompoundStmt']
    saved = {}
    for f, init in zip(fields, inits):
        ft = C.parse(tstr(f.get('type')))
        if isinstance(ft, (C.LRef, C.RRef)):
            continue
        src = strip(init)
        if src.get('kind') == 'CXXThisExpr' or isinstance(ft, C.Ptr) and src.get('kind') == 'CXXThisExpr':
            continue
        ref = src.get('referencedDecl') if src.get('kind') == 'DeclRefExpr' else None
        tv = fn.tmp('cap')
        v = fn.value_for(ft, fn.x(init), init)
        fn.pre.append('var %s = %s;' % (tv, v))
        if ref and ref.get('key'):
            saved[ref['key']] = fn.names.get(ref['key'])
            fn.names[ref['key']] = tv
    try:
        codes = [lambda_code(fn, op) for op in calls]
    finally:
        for k2, v2 in saved.items():
            if v2 is None:
                fn.names.pop(k2, None)
            else:
                fn.names[k2] = v2
    fn.lambda_decls = getattr(fn, 'lambda_decls', {})
    if not generic:
        fn.cs.lambdas[tstr(n.get('type'))] = calls[0]
        return E(codes[0], C.of(n))
    fn.cs.lambdas[tstr(n.get('type'))] = ('generic', [(tstr(op.get('type')), i, op) for i, op in enumerate(calls)])
    return E('new object[] { %s }' % ', '.join(codes), C.of(n))


def lambda_code(fn, op):
    """One C# lambda for the lambda's call operator op, cast to its delegate type."""
    from cs_func import Fn
    from emit_cs import param_type
    sub = Fn(fn.cs, op, fn.rec, is_lambda=True, parent=fn)
    ps = params_of(op)
    ft = C.parse(tstr(op.get('type')))
    ret = ft.ret if isinstance(ft, C.Func) else C.Builtin('void')
    if fn.unresolved(ret):
        # [&](int k) { return v[k]; }: clang spells the deduced return type by the container's member typedef
        # (value_type); the type of what is returned says what it is
        def first_return(x):
            for c in kids(x):
                if c.get('kind') == 'LambdaExpr':
                    continue
                if c.get('kind') == 'ReturnStmt' and kids(c):
                    return kids(c)[0]
                r = first_return(c)
                if r is not None:
                    return r
            return None
        rx = first_return(op)
        if rx is not None:
            try:
                rt = fn.T(rx)
                rt = rt.strip() if isinstance(rt, (C.LRef, C.RRef)) else rt
                if rt is not None and not fn.unresolved(rt):
                    ret = rt
            except Exception:
                pass
    sub.ret_t = ret
    plist = []
    for p in ps:
        pt = C.parse(tstr(p.get('type')))
        nm = sub.declare(p.get('local'), p.get('name') or 'p', pt)
        cst = fn.cs.cs(param_type(pt), 'param')
        if cst.startswith('ref '):
            sub.refs.add(p.get('local'))
        plist.append('%s %s' % (cst, nm))
    body = [c for c in kids(op) if c.get('kind') == 'CompoundStmt']
    lines = sub.stmt(body[0], 0) if body else ['{ }']
    dt = fn.cs.delegate(C.Func(ret, [param_type(C.parse(tstr(p.get('type')))) for p in ps]))
    return '(%s)((%s) =>\n%s)' % (dt, ', '.join(plist), '\n'.join(lines))


def new_expr(fn, n):
    t = C.of(n)
    inner = [c for c in kids(n)]
    pt = t.strip().t if isinstance(t.strip(), C.Ptr) else None
    if n.get('isArray'):
        cnt = fn.x(inner[0]).code if inner else '0'
        return E('%s.Arr.Make<%s>((int)(%s))' % (CPP, fn.cs.cs(pt, 'elem'), cnt), t)
    if inner:
        e = fn.x(inner[-1])
        return E(e.code, t)
    return E(fn.cs.default_value(pt), t)


def delete_expr(fn, n):
    e = fn.x(kids(n)[0])
    return E('%s.Mv.Delete(%s)' % (CPP, e.code), C.of(n))


def sizeof_std(t):
    n = base_name(t)
    return {'std::complex': 16}.get(n)
