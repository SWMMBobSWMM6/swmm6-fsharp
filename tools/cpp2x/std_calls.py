"""Standard-library and C-library calls for the C# back end: free functions, methods of std types, operators on
std types, and construction of std types. Grown from the census of the engine's reachable code.
Written by Claude (Anthropic); see AI-USE.md."""
import re

import ctypes as C
from model import kids, tstr
from names import ident
from std_cs import E, todo, strip, std_name, base_name, CPP, R, lib_args, is_std_move, pass_arg, params_of, unwrap_callee

MATH1 = {'sqrt': 'global::System.Math.Sqrt', 'fabs': 'global::System.Math.Abs', 'floor': 'global::System.Math.Floor',
         'ceil': 'global::System.Math.Ceiling', 'trunc': 'global::System.Math.Truncate', 'exp': 'global::System.Math.Exp',
         'log': 'global::System.Math.Log', 'log10': 'global::System.Math.Log10', 'sin': 'global::System.Math.Sin',
         'cos': 'global::System.Math.Cos', 'tan': 'global::System.Math.Tan', 'asin': 'global::System.Math.Asin',
         'acos': 'global::System.Math.Acos', 'atan': 'global::System.Math.Atan', 'sinh': 'global::System.Math.Sinh',
         'cosh': 'global::System.Math.Cosh', 'tanh': 'global::System.Math.Tanh', 'cbrt': 'global::System.Math.Cbrt',
         'log2': 'global::System.Math.Log2', 'round': CPP + '.LibM.round', 'expm1': CPP + '.LibM.expm1',
         'log1p': CPP + '.LibM.log1p', 'nearbyint': CPP + '.LibM.nearbyint', 'rint': CPP + '.LibM.rint',
         'asinh': 'global::System.Math.Asinh', 'acosh': 'global::System.Math.Acosh', 'atanh': 'global::System.Math.Atanh',
         'erf': CPP + '.LibM.erf', 'erfc': CPP + '.LibM.erfc', 'tgamma': CPP + '.LibM.tgamma', 'lgamma': CPP + '.LibM.lgamma',
         'exp2': CPP + '.LibM.exp2'}
MATH2 = {'pow': 'global::System.Math.Pow', 'atan2': 'global::System.Math.Atan2', 'hypot': CPP + '.LibM.hypot',
         'fmod': CPP + '.LibM.fmod', 'fmax': CPP + '.LibM.fmax', 'fmin': CPP + '.LibM.fmin',
         'copysign': 'global::System.Math.CopySign', 'remainder': CPP + '.LibM.remainder', 'fdim': CPP + '.LibM.fdim',
         'nextafter': CPP + '.LibM.nextafter'}


def ecodes(fn, args):
    return [e.code for e in lib_args(fn, args)]


def tcs(fn, n):
    return fn.cs.cs(C.of(n))


def typed_ptr(fn, x):
    """An argument that C++ passes as char* through reinterpret_cast: the typed pointer it was made from."""
    s = strip(x)
    while s.get('kind') in ('ImplicitCastExpr', 'CStyleCastExpr', 'CXXStaticCastExpr') and s.get('castKind') in ('NoOp', 'BitCast') and kids(s):
        s = strip(kids(s)[0])
    if s.get('kind') == 'CXXReinterpretCastExpr' or (s.get('kind') == 'CStyleCastExpr' and s.get('castKind') == 'BitCast'):
        return fn.x(kids(s)[0])
    return fn.x(x)


LAMBDA_CAST = re.compile(r'^\(global::Gen\.FN\d+\)\(')


def call_of(f, args):
    """Calling the callable code f (a lambda literal or a delegate) with args."""
    return '(%s)(%s)' % (f, ', '.join(args))


def algo_fn_arg(fn, x, code):
    """A predicate or operation handed to a runtime algorithm (Func<...>): a lambda without its delegate cast, so
    C# converts it to the Func the algorithm takes; another delegate through its Invoke method."""
    m = LAMBDA_CAST.match(code)
    if m and code.endswith(')'):
        from cs_func import match_close
        if match_close(code, m.end() - 1) == len(code) - 1:
            return code[m.end():-1]
    xt = C.of(x).strip()
    if isinstance(xt, C.Closure) or std_name(xt) == 'std::function':
        if code.startswith('(') or code.replace('.', '').replace('_', '').isalnum():
            if fn.cs.cs(xt) != 'object':
                return '%s.Invoke' % code
    return code


def ref_variable(x):
    """Whether expression x names a variable declared as an lvalue reference (a T& parameter or local), which the C#
    holds as a variable of its own, not as the place it refers to."""
    x = strip(x)
    if x.get('kind') != 'DeclRefExpr':
        return False
    d = x.get('referencedDecl') or {}
    return d.get('kind') in ('ParmVarDecl', 'VarDecl') and isinstance(C.parse(tstr(d.get('type'))), C.LRef)


