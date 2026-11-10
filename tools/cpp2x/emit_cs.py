#!/usr/bin/env python3
"""C# back end for the SWMM6 engine's C++ (tools/cpp2x).

  emit_cs.py DECLS.jsonl.gz CENSUS.json OUTDIR

Writes OUTDIR/<file>.cs for every engine source file with reachable code, and OUTDIR/_todo.txt listing every
construct the back end could not translate yet (each also becomes a NotImplementedException in the code, so the
C# compiles while the list is worked down).

Mapping, in short: engine classes are C# classes with explicit copy semantics (Clone = copy constructor, Assign =
copy assignment, MoveFrom = move); each C++ constructor is an __init_ method called by a static factory; free
functions are static methods of a class _F in the namespace's C# namespace; numeric references are C# refs;
std::vector is Cpp.Vec with a ref indexer; std::string is Cpp.Str (bytes); numeric pointers are Cpp.Ptr (array
and offset); char pointers are Cpp.CPtr; object pointers and smart pointers are plain references.
Every implicit conversion clang records becomes an explicit C# cast, so arithmetic follows C++ exactly.
Written by Claude (Anthropic); see AI-USE.md."""
import collections, json, os, re, sys

import ctypes as C
from model import Model, FUNC_KINDS, RECORD_KINDS, kids, body_of, tstr
from names import ident, ns_name, type_suffix, method_base_name, CS_KEYWORDS
import std_cs



def lambda_captured_keys(n):
    """Keys of the variables that lambdas inside n refer to."""
    out = set()
    for lam in find_lambdas(n):
        stack = list(lam.get('inner', ()))
        while stack:
            x = stack.pop()
            if x.get('kind') == 'DeclRefExpr':
                k = (x.get('referencedDecl') or {}).get('key')
                if k:
                    out.add(k)
            stack.extend(x.get('inner', ()))
    return out


def find_lambdas(n):
    """The LambdaExpr nodes in a declaration (nested ones too)."""
    out = []
    stack = [n]
    while stack:
        x = stack.pop()
        if x.get('kind') == 'LambdaExpr':
            out.append(x)
        stack.extend(x.get('inner', ()))
    return out

# Functions written by hand in cs/runtime/Hand_*.cs instead of translated. The output writer thread: the ports
# are single-threaded, so IOThread runs each snapshot's writes at once, in posting order, as the engine's own
# __EMSCRIPTEN__ build of IOThread.cpp does. The writes and their order are the same as the threaded queue's.
HAND = {('openswmm::IOThread', n) for n in ('start', 'post', 'stop', 'flush', 'run')}
# simd::multiply's AVX2 loop multiplies four lanes at a time: the same products as the scalar tail
HAND |= {('openswmm::simd', 'multiply')}
HAND |= {('openswmm::PluginFactory', n) for n in ('get_library_directory', 'platform_load', 'platform_unload',
                                                  'platform_sym', 'platform_error')}

class Todo(Exception):
    pass


def ty(n):
    return C.of(n)


def lit_double(v):
    """A C# double literal for clang's value string, exact (clang prints the shortest round-trip form)."""
    s = str(v)
    if s in ('inf', '+inf'):
        return 'double.PositiveInfinity'
    if s == '-inf':
        return 'double.NegativeInfinity'
    if s.lower() == 'nan':
        return 'double.NaN'
    if not re.search(r'[.eE]', s):
        s += '.0'
    return s


class E:
    """A translated expression: C# code, its C++ type, and whether it is an lvalue C# can take a ref to."""
    __slots__ = ('code', 't', 'lv')

    def __init__(self, code, t=None, lv=False):
        self.code, self.t, self.lv = code, t, lv

    def __repr__(self):
        return self.code