def free(fn, nm, rd, args, n):
    t = C.of(n)
    # functions whose pointer arguments are handled here, before any generic translation (a generic &x would box
    # x and copy the box back after the call, undoing what the function wrote)
    if nm in ('sscanf', 'fscanf'):
        a2 = [fn.x(x) for x in args[:2]]
        return printf_like(fn, nm, a2 + [None] * (len(args) - 2), args, n)
    if nm in ('fwrite', 'fread'):
        return fwrite_like(fn, nm, None, args, n)
    if nm in ('strtod', 'strtol', 'strtoul', 'strtoll') and len(args) >= 2:
        endn = strip(args[1])
        if endn.get('kind') == 'UnaryOperator' and endn.get('opcode') == '&':
            s0 = fn.x(args[0])
            ee = fn.x(kids(endn)[0])
            rest = [fn.x(x).code for x in args[2:] if x.get('kind') != 'CXXDefaultArgExpr']
            return E('%s.%s(%s, ref %s%s)' % (R, 'strtol' if nm != 'strtod' else 'strtod', s0.code, ee.code,
                                              ''.join(', ' + x for x in rest)), t)
    if nm in ('memcpy', '__builtin_memcpy', 'memmove', '__builtin_memmove'):
        c3 = [typed_ptr(fn, args[0]).code, typed_ptr(fn, args[1]).code, fn.x(args[2]).code]
        return E('%s.Mem.memcpy(%s)' % (CPP, ', '.join(c3)), t)
    a = lib_args(fn, args)
    c = [e.code for e in a]
    ts = [C.of(x).strip() for x in args[:len(a)]]
    rt = fn.cs.cs(t) if not isinstance(t, C.Builtin) or t.name != 'void' else 'void'
    if nm == 'call_once' and len(c) >= 2:
        f_ = algo_fn_arg(fn, args[1], c[1])
        rest = c[2:]
        return E('%s.Std.call_once(%s, () => ((%s)(%s))(%s))' % (CPP, c[0], 'global::System.Delegate', c[1], ', '.join(rest))
                 if False else '%s.Std.call_once(%s, %s)' % (CPP, c[0], 'new global::System.Action(() => ' + call_of(c[1], rest) + ')'), t)
    if nm == 'length' and len(c) == 1 and any(w in (rd.get('key') or '') + tstr(rd.get('type')) for w in ('char_traits', 'char_type')):
        # std::char_traits<char>::length(s): clang names it only by its type, size_t (const char_type *)
        return E('%s.strlen(%s)' % (R, c[0]), t)
    if nm == 'classic' and not c and 'locale' in (rd.get('key') or '') + tstr(rd.get('type')) + repr(t):
        return E('null', t)
    if nm == 'hardware_concurrency' and not c:
        return E('%s.Sys.hardware_concurrency()' % CPP, t)
    if nm == 'now' and not c and any(w in (rd.get('key') or '') + tstr(rd.get('type')) + repr(t)
                                     for w in ('chrono', 'time_point')):
        return E('%s.TimePoint.now()' % CPP, t)
    if nm == 'current_exception' and not c:
        caught = getattr(fn, 'caught', None) or (getattr(fn.parent, 'caught', None) if fn.parent else None)
        return E(caught[-1] if caught else 'null', t)
    if nm == 'rethrow_exception' and len(c) == 1:
        return E('%s.Std.Rethrow(%s)' % (CPP, c[0]), t)
    if nm == 'make_exception_ptr' and len(c) == 1:
        return E(c[0], t)
    if nm == 'free' and len(c) == 1:
        return E('%s.Mem.Free(%s)' % (CPP, c[0]), t)
    fs_args = any(std_name(C.of(x).strip()) in ('std::filesystem::path', 'std::filesystem::file_status')
                  for x in args[:len(a)])
    if (fs_args or '10filesystem' in (rd.get('key') or '')) and nm in (
            'file_size', 'exists', 'is_regular_file', 'is_directory', 'remove', 'create_directories', 'current_path',
            'absolute', 'canonical', 'weakly_canonical', 'temp_directory_path', 'rename', 'equivalent', 'status',
            'symlink_status', 'create_hard_link', 'last_write_time', 'copy_file', 'is_symlink', 'remove_all'):
        # std::filesystem (dispatched on the path arguments: rename and remove are also C functions)
        return E('%s.Fs.%s(%s)' % (CPP, nm, ', '.join(c)), t)
    if not args and nm in ('quiet_NaN', 'signaling_NaN', 'infinity', 'epsilon', 'max', 'min', 'lowest', 'denorm_min',
                           'round_error', 'digits10'):
        return numeric_limits(fn, nm, t, n)
    if nm in MATH1 and len(c) == 1:
        if ts and isinstance(ts[0], C.Builtin) and ts[0].name == 'float':
            return E('((float)%s((double)%s))' % (MATH1[nm], c[0]), t)
        return E('%s(%s)' % (MATH1[nm], c[0]), t)
    if nm in MATH2 and len(c) == 2:
        return E('%s(%s, %s)' % (MATH2[nm], c[0], c[1]), t)
    if nm in ('abs', 'labs', 'llabs'):
        if isinstance(ts[0], C.Builtin) and ts[0].name in ('double', 'float'):
            return E('global::System.Math.Abs(%s)' % c[0], t)
        return E('%s.Std.iabs(%s)' % (CPP, c[0]), t)
    if nm in ('isnan', 'isinf', 'isfinite', 'signbit', 'isnormal'):
        f = {'isnan': 'double.IsNaN', 'isinf': 'double.IsInfinity', 'isfinite': 'double.IsFinite',
             'signbit': 'double.IsNegative', 'isnormal': 'double.IsNormal'}[nm]
        return E('%s(%s)' % (f, c[0]), t)
    if nm in ('lround', 'llround'):
        return E('%s.LibM.lround(%s)' % (CPP, c[0]), t)
    if nm == 'fma':
        return E('global::System.Math.FusedMultiplyAdd(%s, %s, %s)' % tuple(c), t)
    if nm in ('max', 'min') and len(c) == 2:
        return E('%s.M.%s(%s, %s)' % (CPP, nm, c[0], c[1]), t)       # a value in C# (M.min returns a copy)
    if nm in ('max', 'min') and len(c) == 3:
        return E('%s.M.%s(%s, %s, %s)' % (CPP, nm, c[0], c[1], c[2]), t)
    if nm in ('max', 'min') and len(c) == 1:
        return E('%s.M.%s(%s)' % (CPP, nm, c[0]), t)
    if nm == 'clamp':
        return E('%s.M.clamp(%s, %s, %s)' % (CPP, c[0], c[1], c[2]), t)
    if nm in ('move', 'forward', 'as_const'):
        return E(c[0], t, a[0].lv)
    if nm == 'swap':
        alias = [ref_variable(x) for x in args[:2]]
        if any(alias):
            # std::swap(output, accepted) where a C++ reference (a T& parameter, an auto& local) is a C# variable
            # holding the object: exchanging the variables would leave both referents as they were, so exchange
            # the contents, as std::swap's three moves do
            # (an immediately invoked lambda, which every back end of tools/cs2x already translates, rather than a
            # new runtime helper; each operand is written twice, so only plain names and member accesses qualify)
            et = C.of(args[0]).strip()
            k = fn.m.key_of_type(et)
            if k not in fn.m.records:
                return todo(fn, 'std::swap through a reference of ' + repr(et), n)
            if any(strip(x).get('kind') not in ('DeclRefExpr', 'MemberExpr') for x in args[:2]):
                return todo(fn, 'std::swap through a reference of an expression', n)
            dt = fn.cs.delegate(C.Func(C.Builtin('void'), []))
            return E('((%s)(() => { var __t = %s.MoveNew(%s); %s.MoveFrom(%s); %s.MoveFrom(__t); }))()'
                     % (dt, fn.cs.rec_name(k), c[0], c[0], c[1], c[1]), t)
        return E('%s.Std.swap(ref %s, ref %s)' % (CPP, c[0], c[1]), t)
    if nm == 'exchange':
        return E('%s.Std.exchange(ref %s, %s)' % (CPP, c[0], c[1]), t)
    if nm == 'to_string':
        return E('%s.Str.to_string(%s)' % (CPP, c[0]), t)
    if nm in ('stod', 'stoi', 'stol', 'stof', 'stoul', 'stoll'):
        f = {'stof': 'stod', 'stoul': 'stol', 'stoll': 'stol'}.get(nm, nm)
        call = '%s.Str.%s(%s)' % (CPP, f, ', '.join(c))
        if nm == 'stof':
            call = '((float)%s)' % call
        if nm == 'stoul':
            call = '((ulong)%s)' % call
        return E(call, t)
    if nm == 'getline':
        return E('%s.Streams.getline(%s)' % (CPP, ', '.join(c)), t)
    if nm in ('make_unique', 'make_shared'):
        return make_obj(fn, t, args, n)
    if nm == 'make_pair':
        return E('new %s(%s)' % (fn.cs.cs(t), ', '.join(c)), t)
    if nm in ('sort', 'stable_sort'):
        return sort_call(fn, nm, args, a, n)
    if nm in ('fill', 'fill_n', 'copy', 'copy_n', 'find', 'find_if', 'count', 'count_if', 'accumulate', 'min_element',
              'max_element', 'lower_bound', 'upper_bound', 'binary_search', 'unique', 'remove', 'remove_if', 'reverse',
              'all_of', 'any_of', 'none_of', 'iota', 'equal', 'transform', 'distance', 'next', 'prev', 'advance',
              'begin', 'end', 'size', 'data', 'empty', 'replace', 'for_each', 'partial_sum', 'adjacent_difference',
              'equal_range', 'mismatch', 'copy_backward', 'rotate', 'search', 'set_union', 'set_difference',
              'is_sorted', 'adjacent_find'):
        c = [algo_fn_arg(fn, x, code) for x, code in zip(args, c)]
        if nm in ('fill', 'fill_n', 'replace') and len(c) >= 3:
            # the values convert to the element type (fill(v.begin(), v.end(), 1) on a vector<char>)
            it = C.of(args[0]).strip()
            el = iter_elem(fn, it) if not isinstance(it, C.Ptr) else it.t
            if el is None and isinstance(it, C.Rec) and base_name(it) == '__gnu_cxx::__normal_iterator':
                el = None
            if el is None and std_name(C.of(args[0]).strip()) is None:
                el = None
            if el is not None and not fn.cs.is_class(el):
                ecs = fn.cs.cs(el, 'elem')
                if isinstance(it, C.Rec) and len(it.args) > 1 and std_name(it.args[1]) in ('std::basic_string', 'std::basic_string_view'):
                    ecs = 'byte'           # a string's iterators run over its bytes
                for vi in range(2, len(c)):
                    vt = C.of(args[vi]).strip()
                    if isinstance(vt, C.Builtin) and vt.name == 'bool' and ecs != 'bool':
                        c[vi] = '((%s)(%s ? 1 : 0))' % (ecs, c[vi])
                    else:
                        c[vi] = 'unchecked((%s)(%s))' % (ecs, c[vi])
        return E('%s.Algo.%s(%s)' % (CPP, nm, ', '.join(c)), t, nm in ('begin', 'end'))
    if nm in ('from_chars',):
        if len(c) >= 3 and a[2].lv:
            c = c[:2] + ['ref ' + c[2]] + c[3:]       # the value is written through a reference
        return E('%s.Std.from_chars(%s)' % (CPP, ', '.join(c)), t)
    # the C library
    if nm in ('fprintf', 'printf', 'snprintf', 'sprintf', 'sscanf', 'fscanf', 'vsnprintf'):
        return printf_like(fn, nm, a, args, n)
    if nm in ('strlen', 'strcmp', 'strncmp', 'strcpy', 'strncpy', 'strcat', 'strchr', 'strrchr', 'strstr', 'strtok',
              'strtod', 'strtol', 'strtoul', 'strtoll', 'atoi', 'atof', 'atol', 'toupper', 'tolower', 'isspace',
              'isdigit', 'isalpha', 'isalnum', 'isupper', 'islower', 'ispunct', 'isprint', 'isxdigit', 'strcspn',
              'strspn', 'strcasecmp', 'strncasecmp', 'strpbrk', 'strdup', 'memcmp', 'fopen', 'fclose', 'fgets',
              'fputs', 'fputc', 'fgetc', 'getc', 'putc', 'ungetc', 'fflush', 'fseek', 'ftell', 'rewind', 'feof',
              'ferror', 'remove', 'rename', 'tmpnam', 'tmpfile', 'puts', 'putchar', 'perror', 'clearerr', 'setvbuf',
              'time', 'clock', 'difftime', 'mktime', 'localtime', 'gmtime', 'strftime', 'localtime_r', 'gmtime_r',
              'getenv', 'exit', 'abort', 'qsort', 'rand', 'srand', 'fileno', 'mkstemp', 'access', 'unlink',
              'realpath', 'fwrite', 'fread', 'ctime', 'asctime'):
        if nm in ('strtod', 'strtol', 'strtoul', 'strtoll') and len(c) >= 2:
            # the end pointer is passed as char** : &end
            endn = strip(args[1])
            if endn.get('kind') == 'UnaryOperator' and endn.get('opcode') == '&':
                ee = fn.x(kids(endn)[0])
                rest = c[2:]
                return E('%s.%s(%s, ref %s%s)' % (R, 'strtol' if nm != 'strtod' else 'strtod', c[0], ee.code,
                                                  ''.join(', ' + x for x in rest)), t)
            if endn.get('kind') in ('CXXNullPtrLiteralExpr', 'ImplicitCastExpr', 'IntegerLiteral', 'GNUNullExpr') and c[1] in ('null', 'default(global::Cpp.Ptr<global::Cpp.CPtr>)'):
                return E('%s.%s(%s%s)' % (R, 'strtod' if nm == 'strtod' else 'strtol0', c[0], ''.join(', ' + x for x in c[2:])), t)
        if nm in ('fwrite', 'fread'):
            return fwrite_like(fn, nm, a, args, n)
        return E('%s.%s(%s)' % (R, nm, ', '.join(c)), t)
    if nm in ('memcpy', '__builtin_memcpy', 'memmove', '__builtin_memmove'):
        return memcpy_like(fn, a, args, n)
    if nm in ('memset', '__builtin_memset'):
        return E('%s.Mem.memset(%s)' % (CPP, ', '.join(c)), t)
    if nm in ('__builtin_expect',):
        return a[0]
    if nm in ('__builtin_huge_val', '__builtin_inf'):
        return E('double.PositiveInfinity', t)
    if nm in ('__builtin_nan', 'nan', '__builtin_nanf', 'nanf'):
        return E('%s.Std.QNaN' % CPP, t)
    if nm in ('__builtin_isnan',):
        return E('double.IsNaN(%s)' % c[0], t)
    if nm in ('__builtin_fabs',):
        return E('global::System.Math.Abs(%s)' % c[0], t)
    if nm in ('omp_get_thread_num',):
        return E('0', t)
    if nm in ('omp_get_max_threads', 'omp_get_num_threads', 'omp_get_num_procs'):
        return E('1', t)
    if nm in ('omp_set_num_threads', 'omp_set_dynamic', 'omp_set_nested'):
        return E('%s.Std.Nop(%s)' % (CPP, c[0]), t)
    if nm in ('omp_in_parallel',):
        return E('0', t)
    if nm in ('get',) and args:
        return E('%s.Std.get%d(%s)' % (CPP, tuple_index(rd, n), c[0]), t, True)
    if nm in ('tie',):
        return E('%s.Std.tie(%s)' % (CPP, ', '.join('ref ' + x for x in c)), t)
    if nm in ('isdigit', 'isspace'):
        return E('%s.%s(%s)' % (R, nm, c[0]), t)
    if nm == 'operator new':
        return todo(fn, 'operator new', n)
    if nm in ('__builtin_bit_cast', 'bit_cast'):
        return E('%s.Bits.Cast<%s>(%s)' % (CPP, fn.cs.cs(t), c[0]), t)
    if nm in ('isnan', 'isinf'):
        return E('double.Is%s(%s)' % ('NaN' if nm == 'isnan' else 'Infinity', c[0]), t)
    return todo(fn, 'library function ' + nm, n)


def numeric_limits(fn, nm, t, n):
    """std::numeric_limits<T>::f() by the call's type (the name alone is ambiguous with std::max/min)."""
    cs = fn.cs.cs(t.strip())
    if cs == 'double':
        v = {'quiet_NaN': '%s.Std.QNaN' % CPP, 'signaling_NaN': '%s.Std.SNaN' % CPP, 'infinity': 'double.PositiveInfinity',
             'epsilon': '2.220446049250313E-16', 'max': 'double.MaxValue', 'min': '2.2250738585072014E-308',
             'lowest': 'double.MinValue', 'denorm_min': '4.94065645841247E-324', 'round_error': '0.5'}.get(nm)
    elif cs == 'float':
        v = {'quiet_NaN': '%s.Std.QNaNf' % CPP, 'infinity': 'float.PositiveInfinity', 'epsilon': '1.1920929E-07f',
             'max': 'float.MaxValue', 'min': '1.17549435E-38f', 'lowest': 'float.MinValue'}.get(nm)
    elif cs in ('int', 'long', 'uint', 'ulong', 'short', 'ushort', 'byte', 'sbyte'):
        v = {'max': '%s.MaxValue' % cs, 'min': '%s.MinValue' % cs, 'lowest': '%s.MinValue' % cs}.get(nm)
    else:
        v = None
    if v is None:
        return todo(fn, 'numeric_limits<%s>::%s' % (cs, nm), n)
    return E(v, t)


def tuple_index(rd, n):
    m = re.search(r'get<(\d+)', tstr(rd.get('type')) or '')
    return int(m.group(1)) if m else 0


def make_obj(fn, t, args, n):
    """std::make_unique<T>(args) / make_shared: construct T."""
    pt = t.strip().args[0] if isinstance(t.strip(), C.Rec) and t.strip().args else None
    if pt is None:
        return todo(fn, 'make_unique of unknown type', n)
    k = fn.m.key_of_type(pt)
    if not args:
        return E(fn.cs.default_value(pt), t)
    if k in fn.m.records:
        # find the constructor by the argument types
        from std_cs import params_of, call_args
        cands = [c for c in fn.m.ctors(k) if len(params_of(c)) >= len(args)]
        exact = [c for c in cands if len(params_of(c)) == len(args)]
        c = exact[0] if len(exact) == 1 else (cands[0] if len(cands) == 1 else None)
        if c is None:
            return todo(fn, 'make_unique: ambiguous constructor of ' + k, n)
        from cs_record import RecordEmitter
        nm = RecordEmitter(fn.cs).ctor_name(c)
        a = call_args(fn, c, args, n)
        return E('%s.New%s(%s)' % (fn.cs.rec_name(k), '' if nm == 'd' else '_' + nm, ', '.join(a)), t)
    return todo(fn, 'make_unique of ' + repr(pt), n)


def sort_call(fn, nm, args, a, n):
    """std::sort(first, last[, comp]) over a vector or an array: libstdc++'s algorithm (cs/runtime/StdAlgo.cs)."""
    f, l = a[0], a[1]
    et = C.of(args[0]).strip()
    el = iter_elem(fn, et)
    if el is None:
        return todo(fn, 'sort over ' + repr(et), n)
    comp = algo_fn_arg(fn, args[2], a[2].code) if len(a) > 2 else '%s.Less<%s>.Fn' % (CPP, fn.cs.cs(el, 'elem'))
    fname = 'Sort' if nm == 'sort' else 'StableSort'
    if isinstance(et, C.Ptr):
        # over a raw array: the pointer's array and offsets
        return E('%s.StdAlgo.%s(%s.a, %s.o, %s.o, %s)' % (CPP, fname, f.code, f.code, l.code, comp), C.of(n))
    return E('%s.StdAlgo.%s(%s.a, %s.i, %s.i, %s)' % (CPP, fname, f.code, f.code, l.code, comp), C.of(n))


def iter_elem(fn, it):
    if isinstance(it, C.Rec) and base_name(it) == '__gnu_cxx::__normal_iterator' and isinstance(it.args[0], C.Ptr):
        return it.args[0].t
    if isinstance(it, C.Ptr):
        return it.t
    return None


def printf_like(fn, nm, a, args, n):
    t = C.of(n)
    c = [e.code if e is not None else None for e in a]
    if nm == 'fprintf':
        return E('%s.fprintf(%s)' % (R, ', '.join(c)), t)
    if nm == 'printf':
        return E('%s.fprintf(%s.stdout_, %s)' % (R, R, ', '.join(c)), t)
    if nm in ('snprintf', 'vsnprintf'):
        return E('%s.snprintf(%s)' % (R, ', '.join(c)), t)
    if nm == 'sprintf':
        return E('%s.sprintf(%s)' % (R, ', '.join(c)), t)
    if nm in ('sscanf', 'fscanf'):
        # scanf targets are pointers: &x becomes a setter
        out = c[:2]
        backs = []          # targets a C# lambda cannot write (ref parameters and ref locals): stashed, copied back
        for x, e in zip(args[2:], a[2:]):
            xs = strip(x)
            if xs.get('kind') == 'UnaryOperator' and xs.get('opcode') == '&':
                tn = strip(kids(xs)[0])
                tgt = fn.x(kids(xs)[0])
                tt = C.of(kids(xs)[0]).strip()
                cs = fn.cs.cs(tt)
                tkey = (tn.get('referencedDecl') or {}).get('key') if tn.get('kind') == 'DeclRefExpr' else None
                if tkey in fn.refs and cs in ('int', 'long', 'double', 'float', 'sbyte', 'byte', 'short', 'ushort', 'uint', 'ulong'):
                    out.append('%s.Slot' % R)
                    backs.append(tgt.code)
                    continue
                setter = {'int': 'RefI', 'long': 'RefL', 'double': 'RefD', 'float': 'RefF', 'sbyte': 'RefI',
                          'byte': 'RefI', 'short': 'RefI', 'ushort': 'RefI', 'uint': 'RefL', 'ulong': 'RefL'}.get(cs)
                if setter is None:
                    return todo(fn, 'scanf into ' + cs, n)
                # the lambda's parameter is __sv: a local named v (long v = 99999; sscanf(..., &v)) must not
                # be shadowed by it, or the assignment stores the value into the parameter itself
                conv = '__sv' if cs in ('int', 'long', 'double', 'float') else 'unchecked((%s)__sv)' % cs
                out.append('%s.%s(__sv => %s = %s)' % (R, setter, tgt.code, conv))
            else:
                out.append(fn.x(x).code)
        call = '%s.%s(%s)' % (R, nm, ', '.join(out))
        if backs:
            if len(backs) > 6:
                return todo(fn, 'scanf into more than six reference targets', n)
            call = '%s.Back(%s, %s)' % (R, call, ', '.join('ref ' + b for b in backs))
        return E(call, t)
    return todo(fn, nm, n)