class CS:
    def __init__(self, model):
        self.m = model
        self.todos = collections.Counter()
        self.todo_where = {}
        self._rec_names = {}
        self._fn_names = {}
        self._delegates = {}
        self.lambdas = {}          # closure type -> its operator() (for default arguments at calls)
        # every lambda in the reachable code, by closure type: its call operators (one, or one per instantiation of
        # a generic lambda), so a parameter or field of a closure type has a C# type before the lambda is emitted
        self.lambda_ops = {}
        for lam in model.lambdas:
            if True:
                rec = kids(lam)[0] if kids(lam) else {}
                ops = [c for c in kids(rec) if c.get('kind') == 'CXXMethodDecl' and c.get('name') == 'operator()']
                if not ops:
                    for tm in kids(rec):
                        if tm.get('kind') == 'FunctionTemplateDecl' and tm.get('name') == 'operator()':
                            ops = [c for c in kids(tm) if c.get('kind') == 'CXXMethodDecl'][1:]
                            break
                # a lambda inside a template appears once per instantiation and once in the pattern (no operators)
                if len(ops) > len(self.lambda_ops.get(tstr(lam.get('type')), [])):
                    self.lambda_ops[tstr(lam.get('type'))] = ops
        from resolve import Resolver
        self.resolver = Resolver(model)
        C.set_resolver(self.resolver)
        self.assign_names()
        self.assign_fn_names()
        from cs_record import Destroy
        self.destroy = Destroy(self)

    def arrptr_locals(self, f):
        """Keys of f's parameters and locals of type T* (T an engine class) that are subscripted or offset."""
        k = f.get('key')
        cache = self.__dict__.setdefault('_arrptr', {})
        if k in cache:
            return cache[k]
        decls = {}
        stack = [f]
        while stack:
            x = stack.pop()
            if x.get('kind') in ('ParmVarDecl', 'VarDecl') and x.get('local'):
                decls[x['local']] = x
            stack.extend(x.get('inner', ()))
        used = set()

        def ref_key(x):
            while x.get('kind') in ('ImplicitCastExpr', 'ParenExpr') and x.get('inner'):
                x = x['inner'][0]
            return (x.get('referencedDecl') or {}).get('key') if x.get('kind') == 'DeclRefExpr' else None
        stack = [f]
        while stack:
            x = stack.pop()
            kx = x.get('kind')
            ch = x.get('inner', [])
            if kx == 'ArraySubscriptExpr' and ch:
                used.add(ref_key(ch[0]))
            elif kx == 'BinaryOperator' and x.get('opcode') in ('+', '-') and len(ch) == 2:
                used.add(ref_key(ch[0]))
                used.add(ref_key(ch[1]))
            elif kx in ('UnaryOperator',) and x.get('opcode') in ('++', '--') and ch:
                used.add(ref_key(ch[0]))
            elif kx == 'CompoundAssignOperator' and x.get('opcode') in ('+=', '-=') and ch:
                used.add(ref_key(ch[0]))
            stack.extend(ch)
        def is_obj_ptr(key):
            d = decls.get(key)
            if not d:
                return False
            t = C.parse(tstr(d.get('type')))
            t = t.t if isinstance(t, (C.LRef, C.RRef)) else t
            return isinstance(t, C.Ptr) and isinstance(t.t, C.Rec) and self.m.key_of_type(t.t) in self.m.records

        def strip_c(x):
            while x.get('kind') in ('ImplicitCastExpr', 'ParenExpr', 'ExprWithCleanups', 'CStyleCastExpr',
                                    'CXXStaticCastExpr', 'CXXConstCastExpr') and x.get('inner'):
                x = x['inner'][0]
            return x
        out = {key for key in used if is_obj_ptr(key)}
        # a pointer local that takes the value of an array pointer, of pointer arithmetic or of v.data(), or that is
        # compared with or assigned an array pointer, is one too
        links = []          # (a, b): a and b hold the same kind of pointer
        seeds = set()
        for key, d in decls.items():
            if d.get('kind') == 'VarDecl' and is_obj_ptr(key) and d.get('inner'):
                init = strip_c(d['inner'][0])
                if init.get('kind') == 'BinaryOperator' and init.get('opcode') in ('+', '-'):
                    seeds.add(key)
                elif init.get('kind') == 'CXXMemberCallExpr' and init.get('inner') and \
                        strip_c(init['inner'][0]).get('name') == 'data':
                    seeds.add(key)
                elif init.get('kind') in ('CallExpr', 'CXXMemberCallExpr') and self.call_target(init) is not None and \
                        self.call_target(init).get('key') != k and self.arrptr_returning(self.call_target(init)):
                    seeds.add(key)
                elif init.get('kind') == 'ConditionalOperator':
                    seeds.add(key) if any(strip_c(b).get('kind') == 'BinaryOperator' and strip_c(b).get('opcode') in ('+', '-')
                                          for b in init.get('inner', [])[1:]) else None
                else:
                    rk2 = ref_key(init)
                    if rk2:
                        links.append((key, rk2))
        stack = [f]
        while stack:
            x = stack.pop()
            ch = x.get('inner', [])
            if x.get('kind') == 'BinaryOperator' and x.get('opcode') in ('==', '!=', '<', '<=', '>', '>=', '=') and len(ch) == 2:
                a1, b1 = ref_key(ch[0]), ref_key(ch[1])
                if a1 and b1:
                    links.append((a1, b1))
            stack.extend(ch)
        out |= {k2 for k2 in seeds if is_obj_ptr(k2)}
        changed = True
        while changed:
            changed = False
            for a1, b1 in links:
                if (a1 in out) != (b1 in out) and is_obj_ptr(a1) and is_obj_ptr(b1):
                    out.add(a1)
                    out.add(b1)
                    changed = True
        cache[k] = out
        return out

    def arrptr_fields(self):
        """Fields of type T* (T an engine class) that some function subscripts or offsets: Cpp.Ptr<T> fields."""
        if hasattr(self, '_arrptr_fields'):
            return self._arrptr_fields
        used = set()

        def field_key(x):
            while x.get('kind') in ('ImplicitCastExpr', 'ParenExpr') and x.get('inner'):
                x = x['inner'][0]
            return x.get('referencedMemberDecl') if x.get('kind') == 'MemberExpr' else None
        for f in self.m.funcs.values():
            stack = [f]
            while stack:
                x = stack.pop()
                kx = x.get('kind')
                ch = x.get('inner', [])
                if kx == 'ArraySubscriptExpr' and ch:
                    used.add(field_key(ch[0]))
                elif kx == 'BinaryOperator' and x.get('opcode') in ('+', '-') and len(ch) == 2:
                    used.add(field_key(ch[0]))
                stack.extend(ch)
        out = set()
        for k in used:
            if k in self.m.fields:
                rk, fd = self.m.fields[k]
                st = self.resolver.save()
                r0 = self.m.records[rk]
                self.resolver.set(r0.get('scope', ''), rk, r0.get('file'))
                try:
                    t = C.parse(tstr(fd.get('type')))
                finally:
                    self.resolver.restore(st)
                if isinstance(t, C.Ptr) and isinstance(t.t, C.Rec) and self.m.key_of_type(t.t) in self.m.records:
                    out.add(k)
        self._arrptr_fields = out
        return out

    def call_target(self, x):
        """The engine function a CallExpr / CXXMemberCallExpr node calls, or None."""
        ch = x.get('inner') or []
        if not ch:
            return None
        c = ch[0]
        while c.get('kind') in ('ImplicitCastExpr', 'ParenExpr') and c.get('inner'):
            c = c['inner'][0]
        k = None
        if c.get('kind') == 'MemberExpr':
            k = c.get('referencedMemberDecl')
        elif c.get('kind') == 'DeclRefExpr':
            k = (c.get('referencedDecl') or {}).get('key')
        return self.m.funcs.get(k) if k else None

    def ptr_valued_node(self, x, f, seen):
        """Syntactically, x yields an array pointer: a decayed array, v.data(), pointer arithmetic on one, an array
        pointer local of f, a call to a function returning one, or a ?: with such a branch."""
        while x.get('kind') in ('ImplicitCastExpr', 'ParenExpr', 'ExprWithCleanups', 'CStyleCastExpr', 'CXXStaticCastExpr',
                                'CXXConstCastExpr') and x.get('inner') and x.get('castKind') != 'ArrayToPointerDecay':
            x = x['inner'][0]
        k = x.get('kind')
        ch = x.get('inner') or []
        if k == 'ImplicitCastExpr' and x.get('castKind') == 'ArrayToPointerDecay':
            return True
        if k == 'DeclRefExpr':
            return (x.get('referencedDecl') or {}).get('key') in self.arrptr_locals(f)
        if k == 'BinaryOperator' and x.get('opcode') in ('+', '-') and len(ch) == 2:
            return self.ptr_valued_node(ch[0], f, seen) or self.ptr_valued_node(ch[1], f, seen)
        if k == 'ConditionalOperator' and len(ch) == 3:
            return self.ptr_valued_node(ch[1], f, seen) or self.ptr_valued_node(ch[2], f, seen)
        if k in ('CallExpr', 'CXXMemberCallExpr'):
            if k == 'CXXMemberCallExpr' and ch:
                me = ch[0]
                while me.get('kind') in ('ImplicitCastExpr', 'ParenExpr') and me.get('inner'):
                    me = me['inner'][0]
                if me.get('kind') == 'MemberExpr' and me.get('name') == 'data' and not me.get('referencedMemberDecl'):
                    return True
            g = self.call_target(x)
            return g is not None and self.arrptr_returning(g, seen)
        return False

    def arrptr_returning(self, f, seen=None):
        """f returns T* (T an engine class) and what it returns is an array pointer (a table of objects, or an
        element pointer computed from one): it returns Cpp.Ptr<T>."""
        k = f.get('key')
        cache = self.__dict__.setdefault('_arrret', {})
        if k in cache:
            return cache[k]
        seen = seen if seen is not None else set()
        if k in seen:
            return False
        seen.add(k)
        ft = C.parse(tstr(f.get('type')))
        rt = ft.ret if isinstance(ft, C.Func) else None
        res = False
        if isinstance(rt, C.Ptr) and isinstance(rt.t, C.Rec) and self.m.key_of_type(rt.t) in self.m.records:
            stack = [f]
            while stack and not res:
                x = stack.pop()
                if x.get('kind') == 'ReturnStmt' and x.get('inner'):
                    if self.ptr_valued_node(x['inner'][0], f, seen):
                        res = True
                if x.get('kind') != 'LambdaExpr':
                    stack.extend(x.get('inner', ()))
        cache[k] = res
        return res

    def arrptr_params(self, f):
        """Indices of f's parameters that are array pointers to objects."""
        ks = self.arrptr_locals(f)
        ps = [p for p in kids(f) if p.get('kind') == 'ParmVarDecl']
        return {i for i, p in enumerate(ps) if p.get('local') in ks}

    def context(self, scope, rec, file=None, func=None):
        self.resolver.set(scope, rec, file, func)

    # ------------------------------------------------------------------ names
    def assign_names(self):
        m = self.m
        self._fn_base = {}
        # record C# names and the C# path to them (namespace.Outer.Inner)
        for k, r in m.records.items():
            self._rec_names[k] = self.rec_path(k, r)
        # method names: overloaded C++ names get a signature suffix (shared down the hierarchy)
        overloaded = collections.defaultdict(set)
        for rec, ms in m.methods.items():
            by = collections.defaultdict(list)
            for mk in ms:
                f = m.d[mk]
                if f['kind'] in ('CXXConstructorDecl', 'CXXDestructorDecl'):
                    continue
                by[f.get('name')].append(mk)
            for nm, lst in by.items():
                if len(lst) > 1:
                    overloaded[nm].add(rec)
        self.overloaded_methods = {nm for nm in overloaded}
        free = collections.defaultdict(list)
        for k, f in m.funcs.items():
            if not f.get('parent'):
                free[(f.get('scope'), f.get('name'))].append(k)
        self.free_overloaded = {key for key, lst in free.items() if len(lst) > 1}
        # field names that clash with a method of the same record get a trailing underscore
        self.field_rename = {}
        for rk, r in m.records.items():
            mnames = {method_base_name(m.d[x].get('name', '')) for x in m.methods.get(rk, [])}
            cls = self.rec_simple(rk)
            for f in r.get('fields', []):
                nm = ident(f.get('name') or 'anon')
                if nm in mnames or nm == cls:
                    self.field_rename[f.get('key')] = nm + '_'

    def rec_simple(self, key):
        last = split_scope(key)[-1]
        return ident(re.sub(r'[<>, :*&]+', '_', last).strip('_'))

    def rec_path(self, key, r):
        parent = r.get('parent')
        if parent and parent in self.m.records:
            return self.rec_path(parent, self.m.records[parent]) + '.' + self.rec_simple(key)
        return 'global::' + ns_name(r.get('scope', '')) + '.' + self.rec_simple(key)

    def rec_name(self, key):
        return self._rec_names.get(key)

    def fn_name(self, f, qualify=True):
        """The C# name of a function or method (without the object)."""
        k = f.get('key')
        base = self._fn_base.get(k) if k else None
        if base is None:
            base = self.fn_base(f)
        if f.get('parent') or not qualify:
            return base
        return 'global::' + ns_name(f.get('scope', '')) + '._F.' + base

    def fn_base(self, f):
        """The C# name from the C++ name and, for overloads, the parameter types (read in f's own context)."""
        nm = f.get('name') or 'anon'
        base = method_base_name(nm)
        res = self.resolver
        st = res.save()
        rk = f.get('parent') if f.get('parent') in self.m.records else None
        res.set(f.get('scope', ''), rk, f.get('file'))
        try:
            params = [C.parse(tstr(p.get('type'))) for p in kids(f) if p.get('kind') == 'ParmVarDecl']
        finally:
            res.restore(st)
        if f.get('parent'):
            if nm in self.overloaded_methods:
                base += '__' + '_'.join(type_suffix(p) for p in params) if params else '__v'
            return base
        if (f.get('scope'), nm) in self.free_overloaded:
            base += '__' + '_'.join(type_suffix(p) for p in params) if params else '__v'
        if f.get('key', '').count('@'):
            base += '_' + ident(os.path.splitext(os.path.basename(f['key'].split('@')[-1]))[0])
        return base

    def assign_fn_names(self):
        """Every function's C# name, fixed once; template instantiations that would get the same name and
        parameters in C# (f<3> and f<5> taking arrays, or taking different lambdas) are numbered _t2, _t3, ..."""
        self._fn_base = {}
        groups = collections.defaultdict(list)
        for k in self.m.order:
            f = self.m.d.get(k)
            if not f or f['kind'] not in FUNC_KINDS or f['kind'] in ('CXXConstructorDecl', 'CXXDestructorDecl'):
                continue
            b = self.fn_base(f)
            self._fn_base[k] = b
            where = f.get('parent') or ('_F', f.get('scope'))
            groups[(where, b)].append(k)
        for (where, b), ks in groups.items():
            if len(ks) < 2:
                continue
            # one name per distinct C# method: an instantiation per parameter list (f<3>/f<5>, f<lambda1>/f<lambda2>)
            # and per template argument (f<true>/f<false>); a const twin shares its non-const method's name
            slots = []
            by_sig = collections.OrderedDict()
            for k in ks:
                f = self.m.d[k]
                sig = tuple(tstr(p.get('type')) for p in kids(f) if p.get('kind') == 'ParmVarDecl')
                by_sig.setdefault(sig, []).append(k)
            for sig, kk in by_sig.items():
                nonconst = [k for k in kk if not tstr(self.m.d[k].get('type')).rstrip().endswith('const')]
                const = [k for k in kk if k not in nonconst]
                if nonconst and const and self.m.d[kk[0]].get('parent'):
                    for i in range(max(len(nonconst), len(const))):
                        slots.append([x[i] for x in (nonconst, const) if i < len(x)])
                else:
                    slots += [[k] for k in kk]
            for i, slot in enumerate(slots):
                if i:
                    for k in slot:
                        self._fn_base[k] = b + '_t%d' % (i + 1)

    def var_name(self, v):
        nm = ident(v.get('name') or 'anon')
        if v.get('parent'):
            return self.rec_name(v['parent']) + '.' + nm
        if '@' in v.get('key', ''):
            nm += '_' + ident(os.path.splitext(os.path.basename(v['key'].split('@')[-1]))[0])
        return 'global::' + ns_name(v.get('scope', '')) + '._F.' + nm

    def field_name(self, key, name):
        return self.field_rename.get(key) or ident(name or 'anon')

    def todo(self, what, n=None):
        self.todos[what] += 1
        if what not in self.todo_where and n is not None:
            self.todo_where[what] = n.get('line')
        raise Todo(what)

    # ------------------------------------------------------------------ types
    def cs(self, t, role='value'):
        """C# spelling of C++ type t. role: 'value', 'param', 'field', 'ret', 'elem'."""
        if isinstance(t, C.LRef) or isinstance(t, C.RRef):
            inner = t.t
            if self.by_ref(inner) and role in ('param', 'ret', 'local'):
                return 'ref ' + self.cs(inner, 'value')
            return self.cs(inner, role)
        if isinstance(t, C.Builtin):
            return BUILTIN_CS.get(t.name) or self.todo('builtin type ' + t.name)
        if isinstance(t, C.ArrPtr):
            return 'global::Cpp.Ptr<%s>' % self.cs(t.t, 'elem')
        if isinstance(t, C.Ptr):
            p = t.t
            if isinstance(p, C.Builtin) and p.name in ('char', 'signed char', 'unsigned char'):
                return 'global::Cpp.CPtr'
            if isinstance(p, C.Builtin) and p.name == 'void':
                return 'object'
            if isinstance(p, C.Func):
                return self.delegate(p)
            if isinstance(p, C.Rec) and (self.m.key_of_type(p) in self.m.records or std_cs.is_std_class(p)):
                return self.cs(p, role)
            if isinstance(p, C.Ptr) and isinstance(p.t, C.Builtin) and p.t.name == 'char':
                return 'global::Cpp.Ptr<global::Cpp.CPtr>'
            if isinstance(p, C.Rec) and std_cs.is_file(p):
                return 'global::Cpp.CFile'
            return 'global::Cpp.Ptr<%s>' % self.cs(p, 'elem')
        if isinstance(t, C.Arr):
            if isinstance(t.t, C.Builtin) and t.t.name == 'char':
                return 'global::Cpp.CPtr'
            return self.cs(t.t, 'elem') + '[]'
        if isinstance(t, C.Func):
            return self.delegate(t)
        if isinstance(t, C.Closure):
            # a lambda's own variable is its delegate (var); anywhere else a closure type is a template parameter
            # (template<class F> void each(F fn)) whose instantiations share one spelling, so it is held as object
            # and each call casts it to the delegate of the operator() it calls
            ops = self.lambda_ops.get(repr(C.Closure(t.where)))
            if role == 'local':
                return 'var'
            return 'object'
        if isinstance(t, C.MemPtr):
            # a pointer to data member is a function from the object to a reference to its field
            return 'global::Cpp.MemPtr<%s, %s>' % (self.cs(t.cls), self.cs(t.t, 'elem'))
        if isinstance(t, C.Rec) and '(unnamed enum at' in t.name:
            return 'int'                    # an anonymous enum's constants are ints here
        if isinstance(t, C.Rec):
            k = self.m.key_of_type(t)
            if k in self.m.records:
                return self.rec_name(k)
            if k in self.m.enums:
                return self.enum_name(k)
            if t.name in self.m.enums:
                return self.enum_name(t.name)
            s = std_cs.cs_type(self, t)
            if s:
                return s
            return self.todo('type ' + re.sub(r'<.*', '<>', t.name))
        if isinstance(t, C.Opaque) and '(unnamed enum at' in str(getattr(t, 's', '')):
            return 'int'                    # an anonymous enum's constants are ints here
        if isinstance(t, C.Opaque):
            return self.todo('unparsed type ' + re.sub(r'\s+', ' ', str(getattr(t, 's', t)))[:80])
        return self.todo('type kind ' + type(t).__name__)

    def enum_name(self, k):
        e = self.m.enums[k]
        parent = e.get('parent')
        nm = self.rec_simple(k)
        if parent and parent in self.m.records:
            return self.rec_name(parent) + '.' + nm
        return 'global::' + ns_name(e.get('scope', '')) + '.' + nm

    def by_ref(self, t):
        """Does a C++ reference to t need a C# ref (t is a value type in C#)?"""
        t = t.strip() if isinstance(t, (C.LRef, C.RRef)) else t
        if isinstance(t, C.Builtin):
            return t.name != 'void'
        if isinstance(t, C.Ptr):
            # pointers to objects are references; numeric pointers are Ptr structs, char pointers CPtr structs
            p = t.t
            if isinstance(p, C.Rec) and (self.m.key_of_type(p) in self.m.records or std_cs.is_std_class(p)):
                return True       # rebinding the pointer through a reference needs ref too
            return True
        if isinstance(t, C.Rec):
            k = self.m.key_of_type(t)
            if k in self.m.enums or t.name in self.m.enums:
                return True
            return std_cs.is_value_type(t)
        return False

    def delegate(self, f):
        """A delegate type for a C++ function type (std::function and function pointers)."""
        try:
            params = [self.cs(p, 'param') for p in f.params]
            ret = self.cs(f.ret, 'ret')
        except Todo:
            raise
        key = (ret, tuple(params))
        if key not in self._delegates:
            self._delegates[key] = 'FN%d' % len(self._delegates)
        return 'global::Gen.' + self._delegates[key]

    def is_class(self, t):
        """Is t (after references) a C# reference type with C++ value semantics (needs Clone on copy)?"""
        t = t.strip()
        if isinstance(t, C.Rec):
            k = self.m.key_of_type(t)
            if k in self.m.records:
                return True
            return std_cs.is_copyable_class(t)
        if isinstance(t, C.Arr):
            return True
        return False

    def default_value(self, t, role='value'):
        """C++ default-initialization of a value of type t, in C#."""
        t0 = t
        t = t.strip() if not isinstance(t, C.Arr) else t
        if isinstance(t, C.Builtin):
            return {'bool': 'false', 'double': '0.0', 'float': '0.0f'}.get(t.name, '0')
        if isinstance(t, C.Rec) and '(unnamed enum at' in t.name:
            return '0'
        if isinstance(t, C.ArrPtr):
            return 'default(%s)' % self.cs(t)
        if isinstance(t, C.Ptr):
            if isinstance(t.t, C.Rec) and (self.m.key_of_type(t.t) in self.m.records or std_cs.is_std_class(t.t)):
                return 'null'
            if isinstance(t.t, C.Builtin) and t.t.name == 'void':
                return 'null'
            if isinstance(t.t, C.Func):
                return 'null'
            if isinstance(t.t, C.Rec) and std_cs.is_file(t.t):
                return 'null'
            return 'default(%s)' % self.cs(t)
        if isinstance(t, C.Arr):
            if isinstance(t.t, C.Builtin) and t.t.name == 'char':
                return 'global::Cpp.R.Buf(%d)' % (t.n or 1)
            if t.n is None:
                return 'null'
            if isinstance(t.t, C.Arr) and isinstance(t.t.t, C.Builtin) and t.t.t.name == 'char':
                return 'global::Cpp.R.Bufs(%d, %d)' % (t.n, t.t.n or 1)
            if isinstance(t.t, C.Arr):
                return 'global::Cpp.Arr.Make2<%s>(%d, %d)' % (self.cs(t.t.t, 'elem'), t.n, t.t.n)
            return 'global::Cpp.Arr.Make<%s>(%d)' % (self.cs(t.t, 'elem'), t.n)
        if isinstance(t, C.Rec):
            k = self.m.key_of_type(t)
            if k in self.m.enums or t.name in self.m.enums:
                return 'default(%s)' % self.cs(t)
            if k in self.m.records:
                return 'new %s()' % self.rec_name(k)
            d = std_cs.default_value(self, t)
            if d:
                return d
        if isinstance(t, C.Func):
            return 'null'
        return 'default(%s)' % self.cs(t0)