def fwrite_like(fn, nm, a, args, n):
    """fwrite(ptr, size, count, f) / fread: by the pointer's element type."""
    t = C.of(n)
    p = strip(args[0])
    while p.get('kind') in ('ImplicitCastExpr', 'CStyleCastExpr', 'CXXStaticCastExpr', 'CXXReinterpretCastExpr') and kids(p):
        p = strip(kids(p)[0])
    rest = [fn.x(x).code for x in args[1:]]
    if p.get('kind') == 'UnaryOperator' and p.get('opcode') == '&':
        target = kids(p)[0]
        tt = C.of(target).strip()
        cs = fn.cs.cs(tt)
        suf = {'float': 'F1', 'double': 'D1', 'int': 'I1', 'long': 'L1'}.get(cs)
        if suf is None:
            return todo(fn, '%s of &%s' % (nm, cs), n)
        e = fn.x(target)
        if nm == 'fwrite':
            return E('%s.fwrite%s(%s, %s)' % (R, suf, e.code, rest[2]), t)
        return E('%s.fread%s(ref %s, %s)' % (R, suf, e.code, rest[2]), t)
    e0 = typed_ptr(fn, args[0])
    return E('%s.Mem.%s(%s)' % (CPP, nm, ', '.join([e0.code] + rest)), t)


def memcpy_like(fn, a, args, n):
    """memcpy(dst, src, n): between typed pointers, or the bytes of a value (a bit cast)."""
    t = C.of(n)
    c = [e.code for e in a]
    return E('%s.Mem.memcpy(%s)' % (CPP, ', '.join(c)), t)


# ---------------------------------------------------------------------------------------------- methods
def method(fn, me, obj_n, args, n):
    t = C.of(n)
    nm = me.get('name')
    ot = C.of(obj_n).strip()
    if isinstance(ot, C.Ptr) and me.get('isArrow'):
        ot = ot.t.strip()
    sn = std_name(ot)
    ob = fn.x(obj_n)
    o = fn.obj(ob)
    if isinstance(ot, C.Closure) and nm.startswith('operator ') and isinstance(t.strip(), C.Ptr) and \
            isinstance(t.strip().t, C.Func):
        # a captureless lambda converted to a plain function pointer (forcing.refresh = [](void* u, double t) {...}):
        # clang calls the closure's conversion operator; the C# lambda is already a delegate of that signature
        dt = fn.cs.cs(t.strip())
        if ob.code.startswith('(%s)(' % dt):
            return E(ob.code, t)           # the lambda, already cast to the pointer's delegate
        return E('((%s)(%s).Invoke)' % (dt, ob.code), t)
    if nm in ('read', 'write') and sn in STREAMS and args:
        p = typed_ptr(fn, args[0])
        rest = [e.code for e in lib_args(fn, args[1:])]
        return E('%s.%s(%s)' % (o, nm, ', '.join([p.code] + rest)), t)
    a = lib_args(fn, args)
    c = [e.code for e in a]
    if sn == 'std::vector':
        return vec_method(fn, o, ob, ot, nm, a, args, t, n)
    if sn in ('std::basic_string', 'std::basic_string_view'):
        return str_method(fn, o, ob, ot, nm, a, args, t, n)
    if sn == 'std::array':
        if nm == 'size':
            return E('((ulong)%s.Length)' % o, t)
        if nm in ('begin', 'cbegin'):
            return E('new %s.Iter<%s>(%s, 0)' % (CPP, fn.cs.cs(ot.args[0], 'elem'), o), t)
        if nm in ('end', 'cend'):
            return E('new %s.Iter<%s>(%s, %s.Length)' % (CPP, fn.cs.cs(ot.args[0], 'elem'), o, o), t)
        if nm == 'data':
            return E('new %s.Ptr<%s>(%s, 0)' % (CPP, fn.cs.cs(ot.args[0], 'elem'), o), t)
        if nm == 'fill':
            return E('global::System.Array.Fill(%s, %s)' % (o, c[0]), t)
        if nm in ('front',):
            return E('%s[0]' % o, t, True)
        if nm in ('back',):
            return E('%s[%s.Length - 1]' % (o, o), t, True)
        if nm == 'at':
            return E('%s[%s]' % (o, c[0]), t, True)
        if nm == 'empty':
            return E('(%s.Length == 0)' % o, t)
    if sn in ('std::stack', 'std::queue'):
        if nm == 'push':
            return E('%s.push(%s)' % (o, elem_copy(fn, ot.args[0], a[0], args[0])), t)
        if nm in ('pop', 'empty', 'size'):
            return E('%s.%s()' % (o, nm), t)
        if nm in ('top', 'front', 'back'):
            return E('%s.%s()' % (o, nm), t, True)
    if sn == 'std::_Bit_reference':
        if nm == 'operator bool':
            return E(ob.code, t)
        if nm == 'flip':
            return E('(%s = !%s)' % (ob.code, ob.code), t)
    if sn == 'std::error_code':
        if nm == 'operator bool':
            return E('%s.Bool()' % o, t)
        if nm in ('value', 'message', 'clear'):
            return E('%s.%s()' % (o, nm), t)
    if sn in ('std::__shared_ptr', 'std::__shared_ptr_access', 'std::__weak_ptr'):
        sn = 'std::shared_ptr'
    if sn in ('std::unique_ptr', 'std::shared_ptr'):
        if nm == 'get':
            return E(ob.code, t)
        if nm == 'reset':
            if sn == 'std::unique_ptr':
                # the old object is deleted (its destructor runs) after the new pointer is stored
                return E('%s.Mv.Reset(ref %s, %s)' % (CPP, ob.code, c[0] if c else 'null'), t)
            if c:
                return E('(%s = %s)' % (ob.code, c[0]), t)
            return E('(%s = null)' % ob.code, t)
        if nm == 'release':
            return E('%s.Std.release(ref %s)' % (CPP, ob.code), t)
        if nm == 'operator bool':
            return E('(%s != null)' % ob.code, t)
    if sn == 'std::function' and nm == 'operator bool':
        return E('(%s != null)' % ob.code, t)
    if sn == 'std::optional':
        if nm in ('has_value', 'operator bool'):
            return E('%s.has' % o, t)
        if nm == 'value':
            return E('%s.value()' % o, t, True)
        if nm == 'value_or':
            return E('%s.value_or(%s)' % (o, c[0]), t)
        if nm == 'reset':
            return E('%s.reset()' % o, t)
        if nm == 'emplace':
            el = ot.args[0] if ot.args else None
            ek = fn.m.key_of_type(el) if isinstance(el, C.Rec) else None
            if ek in fn.m.records and args and not (len(args) == 1 and fn.m.key_of_type(C.of(args[0]).strip()) == ek):
                # emplace(args...) runs T's constructor on them
                made = make_obj(fn, C.Rec('std::unique_ptr', [el]), args, n)
                return E('%s.emplace(%s)' % (o, made.code), t)
            return E('%s.emplace(%s)' % (o, c[0] if c else fn.cs.default_value(ot.args[0])), t)
    if sn == '__gnu_cxx::__normal_iterator' and nm == 'base':
        return E(ob.code, t)
    if sn in ('std::unordered_map', 'std::map', 'std::unordered_set', 'std::set'):
        return map_method(fn, o, ob, ot, nm, a, args, t, n)
    if sn == 'std::pair' and nm == 'swap':
        return todo(fn, 'pair::swap', n)
    if sn in ('std::__exception_ptr::exception_ptr', 'std::exception_ptr') and nm == 'operator bool':
        return E('(%s != null)' % o, t)
    if sn == 'std::fpos' and nm.startswith('operator '):
        return E('((long)%s)' % o, t)          # streampos is the stream offset (a long here)
    if sn in ('std::atomic',):
        if nm == 'load':
            return E('%s.v' % o, t)
        if nm == 'store':
            return E('(%s.v = %s)' % (o, c[0]), t)
        if nm in ('fetch_add', 'fetch_sub'):
            return E('%s.%s(%s)' % (o, nm, c[0]), t)
        if nm in ('compare_exchange_weak', 'compare_exchange_strong') and len(c) >= 2:
            return E('%s.compare_exchange(ref %s, %s)' % (o, c[0], c[1]), t)
        if nm == 'operator ' + (repr(ot.args[0]) if ot.args else ''):
            return E('%s.v' % o, t)
        if nm.startswith('operator '):
            return E('%s.v' % o, t)
        if nm == 'exchange':
            return E('%s.exchange(%s)' % (o, c[0]), t)
    if sn in ('std::mutex', 'std::recursive_mutex', 'std::shared_mutex') and nm in ('lock', 'unlock', 'try_lock', 'lock_shared', 'unlock_shared'):
        return E('%s.Std.Nop(0)' % CPP, t)
    if sn in ('std::basic_ostringstream', 'std::basic_stringstream', 'std::basic_istringstream', 'std::basic_ifstream',
              'std::basic_ofstream', 'std::basic_fstream', 'std::basic_ostream', 'std::basic_istream', 'std::basic_ios'):
        return stream_method(fn, o, ob, ot, nm, a, args, t, n)
    if sn in ('std::filesystem::path',):
        return E('%s.%s(%s)' % (o, ident(nm), ', '.join(c)), t)
    if sn in ('std::exception', 'std::runtime_error', 'std::logic_error', 'std::invalid_argument', 'std::out_of_range') and nm == 'what':
        return E('%s.what()' % o, t)
    if sn in ('std::chrono::duration',) and nm == 'count':
        return E('%s.count()' % o, t)
    if sn in ('std::chrono::time_point',) and nm == 'time_since_epoch':
        return E('%s.time_since_epoch()' % o, t)
    if sn == 'std::initializer_list':
        if nm == 'size':
            return E('((ulong)%s.Length)' % o, t)
        if nm == 'begin':
            return E('new %s.Iter<%s>(%s, 0)' % (CPP, fn.cs.cs(ot.args[0], 'elem'), o), t)
        if nm == 'end':
            return E('new %s.Iter<%s>(%s, %s.Length)' % (CPP, fn.cs.cs(ot.args[0], 'elem'), o, o), t)
    if sn == 'std::span':
        if nm in ('size', 'empty', 'data', 'subspan', 'first', 'last'):
            return E('%s.%s(%s)' % (o, nm, ', '.join(c)), t)
        if nm in ('front', 'back'):
            return E('%s.%s()' % (o, nm), t, True)
    return todo(fn, 'method %s::%s' % (sn or repr(ot), nm), n)


def elem_copy(fn, el, e, node):
    """An element stored into a container: a named class object is copied, a moved or temporary one is not."""
    if fn.cs.is_class(el):
        s = strip(node)
        if e.lv is True and node.get('valueCategory') == 'lvalue':
            from std_cs import copy_of
            return copy_of(fn, e.code, el)
        if s.get('kind') == 'CallExpr' and is_std_move(s):
            inner = fn.x(kids(s)[1])
            if inner.lv is True:
                return '%s.Mv.Take(ref %s)' % (CPP, inner.code)
    return e.code


def vec_method(fn, o, ob, ot, nm, a, args, t, n):
    el = ot.args[0]
    c = [e.code for e in a]
    simple = {'size', 'empty', 'clear', 'pop_back', 'reserve', 'shrink_to_fit', 'capacity', 'begin', 'end',
              'cbegin', 'cend', 'rbegin', 'rend'}
    if nm in simple:
        if nm in ('reserve',):
            return E('%s.reserve(%s)' % (o, c[0]), t)
        return E('%s.%s()' % (o, nm), t)
    if nm in ('front', 'back'):
        return E('%s.%s()' % (o, nm), t, True)
    if nm == 'data':
        return E('%s.data()' % o, t)
    if nm == 'max_size':
        # libstdc++ on x86-64: min(PTRDIFF_MAX / sizeof(T), allocator's max_size), which is PTRDIFF_MAX / sizeof(T)
        from cs_func import sizeof
        return E('((ulong)%dUL)' % (0x7FFFFFFFFFFFFFFF // sizeof(fn, el, n)), t)
    if nm == 'at':
        return E('%s.at(%s)' % (o, c[0]), t, True)
    if nm in ('push_back', 'emplace_back'):
        if nm == 'emplace_back' and (len(args) != 1 or not same_type(fn, C.of(args[0]).strip(), el)):
            v = emplace_value(fn, el, args, n)
        else:
            v = elem_copy(fn, el, a[0], args[0])
        return E('%s.%s(%s)' % (o, nm, v), t, nm == 'emplace_back')
    if nm == 'resize':
        if len(c) == 2:
            return E('%s.resize(%s, %s)' % (o, c[0], c[1]), t)
        return E('%s.resize(%s)' % (o, c[0]), t)
    if nm == 'assign':
        if len(c) == 2 and std_name(C.of(args[0]).strip()) == '__gnu_cxx::__normal_iterator':
            return E('%s.assign(%s, %s)' % (o, c[0], c[1]), t)
        return E('%s.assign(%s)' % (o, ', '.join(c)), t)
    if nm in ('insert', 'emplace'):
        if len(c) == 2:
            v = elem_copy(fn, el, a[1], args[1]) if nm == 'insert' else emplace_value(fn, el, args[1:], n)
            return E('%s.insert(%s, %s)' % (o, c[0], v), t)
        return E('%s.insert(%s)' % (o, ', '.join(c)), t)
    if nm == 'erase':
        return E('%s.erase(%s)' % (o, ', '.join(c)), t)
    if nm == 'swap':
        return E('%s.swap(%s)' % (o, c[0]), t)
    return todo(fn, 'vector::' + nm, n)


def same_type(fn, a, b):
    try:
        return fn.cs.cs(a) == fn.cs.cs(b)
    except Exception:
        return False


def emplace_value(fn, el, args, n):
    """emplace_back(args...) constructs an element from its constructor arguments."""
    k = fn.m.key_of_type(el)
    if not args:
        return fn.cs.default_value(el)
    if k in fn.m.records:
        from std_cs import params_of, call_args
        cands = [c for c in fn.m.ctors(k) if len(params_of(c)) == len(args)]
        if len(cands) != 1:
            # an aggregate: fields in order
            fields = fn.m.records[k].get('fields', [])
            if not fn.m.ctors(k) or all(c.get('isImplicit') for c in fn.m.ctors(k)):
                sets = []
                for f, x in zip(fields, args):
                    ft = C.parse(tstr(f.get('type')))
                    sets.append('%s = %s' % (fn.cs.field_name(f.get('key'), f.get('name')), fn.value_for(ft, fn.x(x), x)))
                return 'new %s() { %s }' % (fn.cs.rec_name(k), ', '.join(sets))
            return todo(fn, 'emplace: constructor of %s by %d arguments' % (k, len(args)), n)
        from cs_record import RecordEmitter
        nmc = RecordEmitter(fn.cs).ctor_name(cands[0])
        a = call_args(fn, cands[0], args, n)
        return '%s.New%s(%s)' % (fn.cs.rec_name(k), '' if nmc == 'd' else '_' + nmc, ', '.join(a))
    sn = std_name(el)
    if sn == 'std::pair' and len(args) == 2:
        vals = [fn.value_for(el.args[i], fn.x(x), x) for i, x in enumerate(args)]
        return 'new %s(%s)' % (fn.cs.cs(el), ', '.join(vals))
    if len(args) == 1:
        e = fn.x(args[0])
        return fn.value_for(el, e, args[0])
    if sn in ('std::basic_string',):
        return 'new %s.Str(%s)' % (CPP, ', '.join(fn.x(x).code for x in args))
    return todo(fn, 'emplace of ' + repr(el), n)


def str_method(fn, o, ob, ot, nm, a, args, t, n):
    c = [e.code for e in a]
    same = {'size', 'length', 'empty', 'clear', 'c_str', 'data', 'substr', 'find', 'rfind', 'find_first_of',
            'find_first_not_of', 'find_last_of', 'find_last_not_of', 'append', 'push_back', 'pop_back', 'resize',
            'reserve', 'erase', 'insert', 'replace', 'compare', 'starts_with', 'ends_with', 'contains', 'assign',
            'begin', 'end', 'shrink_to_fit'}
    if nm in same:
        return E('%s.%s(%s)' % (o, nm, ', '.join(c)), t)
    if nm in ('front', 'back'):
        return E('((sbyte)%s.%s())' % (o, nm), t, 'set')
    if nm == 'at':
        return E('((sbyte)%s.at(%s))' % (o, c[0]), t)
    if nm == 'operator basic_string_view':
        return E('%s.Clone()' % o, t)
    if nm in ('remove_prefix', 'remove_suffix'):
        return E('%s.%s(%s)' % (o, nm, c[0]), t)
    if nm == 'swap':
        return todo(fn, 'string::swap', n)
    return todo(fn, 'string::' + nm, n)


def map_method(fn, o, ob, ot, nm, a, args, t, n):
    c = [e.code for e in a]
    if nm in ('find', 'count', 'contains', 'erase', 'size', 'empty', 'clear', 'end', 'begin', 'reserve', 'at'):
        return E('%s.%s(%s)' % (o, nm, ', '.join(c)), t, nm == 'at')
    if nm in ('insert', 'emplace', 'try_emplace', 'insert_or_assign'):
        return E('%s.%s(%s)' % (o, nm, ', '.join(c)), t)
    return todo(fn, 'map::' + nm, n)


def stream_method(fn, o, ob, ot, nm, a, args, t, n):
    c = [e.code for e in a]
    if nm in ('str', 'good', 'fail', 'bad', 'eof', 'is_open', 'close', 'clear', 'flush', 'tellg', 'tellp', 'seekg',
              'seekp', 'peek', 'get', 'ignore', 'precision', 'setf', 'unsetf', 'width', 'fill', 'rdbuf',
              'operator bool', 'operator!', 'open', 'gcount', 'imbue', 'exceptions'):
        name = {'operator bool': 'ok', 'operator!': 'notok'}.get(nm, nm)
        return E('%s.%s(%s)' % (o, name, ', '.join(c)), t)
    if nm in ('read', 'write'):
        # binary I/O through reinterpret_cast<char*>: the runtime moves the bytes of the typed pointer
        p = typed_ptr(fn, args[0])
        return E('%s.%s(%s)' % (o, nm, ', '.join([p.code] + c[1:])), t)
    return todo(fn, 'stream::' + nm, n)


# ---------------------------------------------------------------------------------------------- operators
STREAMS = ('std::basic_ostringstream', 'std::basic_stringstream', 'std::basic_istringstream', 'std::basic_ifstream',
           'std::basic_ofstream', 'std::basic_fstream', 'std::basic_ostream', 'std::basic_istream', 'std::basic_ios')
MAP_ITERS = ('std::__detail::_Node_iterator', 'std::__detail::_Node_const_iterator', 'std::_Rb_tree_iterator',
             'std::_Rb_tree_const_iterator', 'std::__detail::_Node_iterator_base', 'std::_Rb_tree_iterator_base')


def operator(fn, nm, rd, args, n):
    t = C.of(n)
    if not args:
        return todo(fn, 'operator with no operands', n)
    t0 = C.of(args[0]).strip()
    sn = std_name(t0)
    if sn in ('std::__shared_ptr_access', 'std::__shared_ptr'):
        sn = 'std::shared_ptr'
    if sn == 'std::_Bit_reference' and nm == 'operator=':
        ea, eb = fn.x(args[0]), fn.x(args[1])
        return E('(%s = %s)' % (ea.code, eb.code), t, True)
    if sn in MAP_ITERS:
        ea = fn.x(args[0])
        if nm in ('operator==', 'operator!='):
            eb = fn.x(args[1])
            return E('(%s %s %s)' % (ea.code, nm[8:], eb.code), t)
        if nm in ('operator*', 'operator->'):
            return E('%s.Deref' % fn.obj(ea), t, True)
        if nm in ('operator++', 'operator--'):
            if len(args) == 2:
                return E('%s.Mv.PostStep(ref %s, %d)' % (CPP, ea.code, 1 if nm == 'operator++' else -1), t)
            return E('(%s%s)' % (nm[8:], ea.code), t, True)
    if nm == 'operator[]':
        ob = fn.x(args[0])
        ie = fn.x(args[1])
        o = fn.obj(ob)
        if sn in ('std::vector',):
            return E('%s[%s]' % (o, ie.code), t, True)
        if sn in ('std::array',):
            return E('%s[%s]' % (o, ie.code), t, True)
        if sn in ('std::basic_string', 'std::basic_string_view'):
            return E('((sbyte)%s[%s])' % (o, ie.code), t, 'str:%s[%s]' % (o, ie.code))
        if sn in ('std::unordered_map', 'std::map'):
            return E('%s.Index(%s)' % (o, ie.code), t, True)
        if sn == '__gnu_cxx::__normal_iterator':
            return E('%s[%s]' % (o, ie.code), t, True)
        if sn == 'std::span':
            return E('%s[%s]' % (o, ie.code), t, True)
        if sn in ('std::unique_ptr',):
            return E('%s[%s]' % (o, ie.code), t, True)
        return todo(fn, 'operator[] on ' + (sn or repr(t0)), n)
    if nm == 'operator()':
        ob = fn.x(args[0])
        if isinstance(t0, C.Closure):
            op = fn.cs.lambdas.get('(lambda at %s)' % t0.where)
            if op is None:
                # not emitted yet (the lambda is in a caller): any instantiation's operator serves for the
                # parameters; the delegate comes from the operator() this call names
                ops = fn.cs.lambda_ops.get('(lambda at %s)' % t0.where) or []
                op = ops[0] if len(ops) == 1 else None
            if isinstance(op, tuple):
                # a generic lambda: the instantiation whose signature the call's operator() has
                callee = unwrap_callee(kids(n)[0])
                sig = tstr((callee.get('referencedDecl') or {}).get('type') or callee.get('type'))
                pick = [x for x in op[1] if x[0] == sig]
                if not pick:
                    return todo(fn, 'generic lambda call without a matching instantiation', n)
                _, i, op1 = pick[0]
                from std_cs import call_args
                ca = call_args(fn, op1, args[1:], n)
                ft1 = C.parse(tstr(op1.get('type')))
                from emit_cs import param_type
                dt = fn.cs.delegate(C.Func(ft1.ret if isinstance(ft1, C.Func) else C.Builtin('void'),
                                           [param_type(C.parse(tstr(p.get('type')))) for p in params_of(op1)]))
                return E('((%s)%s[%d])(%s)' % (dt, fn.obj(ob), i, ', '.join(ca)), t, isinstance(t, C.LRef))
            callee = unwrap_callee(kids(n)[0])
            sig = C.parse(tstr((callee.get('referencedDecl') or {}).get('type') or callee.get('type')))
            if isinstance(sig, C.Func):
                # the operator() this call names gives the delegate and how each argument is passed (a closure
                # parameter is held as object; its lambda may be any of several instantiations); a default
                # argument comes from the lambda's own declaration
                from emit_cs import param_type
                from std_cs import default_arg
                pps = params_of(op) if op is not None else []
                ca = []
                for i, x in enumerate(args[1:]):
                    if x.get('kind') == 'CXXDefaultArgExpr':
                        if i >= len(pps):
                            return todo(fn, 'default argument of an unknown lambda', n)
                        e = default_arg(fn, pps[i], n)
                    else:
                        e = fn.x(x)
                    pt = param_type(sig.params[i]) if i < len(sig.params) else None
                    ca.append(pass_arg(fn, pt, x, e, n) if pt is not None else e.code)
                sret = sig.ret
                if fn.unresolved(sret):
                    # spelled by a member typedef (value_type), as in the lambda (std_cs.lambda_code): the call's type
                    st_ = fn.T(n)
                    sret = st_.strip() if isinstance(st_, (C.LRef, C.RRef)) else st_
                dt = fn.cs.delegate(C.Func(sret, [param_type(p) for p in sig.params]))
                return E('((%s)%s)(%s)' % (dt, fn.obj(ob), ', '.join(ca)), t, isinstance(t, C.LRef))
            if op is not None:
                from std_cs import call_args
                ca = call_args(fn, op, args[1:], n)
                return E('%s(%s)' % (fn.obj(ob), ', '.join(ca)), t, isinstance(t, C.LRef))
        a = [fn.x(x) for x in args[1:]]
        if sn == 'std::function' or isinstance(t0, C.Closure):
            ft = t0.args[0] if sn == 'std::function' else None
            callee_t = None
            if isinstance(t0, C.Closure):
                callee = unwrap_callee(kids(n)[0])
                ft = C.parse(tstr((callee.get('referencedDecl') or {}).get('type') or callee.get('type')))
                if isinstance(ft, C.Func):
                    from emit_cs import param_type
                    callee_t = fn.cs.delegate(C.Func(ft.ret, [param_type(p) for p in ft.params]))
                else:
                    ft = None
            ca = []
            for i, (x, e) in enumerate(zip(args[1:], a)):
                pt = ft.params[i] if ft is not None and i < len(ft.params) else None
                ca.append(pass_arg(fn, pt, x, e, n) if pt is not None else e.code)
            f_ = '((%s)%s)' % (callee_t, fn.obj(ob)) if callee_t else fn.obj(ob)
            return E('%s(%s)' % (f_, ', '.join(ca)), t, isinstance(t, C.LRef))
        if sn in ('std::hash',):
            return E('%s.Hash<%s>.Of(%s)' % (CPP, fn.cs.cs(t0.args[0], 'elem'), a[0].code), t)
        if sn in ('std::less',):
            return E('(%s < %s)' % (a[0].code, a[1].code), t)
        return todo(fn, 'call operator on ' + (sn or repr(t0)), n)
    if nm == 'operator=':
        return assign_op(fn, sn, t0, args, t, n)
    if nm in ('operator==', 'operator!=', 'operator<', 'operator>', 'operator<=', 'operator>='):
        op = nm[8:]
        ea, eb = fn.x(args[0]), fn.x(args[1])
        t1 = C.of(args[1]).strip()
        sn1 = std_name(t1)
        if sn in ('std::basic_string', 'std::basic_string_view') or sn1 in ('std::basic_string', 'std::basic_string_view'):
            if op in ('==', '!='):
                if sn in ('std::basic_string', 'std::basic_string_view') and sn1 in ('std::basic_string', 'std::basic_string_view'):
                    code = '%s.Equals(%s)' % (fn.obj(ea), eb.code)
                elif sn in ('std::basic_string', 'std::basic_string_view'):
                    code = '%s.EqLit(%s)' % (fn.obj(ea), eb.code)
                else:
                    code = '%s.EqLit(%s)' % (fn.obj(eb), ea.code)
                return E(code if op == '==' else '!' + code, t)
            return E('(%s.compare(%s) %s 0)' % (fn.obj(ea), eb.code, op), t)
        if sn == '__gnu_cxx::__normal_iterator' or sn == 'std::reverse_iterator':
            return E('(%s %s %s)' % (ea.code, op, eb.code), t)
        if sn in ('std::vector',) and op in ('==', '!='):
            code = '%s.Vec<%s>.Eq(%s, %s)' % (CPP, fn.cs.cs(t0.args[0], 'elem'), ea.code, eb.code)
            return E(code if op == '==' else '!' + code, t)
        if sn in ('std::array',) and op in ('==', '!='):
            code = '%s.Arr.Eq(%s, %s)' % (CPP, ea.code, eb.code)
            return E(code if op == '==' else '!' + code, t)
        if sn in ('std::pair',) and op in ('==', '!='):
            code = '%s.Pair<%s, %s>.Eq(%s, %s)' % (CPP, fn.cs.cs(t0.args[0], 'elem'), fn.cs.cs(t0.args[1], 'elem'), ea.code, eb.code)
            return E(code if op == '==' else '!' + code, t)
        if sn in ('std::pair',) and op in ('<', '>', '<=', '>='):
            return E('(%s %s %s)' % (ea.code, op, eb.code), t)     # Cpp.Pair's lexicographic operators
        if sn in ('std::unique_ptr', 'std::shared_ptr', 'std::function') or sn1 in ('std::unique_ptr', 'std::shared_ptr', 'std::function'):
            return E('(%s %s %s)' % (ea.code, op, eb.code), t)
        if sn in ('std::chrono::duration', 'std::chrono::time_point'):
            return E('(%s %s %s)' % (ea.code, op, eb.code), t)
        if sn in ('std::filesystem::path',):
            return E('(%s.compare(%s) %s 0)' % (fn.obj(ea), eb.code, op), t)
        if sn == 'std::optional':
            return E('%s.Std.OptCmp(%s, %s, "%s")' % (CPP, ea.code, eb.code, op), t)
        return todo(fn, 'comparison %s on %s' % (op, sn or repr(t0)), n)
    if nm in ('operator+', 'operator-'):
        ea = fn.x(args[0])
        if len(args) == 1:
            return todo(fn, 'unary %s on %s' % (nm, sn or repr(t0)), n)
        eb = fn.x(args[1])
        t1 = C.of(args[1]).strip()
        if sn in ('std::basic_string',) or std_name(t1) in ('std::basic_string',):
            if nm == '+':
                pass
            return E('%s.Str.Cat(%s, %s)' % (CPP, ea.code, eb.code), t)
        if sn == '__gnu_cxx::__normal_iterator' or sn == 'std::reverse_iterator':
            if std_name(t1) == '__gnu_cxx::__normal_iterator':
                return E('(%s %s %s)' % (ea.code, nm[8:], eb.code), t)
            return E('(%s %s (long)(%s))' % (ea.code, nm[8:], eb.code), t)
        if sn in ('std::chrono::duration', 'std::chrono::time_point'):
            return E('(%s %s %s)' % (ea.code, nm[8:], eb.code), t)
        if sn in ('std::filesystem::path',):
            return todo(fn, 'path +', n)
        return todo(fn, '%s on %s' % (nm, sn or repr(t0)), n)
    if nm in ('operator+=', 'operator-='):
        ea, eb = fn.x(args[0]), fn.x(args[1])
        if sn in ('std::basic_string',):
            return E('%s.append(%s)' % (fn.obj(ea), eb.code), t)
        if sn == '__gnu_cxx::__normal_iterator':
            return E('(%s = %s %s (long)(%s))' % (ea.code, ea.code, nm[8], eb.code), t, True)
        if sn in ('std::chrono::duration',):
            return E('(%s = %s %s %s)' % (ea.code, ea.code, nm[8], eb.code), t, True)
        return todo(fn, '%s on %s' % (nm, sn or repr(t0)), n)
    if nm in ('operator*', 'operator->'):
        ea = fn.x(args[0])
        if len(args) == 2:
            eb = fn.x(args[1])
            if sn in ('std::chrono::duration',):
                return E('(%s * %s)' % (ea.code, eb.code), t)
            return todo(fn, 'binary * on ' + (sn or repr(t0)), n)
        if sn in ('std::unique_ptr', 'std::shared_ptr'):
            return E(ea.code, t, True)
        if sn == '__gnu_cxx::__normal_iterator' or sn == 'std::reverse_iterator':
            return E('%s.Deref' % fn.obj(ea), t, True)
        if sn == 'std::optional':
            return E('%s.val' % fn.obj(ea), t, True)
        if sn in ('std::_Rb_tree_iterator', 'std::_Rb_tree_const_iterator', 'std::__detail::_Node_iterator',
                  'std::__detail::_Node_const_iterator'):
            return E('%s.Deref' % fn.obj(ea), t, True)
        return todo(fn, '%s on %s' % (nm, sn or repr(t0)), n)
    if nm in ('operator++', 'operator--'):
        ea = fn.x(args[0])
        post = len(args) == 2
        if sn in ('__gnu_cxx::__normal_iterator', 'std::reverse_iterator', 'std::_Rb_tree_iterator',
                  'std::_Rb_tree_const_iterator', 'std::__detail::_Node_iterator', 'std::__detail::_Node_const_iterator'):
            if post:
                return E('%s.Mv.PostStep(ref %s, %d)' % (CPP, ea.code, 1 if nm == 'operator++' else -1), t)
            return E('(%s%s)' % (nm[8:], ea.code), t, True)
        if sn == 'std::atomic':
            # ++a returns the new value, a++ the old one
            if post:
                return E('(%s.v%s)' % (fn.obj(ea), nm[8:]), t)
            return E('(%s%s.v)' % (nm[8:], fn.obj(ea)), t)
        return todo(fn, '%s on %s' % (nm, sn or repr(t0)), n)
    if nm in ('operator<<', 'operator>>'):
        return stream_op(fn, nm, args, t, n)
    if nm == 'operator!':
        ea = fn.x(args[0])
        if sn in ('std::basic_ifstream', 'std::basic_ofstream', 'std::basic_istream', 'std::basic_ostream',
                  'std::basic_istringstream', 'std::basic_ostringstream', 'std::basic_ios', 'std::basic_fstream', 'std::basic_stringstream'):
            return E('%s.notok()' % fn.obj(ea), t)
        return todo(fn, 'operator! on ' + (sn or repr(t0)), n)
    if nm in ('operator/',):
        ea, eb = fn.x(args[0]), fn.x(args[1])
        if sn == 'std::filesystem::path':
            return E('%s.Join(%s)' % (fn.obj(ea), eb.code), t)
        if sn in ('std::chrono::duration',):
            return E('(%s / %s)' % (ea.code, eb.code), t)
    if nm in ('operator|', 'operator&', 'operator~', 'operator^') and sn is None:
        ea = fn.x(args[0])
        if len(args) == 2:
            eb = fn.x(args[1])
            return E('(%s %s %s)' % (ea.code, nm[8:], eb.code), t)
        return E('(~%s)' % ea.code, t)
    if nm in ('operator|', 'operator&') and sn in ('std::_Ios_Openmode', 'std::_Ios_Fmtflags', 'std::_Ios_Iostate'):
        ea, eb = fn.x(args[0]), fn.x(args[1])
        return E('(%s %s %s)' % (ea.code, nm[8:], eb.code), t)
    return todo(fn, '%s on %s' % (nm, sn or repr(t0)), n)


def assign_op(fn, sn, t0, args, t, n):
    ea = fn.x(args[0])
    src = args[1]
    eb = fn.x(src)
    t1 = C.of(src).strip()
    s = strip(src)
    moving = src.get('valueCategory') == 'xvalue' or (s.get('kind') == 'CallExpr' and is_std_move(s))
    temp = src.get('valueCategory') == 'prvalue' or strip(src).get('kind') in ('CXXConstructExpr', 'CXXTemporaryObjectExpr', 'InitListExpr')
    if sn in ('std::vector', 'std::basic_string', 'std::basic_string_view', 'std::pair', 'std::optional',
              'std::unordered_map', 'std::map', 'std::set', 'std::unordered_set', 'std::filesystem::path'):
        if sn == 'std::optional' and std_name(t1) != 'std::optional':
            if strip(src).get('kind') in ('CXXNullPtrLiteralExpr',) or eb.code == 'null':
                return E('%s.reset()' % fn.obj(ea), t)
            return E('%s.Set(%s)' % (fn.obj(ea), eb.code), t)
        if sn in ('std::basic_string', 'std::basic_string_view') and std_name(t1) not in ('std::basic_string', 'std::basic_string_view'):
            return E('%s.Assign(%s)' % (fn.obj(ea), eb.code), t)
        if moving and not temp:
            return E('%s.MoveFrom(%s)' % (fn.obj(ea), eb.code), t)
        if temp:
            return E('%s.MoveFrom(%s)' % (fn.obj(ea), eb.code), t)
        return E('%s.Assign(%s)' % (fn.obj(ea), eb.code), t)
    if sn == 'std::array':
        return E('%s.Arr.Assign(%s, %s)' % (CPP, ea.code, eb.code), t)
    if sn == 'std::variant':
        if std_name(t1) == 'std::variant':
            return E('%s.%s(%s)' % (fn.obj(ea), 'MoveFrom' if moving else 'Assign', eb.code), t)
        # the converting assignment picks the alternative of the value's type
        return E('%s.Set(%s)' % (fn.obj(ea), eb.code), t)
    if sn in ('std::unique_ptr', 'std::shared_ptr', 'std::function', '__gnu_cxx::__normal_iterator',
              'std::chrono::duration', 'std::chrono::time_point', 'std::reverse_iterator',
              'std::_Rb_tree_iterator', 'std::_Rb_tree_const_iterator', 'std::__detail::_Node_iterator',
              'std::__detail::_Node_const_iterator', 'std::__exception_ptr::exception_ptr', 'std::exception_ptr'):
        if sn == 'std::unique_ptr':
            src_ = '%s.Mv.TakeRef(ref %s)' % (CPP, eb.code) if (moving and eb.lv is True) else eb.code
            if src_ == 'null' or eb.code.startswith('default('):
                src_ = 'null'
            return E('%s.Mv.Reset(ref %s, %s)' % (CPP, ea.code, src_), t)
        return E('(%s = %s)' % (ea.code, eb.code), t, True)
    if sn in ('std::atomic',):
        return E('(%s.v = %s)' % (fn.obj(ea), eb.code), t)
    if sn in ('std::basic_ostringstream', 'std::basic_stringstream', 'std::basic_istringstream', 'std::basic_ifstream',
              'std::basic_ofstream'):
        return E('%s.MoveFrom(%s)' % (fn.obj(ea), eb.code), t)
    return todo(fn, 'operator= on ' + (sn or repr(t0)), n)


def stream_op(fn, nm, args, t, n):
    """os << a << b ... and is >> x: flatten the chain into calls."""
    ea = fn.x(args[0])
    s0 = strip(args[1])
    if s0.get('kind') == 'CallExpr' and nm == 'operator<<':
        from std_cs import unwrap_callee
        k0 = kids(s0)
        rd0 = unwrap_callee(k0[0]).get('referencedDecl') or {}
        if rd0.get('name') in ('setprecision', 'setw', 'setfill') and len(k0) == 2:
            return E('%s.Manip_%s(%s)' % (fn.obj(ea), rd0['name'], fn.x(k0[1]).code), t)
        if rd0.get('name') == 'quoted' and len(k0) >= 2:
            return E('%s.Manip_quoted(%s)' % (fn.obj(ea), ', '.join(fn.x(x).code for x in k0[1:2])), t)
    eb = fn.x(args[1])
    t1 = C.of(args[1]).strip()
    if nm == 'operator<<':
        s = strip(args[1])
        rd = s.get('referencedDecl') or {}
        if rd.get('name') in ('endl', 'flush', 'fixed', 'scientific', 'left', 'right', 'boolalpha', 'noboolalpha',
                              'defaultfloat', 'showpoint', 'noshowpoint') or eb.code.startswith('__libfn:'):
            nmx = rd.get('name') or eb.code[8:]
            return E('%s.Manip_%s()' % (fn.obj(ea), nmx), t)
        cs = fn.cs.cs(t1) if not isinstance(t1, C.Opaque) else None
        if cs is None:
            return todo(fn, 'stream << of opaque', n)
        return E('%s.Put(%s)' % (fn.obj(ea), eb.code), t)
    if nm == 'operator>>':
        if eb.code.startswith('__libfn:'):
            return E('%s.Manip_%s()' % (fn.obj(ea), eb.code[8:]), t)      # is >> std::ws
        if eb.lv is True and fn.cs.by_ref(t1) and not fn.cs.is_class(t1):
            return E('%s.Get(ref %s)' % (fn.obj(ea), eb.code), t)
        if isinstance(eb.lv, str):
            return todo(fn, 'stream >> into a char element', n)
        return E('%s.Get(%s)' % (fn.obj(ea), eb.code), t)
    return todo(fn, nm, n)


# ---------------------------------------------------------------------------------------------- construction
def construct(fn, t, n, args):
    sn = std_name(t)
    ps = None
    from std_cs import sig_params
    ps = sig_params(tstr(n.get('ctorType'))) or []
    a = lib_args(fn, args)
    c = [e.code for e in a]
    cst = fn.cs.cs(t)
    if sn in ('std::__exception_ptr::exception_ptr', 'std::exception_ptr'):
        if not c:
            return E('null', t)              # an empty exception_ptr (System.Exception, see std_cs)
        if len(c) == 1:
            return E(c[0], t)                # a copy: the same exception object
    one_same = len(ps) == 1 and isinstance(ps[0], (C.LRef, C.RRef)) and len(a) == 1 and \
        (std_name(ps[0].t.strip()) == sn or (isinstance(ps[0].t.strip(), C.Rec) and
                                                ps[0].t.strip().name.split('::')[-1] == (sn or '').split('::')[-1]))
    if one_same:
        e = a[0]
        src = args[0]
        if isinstance(ps[0], C.RRef):
            s = strip(src)
            if e.lv is True and (src.get('valueCategory') == 'xvalue' or (s.get('kind') == 'CallExpr' and is_std_move(s))):
                if sn in ('std::unique_ptr', 'std::shared_ptr', 'std::function'):
                    return E('%s.Mv.TakeRef(ref %s)' % (CPP, e.code), t)
                return E('%s.Mv.Take(ref %s)' % (CPP, e.code), t)
            return E(e.code, t)
        if e.lv is True or src.get('valueCategory') == 'lvalue':
            from std_cs import copy_of
            return E(copy_of(fn, e.code, t), t)
        return E(e.code, t)
    if sn in ('std::stack', 'std::queue', 'std::variant', 'std::error_code') and not a:
        return E('new %s()' % cst, t)
    if sn == 'std::tuple' and len(t.args) > 3:
        return E('new %s.TupN(%s)' % (CPP, ', '.join('(object)(%s)' % fn.value_for(t.args[i], e, x)
                                                     for i, (e, x) in enumerate(zip(a, args)))), t)
    if sn == 'std::variant' and len(a) == 1:
        return E('new %s(%s)' % (cst, c[0]), t)
    if sn == 'std::vector':
        el = t.args[0]
        from std_cs import elem_maker
        mk = elem_maker(fn.cs, el)
        if not a:
            return E('new %s(%s)' % (cst, mk or ''), t)
        if len(a) == 1 and std_name(C.of(args[0]).strip()) == 'std::initializer_list':
            return E('new %s(%s)' % (cst, c[0]), t)
        if len(a) == 1:
            return E('new %s((ulong)(%s)%s)' % (cst, c[0], ', ' + mk if mk else ''), t)
        if len(a) == 2 and std_name(C.of(args[0]).strip()) == '__gnu_cxx::__normal_iterator':
            return E('%s.Algo.VecFrom(%s, %s)' % (CPP, c[0], c[1]), t)
        if len(a) == 2 and isinstance(C.of(args[0]).strip(), C.Ptr):
            return E('%s.Algo.VecFromPtr(%s, %s)' % (CPP, c[0], c[1]), t)
        if len(a) == 2:
            return E('new %s((ulong)(%s), %s)' % (cst, c[0], c[1]), t)
    if sn in ('std::basic_string', 'std::basic_string_view'):
        if not a:
            return E('new %s.Str()' % CPP, t)
        return E('new %s.Str(%s)' % (CPP, ', '.join(c)), t)
    if sn == 'std::pair':
        if not a:
            return E('new %s()' % cst, t)
        vals = [fn.value_for(t.args[i], e, x) for i, (e, x) in enumerate(zip(a, args))]
        return E('new %s(%s)' % (cst, ', '.join(vals)), t)
    if sn == 'std::optional':
        if not a or c[0] == 'null':
            return E('new %s()' % cst, t)
        return E('new %s(%s)' % (cst, fn.value_for(t.args[0], a[0], args[0])), t)
    if sn == 'std::function':
        if not a or c[0] == 'null':
            return E('null', t)
        at0 = C.of(args[0]).strip()
        if isinstance(at0, C.Closure) and fn.cs.cs(at0) == 'object' and not LAMBDA_CAST.match(c[0]):
            # a closure held as object (returned by another lambda, a template parameter): the delegate it is
            return E('((%s)%s)' % (cst, c[0]), t)
        return E(c[0], t)
    if sn in ('std::unique_ptr', 'std::shared_ptr'):
        if not a or c[0] == 'null':
            return E('null', t)
        return E(c[0], t)
    if sn in ('std::runtime_error', 'std::invalid_argument', 'std::out_of_range', 'std::logic_error', 'std::exception'):
        return E('new %s(%s)' % (cst, ', '.join(c)), t)
    if sn in ('std::unordered_map', 'std::map', 'std::set', 'std::unordered_set'):
        if not a:
            from std_cs import default_value as std_default
            return E(std_default(fn.cs, t) or 'new %s()' % cst, t)       # with an element maker if it needs one
        return E('new %s(%s)' % (cst, ', '.join(c)), t)
    if sn in ('std::basic_ostringstream', 'std::basic_stringstream', 'std::basic_istringstream', 'std::basic_ifstream',
              'std::basic_ofstream', 'std::basic_fstream'):
        return E('new %s(%s)' % (cst, ', '.join(c)), t)
    if sn == 'std::filesystem::path':
        return E('new %s.FsPath(%s)' % (CPP, ', '.join(c)), t)
    if sn in ('std::atomic',):
        return E('new %s(%s)' % (cst, ', '.join(c)), t)
    if sn in ('std::mutex', 'std::recursive_mutex', 'std::shared_mutex', 'std::lock_guard', 'std::unique_lock',
              'std::scoped_lock', 'std::shared_lock'):
        return E('new %s()' % cst, t)
    if sn == '__gnu_cxx::__normal_iterator':
        if a:
            return E(c[0], t)
        return E('default(%s)' % cst, t)
    if sn in ('std::chrono::duration', 'std::chrono::time_point'):
        if not a:
            return E('default(%s)' % cst, t)
        return E('new %s(%s)' % (cst, c[0]), t)
    if sn == 'std::array':
        if not a:
            return E(fn.cs.default_value(t), t)
    if sn == 'std::initializer_list' and not a:
        return E('new %s[0]' % fn.cs.cs(t.args[0], 'elem'), t)
    if sn == 'std::span':
        if not c:
            return E('default(%s)' % cst, t)
        return E('%s.Std.Span<%s>(%s)' % (CPP, fn.cs.cs(t.args[0], 'elem'), ', '.join(c)), t)
    if sn in ('std::hash', 'std::equal_to', 'std::less'):
        return E('new %s()' % cst, t)
    if not a and cst.startswith(CPP + '.') and not cst.startswith(CPP + '.Ptr<'):
        return E('new %s()' % cst, t)          # a default-constructed library object (thread, condition_variable)
    return todo(fn, 'construct %s with %d arguments' % (sn or repr(t), len(a)), n)