BUILTIN_CS = {
    'void': 'void', 'bool': 'bool', 'char': 'sbyte', 'signed char': 'sbyte', 'unsigned char': 'byte', 'short': 'short',
    'unsigned short': 'ushort', 'int': 'int', 'unsigned int': 'uint', 'long': 'long', 'unsigned long': 'ulong',
    'long long': 'long', 'unsigned long long': 'ulong', 'float': 'float', 'double': 'double', 'long double': 'double',
    'wchar_t': 'int', 'char16_t': 'ushort', 'char32_t': 'uint', 'char8_t': 'byte', 'std::nullptr_t': 'object',
}

INT_SIZES = {'bool': 1, 'char': 1, 'signed char': 1, 'unsigned char': 1, 'short': 2, 'unsigned short': 2, 'int': 4,
             'unsigned int': 4, 'long': 8, 'unsigned long': 8, 'long long': 8, 'unsigned long long': 8, 'float': 4,
             'double': 8, 'long double': 16, 'wchar_t': 4, 'char16_t': 2, 'char32_t': 4, 'char8_t': 1}


def split_scope(key):
    """'a::b<c::d>::e' -> ['a', 'b<c::d>', 'e'] (template arguments are not split)."""
    out, depth, cur = [], 0, ''
    i = 0
    while i < len(key):
        ch = key[i]
        if ch == '<':
            depth += 1
        elif ch == '>':
            depth -= 1
        if depth == 0 and key.startswith('::', i):
            out.append(cur)
            cur = ''
            i += 2
            continue
        cur += ch
        i += 1
    out.append(cur)
    return out


# ====================================================================== driver
class Emitter:
    """Functions, records and files."""

    def __init__(self, cs):
        self.cs, self.m = cs, cs.m
        from cs_record import RecordEmitter
        self.rec = RecordEmitter(cs)
        self.files = collections.defaultdict(lambda: collections.defaultdict(list))   # file -> (ns, class path) -> lines
        self.class_mods = {}

    def param_list(self, fn, params):
        out = []
        for x in params:
            t = fn.arr_t(x.get('local'), C.parse(tstr(x.get('type'))))
            nm = fn.names.get(x.get('local')) or fn.declare(x.get('local'), x.get('name') or 'p', t)
            cst = self.cs.cs(param_type(t), 'param')
            if cst.startswith('ref '):
                fn.refs.add(x.get('local'))
            out.append('%s %s' % (cst, nm))
        return ', '.join(out)

    def param_pass(self, fn, x):
        nm = fn.names.get(x.get('local'))
        return ('ref ' + nm) if x.get('local') in fn.refs else nm

    def modifiers(self, f, rk):
        mods = ['public']
        if f.get('storageClass') == 'static' or not rk:
            mods.append('static')
            return mods
        if f.get('pure'):
            mods.append('abstract')
            return mods
        if f.get('virtual') or self.overrides(f, rk):
            mods.append('override' if self.overrides(f, rk) else 'virtual')
        return mods

    def overrides(self, f, rk):
        nm = self.cs.fn_name(f)

        def norm(t):
            # `const class IPluginComponentInfo *` in the base and `const openswmm::IPluginComponentInfo *` in
            # the derived class are one type: drop elaborated keywords and namespace qualifiers
            return re.sub(r'\b[A-Za-z_]\w*::', '', re.sub(r'\b(class|struct|enum|union) ', '', t)).replace(' ', '')
        sig = [norm(tstr(p.get('type'))) for p in kids(f) if p.get('kind') == 'ParmVarDecl']
        seen = set()
        stack = [b for b, _ in self.m.bases(rk)]
        while stack:
            b = stack.pop()
            if b in seen or b not in self.m.records:
                continue
            seen.add(b)
            for mk in self.m.methods.get(b, []):
                g = self.m.d[mk]
                if g['kind'] == 'CXXMethodDecl' and g.get('virtual') and self.cs.fn_name(g) == nm and \
                        [norm(tstr(p.get('type'))) for p in kids(g) if p.get('kind') == 'ParmVarDecl'] == sig:
                    return True
            stack += [x for x, _ in self.m.bases(b)]
        return False

    def function(self, f):
        """C# lines of one function or method; returns (file, (namespace, class path), lines)."""
        from cs_func import Fn
        rk = f.get('parent') if f.get('parent') in self.m.records else None
        self.cs.context(f.get('scope', ''), rk, f.get('file'), f.get('key') if f.get('key') in self.m.local_types else None)
        fn = Fn(self.cs, f, rk)
        params = [x for x in kids(f) if x.get('kind') == 'ParmVarDecl']
        ft = C.parse(tstr(f.get('type')))
        ret = ft.ret if isinstance(ft, C.Func) else C.Builtin('void')
        if isinstance(ret, C.Ptr) and self.cs.arrptr_returning(f):
            ret = C.ArrPtr(ret.t)                 # it returns an array pointer: Cpp.Ptr<T>
        fn.ret_t = ret
        name = self.cs.fn_name(f, qualify=False)
        mods = self.modifiers(f, rk)
        try:
            plist = self.param_list(fn, params)
            rcs = self.cs.cs(ret_type(ret), 'ret')
        except Todo as e:
            return ['        // TODO %s: %s' % (name, e)]
        head = '        %s %s %s(%s)' % (' '.join(mods), rcs, name, plist)
        if 'abstract' in mods:
            return [head + ';']
        b = body_of(f)
        if b is None:
            return [head, '        {', '            throw new global::System.NotImplementedException("TODO no body: %s");' % name, '        }']
        # a ref parameter that a lambda uses: C# lambdas cannot capture ref parameters, so the body works on a
        # local copy that is written back when the function leaves (normally or by an exception)
        captured = lambda_captured_keys(b) & set(fn.refs) & {p.get('local') for p in params}
        if captured:
            ren = {}
            # in parameter order: a set's order follows Python's per-run string hash, which made regeneration
            # reorder these copy-in/copy-out lines from run to run
            for k in [p.get('local') for p in params if p.get('local') in captured]:
                nm = fn.names[k]
                ren[k] = nm
                fn.refs.discard(k)
            plist2 = plist
            for k, nm in ren.items():
                plist2 = re.sub(r'(ref [^,]* )%s(,|$)' % re.escape(nm), r'\g<1>%s__ref\2' % nm, plist2)
            head = '        %s %s %s(%s)' % (' '.join(mods), rcs, name, plist2)
            body = fn.stmt(b, 3)
            lines = [head, '        {'] + ['            var %s = %s__ref;' % (nm, nm) for nm in ren.values()] + \
                ['            try'] + body + ['            finally', '            {'] + \
                ['                %s__ref = %s;' % (nm, nm) for nm in ren.values()] + ['            }', '        }']
            for (sn, st, init, flag) in fn.statics:
                lines.insert(0, '        static %s %s = %s;' % (st, sn, init))
                if flag:
                    lines.insert(0, '        static bool %s;' % flag)
            return lines
        lines = [head] + fn.stmt(b, 2)
        for (sn, st, init, flag) in fn.statics:
            lines.insert(0, '        static %s %s = %s;' % (st, sn, init))
            if flag:
                lines.insert(0, '        static bool %s;' % flag)
        return lines

    def place(self, file, key_path, lines):
        self.files[file or '_misc'][key_path] += lines

    def class_path(self, rk):
        """(namespace, [record keys outermost first]) for a record."""
        chain = []
        k = rk
        while k and k in self.m.records:
            chain.insert(0, k)
            k = self.m.records[k].get('parent')
        ns = ns_name(self.m.records[chain[0]].get('scope', ''))
        return ns, tuple(chain)

    def emit_record(self, rk):
        r = self.m.records[rk]
        self.cs.context(r.get('scope', ''), rk, r.get('file'))
        ns, chain = self.class_path(rk)
        lines = []
        ind = 2
        lines += self.rec.field_decl_lines(rk, ind)
        lines += self.rec.ctor_lines(rk, ind, self)
        lines += self.rec.copy_lines(rk, ind, self)
        lines += self.cs.destroy.lines(rk, ind)
        lines += self.functor_lines(rk)
        self.class_mods[rk] = self.rec
        self.place(r.get('file'), (ns, chain), lines)

    def functors(self, rk):
        """A function object given to a container (a hasher, key-equal or comparator): the runtime interface it
        implements and the adapter method calling its operator(). [(interface, adapter line)]"""
        out = []
        for mk in self.m.methods.get(rk, []):
            g = self.m.d[mk]
            if g['kind'] != 'CXXMethodDecl' or g.get('name') != 'operator()' or (self.m.reachable is not None and mk not in self.m.reachable):
                continue
            ps = [C.parse(tstr(q.get('type'))) for q in kids(g) if q.get('kind') == 'ParmVarDecl']
            ft = C.parse(tstr(g.get('type')))
            ret = ft.ret if isinstance(ft, C.Func) else None
            try:
                pcs = [self.cs.cs(q, 'param') for q in ps]
                name = self.cs.fn_name(g, qualify=False)
            except Todo:
                continue
            if len(ps) == 1 and isinstance(ret, C.Builtin) and ret.name in ('unsigned long', 'unsigned int', 'unsigned long long', 'long'):
                out.append(('global::Cpp.IHashFn<%s>' % pcs[0],
                            'public ulong HashOf(%s k) => (ulong)%s(k);' % (pcs[0], name)))
            elif len(ps) == 2 and pcs[0] == pcs[1] and isinstance(ret, C.Builtin) and ret.name == 'bool':
                out.append(('global::Cpp.IBinFn<%s>' % pcs[0],
                            'public bool Call2(%s a, %s b) => %s(a, b);' % (pcs[0], pcs[1], name)))
        return out

    def functor_lines(self, rk):
        return ['        ' + line for _, line in self.functors(rk)]

    def emit_var(self, v):
        """A global or a static data member."""
        from cs_func import Fn
        rk = v.get('parent') if v.get('parent') in self.m.records else None
        self.cs.context(v.get('scope', ''), rk, v.get('file'))
        fn = Fn(self.cs, v, rk)
        t = C.parse(tstr(v.get('type')))
        nm = ident(v.get('name') or 'anon')
        if not rk and '@' in v.get('key', ''):
            nm += '_' + ident(os.path.splitext(os.path.basename(v['key'].split('@')[-1]))[0])
        init = [c for c in kids(v) if c.get('kind') not in ('FullComment',)]
        try:
            cst = self.cs.cs(t.strip() if isinstance(t, (C.LRef, C.RRef)) else t)
            val = fn.init_value(t, init[0]) if init else self.cs.default_value(t)
            pre = fn.pre
        except Todo as e:
            return ['        // TODO global %s: %s' % (nm, e)]
        if pre:
            return ['        public static %s %s = ((global::System.Func<%s>)(() => { %s return %s; }))();' % (
                cst, nm, cst, ' '.join(pre), val)]
        return ['        public static %s %s = %s;' % (cst, nm, val)]

    def run(self, outdir):
        m = self.m
        reach = m.reachable
        # records: reachable ones, their bases and the records they are nested in
        # every class and enum is emitted (a type the census missed, such as one in an anonymous namespace, still
        # has to exist); only functions are limited to the reachable ones
        recs = set(m.records)
        changed = True
        while changed:
            changed = False
            for k in list(recs):
                for b, _ in m.bases(k):
                    if b in m.records and b not in recs:
                        recs.add(b); changed = True
                par = m.records[k].get('parent')
                if par in m.records and par not in recs:
                    recs.add(par); changed = True
        recs = {k for k in recs if not is_lambda_record(m.records[k])}
        self.recs = recs
        for k in sorted(recs):
            try:
                self.emit_record(k)
            except Todo as e:
                self.place(m.records[k].get('file'), self.class_path(k), ['        // TODO record: %s' % e])
        # const and non-const overloads (T& at(i) / const T& at(i) const) are one C# method: keep the non-const
        by_sig = collections.defaultdict(list)
        for k in m.order:
            f = m.d.get(k)
            if f and f['kind'] == 'CXXMethodDecl' and f.get('parent'):
                sig = (f['parent'], f.get('name'), tuple(tstr(p.get('type')) for p in kids(f) if p.get('kind') == 'ParmVarDecl'))
                by_sig[sig].append(k)
        const_twin = set()
        for sig, ks in by_sig.items():
            if len(ks) > 1:
                nonconst = [k for k in ks if not tstr(m.d[k].get('type')).rstrip().endswith('const')]
                if nonconst:
                    # drop the const ones; several non-const ones are template instantiations, numbered by fn_name
                    const_twin.update(k for k in ks if k not in nonconst)
        for k in m.order:
            f = m.d.get(k)
            if not f or f['kind'] not in FUNC_KINDS:
                continue
            if f['kind'] in ('CXXConstructorDecl', 'CXXDestructorDecl'):
                continue
            if k in const_twin:
                continue
            if (f.get('isImplicit') or f.get('explicitlyDefaulted')) and f.get('name') in ('operator=',):
                continue
            par = f.get('parent')
            if par and (par not in recs):
                continue
            if (par or f.get('scope'), f.get('name')) in HAND:
                continue
            lines = self.function(f)
            if par:
                ns, chain = self.class_path(par)
                self.place(f.get('file'), (ns, chain), lines)
            else:
                self.place(f.get('file'), (ns_name(f.get('scope', '')), ()), lines)
        for k, v in m.vars.items():
            # static data members always (a default member initializer can name one the census did not see);
            # namespace-scope variables when reached
            if reach is not None and k not in reach and not v.get('parent'):
                continue
            par = v.get('parent')
            if par and par not in recs:
                continue
            lines = self.emit_var(v)
            if par:
                ns, chain = self.class_path(par)
                self.place(v.get('file'), (ns, chain), lines)
            else:
                self.place(v.get('file'), (ns_name(v.get('scope', '')), ()), lines)
        self.write(outdir)

    def class_header(self, rk):
        r = self.m.records[rk]
        nm = self.cs.rec_simple(rk)
        base = self.rec.base(rk)
        kw = 'abstract partial class' if self.rec.is_abstract(rk) else 'partial class'
        bases = []
        if base:
            bases.append(self.cs.rec_name(base))
        bases.append('global::Cpp.ICopy<%s>' % self.cs.rec_name(rk))
        exc = std_cs.exception_base(self, rk)
        if exc and not base:
            bases.insert(0, exc)
        if self.cs.destroy.rec(rk) and not self.cs.destroy.has_dispose_above(rk):
            bases.append('global::System.IDisposable')
        bases += [i for i, _ in self.functors(rk)]
        return 'public %s %s : %s' % (kw, nm, ', '.join(bases))

    def write(self, outdir):
        os.makedirs(outdir, exist_ok=True)
        for f in os.listdir(outdir):
            if f.endswith('.cs'):
                os.remove(os.path.join(outdir, f))
        enums_by_file = collections.defaultdict(list)
        for k, e in self.m.enums.items():
            enums_by_file[e.get('file')].append(k)
        written = set()
        for file, parts in sorted(self.files.items(), key=lambda kv: kv[0] or ''):
            out = ['// Generated by tools/cpp2x/emit_cs.py from the OpenSWMM engine (swmm6_rel) %s. Do not edit.' % file,
                   '#pragma warning disable', 'using System;', '']
            by_ns = collections.defaultdict(list)
            for (ns, chain), lines in parts.items():
                by_ns[ns].append((chain, lines))
            for e in enums_by_file.pop(file, []):
                ens = ns_name(self.m.enums[e].get('scope', ''))
                par = self.m.enums[e].get('parent')
                if par in self.m.records:
                    ns2, chain = self.class_path(par)
                    by_ns[ns2].append((chain, self.enum_lines(e)))
                else:
                    by_ns[ens].append(((), self.enum_lines(e, top=True)))
            for ns, items in sorted(by_ns.items()):
                out.append('namespace %s' % ns)
                out.append('{')
                for chain, lines in items:
                    if not chain:
                        if lines and lines[0].startswith('    public enum'):
                            out += lines
                            continue
                        out.append('    public static partial class _F')
                        out.append('    {')
                        out += lines
                        out.append('    }')
                        continue
                    ind = 1
                    for rk in chain:
                        out.append('    ' * ind + self.class_header(rk))
                        out.append('    ' * ind + '{')
                        ind += 1
                    out += ['    ' * (ind - 2) + l for l in lines]
                    for rk in reversed(chain):
                        ind -= 1
                        out.append('    ' * ind + '}')
                out.append('}')
            name = re.sub(r'[^A-Za-z0-9_.]', '_', (file or 'misc').replace('src/engine/', '').replace('include/', 'inc_'))
            with open(os.path.join(outdir, name + '.cs'), 'w') as g:
                g.write('\n'.join(out) + '\n')
        # enums whose file had nothing else
        for file, ks in enums_by_file.items():
            out = ['// Generated by tools/cpp2x/emit_cs.py. Do not edit.', '#pragma warning disable', '']
            for e in ks:
                par = self.m.enums[e].get('parent')
                ens = ns_name(self.m.enums[e].get('scope', ''))
                if par in self.m.records:
                    ns2, chain = self.class_path(par)
                    out.append('namespace %s {' % ns2)
                    for rk in chain:
                        out.append(self.class_header(rk) + ' {')
                    out += self.enum_lines(e)
                    out += ['}'] * len(chain)
                    out.append('}')
                else:
                    out.append('namespace %s {' % ens)
                    out += self.enum_lines(e, top=True)
                    out.append('}')
            name = re.sub(r'[^A-Za-z0-9_.]', '_', (file or 'misc').replace('src/engine/', '')) + '.enums'
            with open(os.path.join(outdir, name + '.cs'), 'w') as g:
                g.write('\n'.join(out) + '\n')
        with open(os.path.join(outdir, '_delegates.cs'), 'w') as g:
            g.write('// Generated by tools/cpp2x/emit_cs.py: delegate types for the engine\'s function types.\n')
            g.write('namespace Gen\n{\n')
            for (ret, params), nm in sorted(self.cs._delegates.items(), key=lambda kv: kv[1]):
                ps = ', '.join('%s a%d' % (p, i) for i, p in enumerate(params))
                g.write('    public delegate %s %s(%s);\n' % (ret, nm, ps))
            g.write('}\n')
        with open(os.path.join(outdir, '_todo.txt'), 'w') as g:
            for k, v in self.cs.todos.most_common():
                g.write('%6d  %s   (first at line %s)\n' % (v, k, self.cs.todo_where.get(k)))

    def enum_lines(self, ek, top=False):
        e = self.m.enums[ek]
        self.cs.context(e.get('scope', ''), e.get('parent') if e.get('parent') in self.m.records else None, e.get('file'))
        nm = self.cs.rec_simple(ek)
        under = e.get('fixedUnderlyingType')
        ut = 'int'
        if under:
            try:
                ut = self.cs.cs(C.parse(tstr(under)))
            except Todo:
                ut = 'int'
        ind = '    ' if top else '        '
        out = [ind + 'public enum %s : %s' % (nm, ut), ind + '{']
        for c in e.get('enumerators', []):
            v = None
            for x in kids(c):
                # an unscoped enum without a fixed type whose values are all nonnegative has unsigned int as its
                # underlying type, and clang wraps each initializer in a cast to it: look through the cast
                while x.get('kind') == 'ImplicitCastExpr' and kids(x):
                    x = kids(x)[0]
                if x.get('kind') == 'ConstantExpr' and 'value' in x:
                    v = x['value']
            out.append(ind + '    %s%s,' % (ident(c.get('name')), (' = ' + ('unchecked((%s)%s)' % (ut, v))) if v is not None else ''))
        out.append(ind + '}')
        return out


def param_type(t):
    """const T& of a number is passed by value."""
    if isinstance(t, C.LRef) and isinstance(t.t, (C.Builtin, C.Ptr)) and t.t.const:
        return t.t
    if isinstance(t, C.LRef) and t.t.const and isinstance(t.t, C.Rec):
        return t.t if True else t
    return t


def ret_type(t):
    if isinstance(t, C.LRef) and t.t.const and isinstance(t.t, (C.Builtin, C.Ptr)):
        return t.t
    return t


def is_lambda_record(r):
    return (r.get('definitionData') or {}).get('isLambda') or (r.get('name') or '').startswith('(lambda')


def main():
    decls, census, outdir = sys.argv[1], sys.argv[2], sys.argv[3]
    reach = set(json.load(open(census))['reachable'])
    m = Model(decls, reach)
    cs = CS(m)
    em = Emitter(cs)
    em.run(outdir)
    print('wrote %s: %d files, %d TODO kinds, %d TODO sites' % (outdir, len(em.files), len(cs.todos), sum(cs.todos.values())))
    for k, v in cs.todos.most_common(40):
        print('%6d  %s   (line %s)' % (v, k, cs.todo_where.get(k)))


if __name__ == '__main__':
    # run through the imported module, so the classes (Todo above all) are the same ones cs_func imports
    import emit_cs
    emit_cs.main()
