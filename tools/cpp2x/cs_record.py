"""Engine classes for the C# back end: fields, constructors as __init methods behind static factories, copy
semantics (Clone / Assign / MoveFrom), static members, and the methods (emitted with their bodies by the driver).

C++ construction order is kept: the base's constructor, then every field in declaration order (from the
constructor's initializer list, else the field's default member initializer, else default construction for
classes, zero for numbers), then the constructor body.
Written by Claude (Anthropic); see AI-USE.md."""
import ctypes as C
from model import kids, body_of, tstr
from names import ident, method_base_name
from emit_cs import E, Todo
from cs_func import Fn, strip_trans
import std_cs


class RecordEmitter:
    def __init__(self, cs):
        self.cs, self.m = cs, cs.m

    def fields(self, rk):
        r = self.m.records[rk]
        return [f for f in r.get('fields', []) if f.get('kind') == 'FieldDecl']

    def base(self, rk):
        bs = [b for b in self.m.bases(rk) if b[0] in self.m.records]
        if len(bs) > 1:
            # empty bases (tags, interfaces without data) can be dropped; two bases with data cannot
            withdata = [b for b in bs if self.fields(b[0]) or self.m.methods.get(b[0])]
            if len(withdata) > 1:
                self.cs.todos['multiple inheritance %s' % rk] += 1
            bs = withdata[:1] or bs[:1]
        return bs[0][0] if bs else None

    def field_decl_lines(self, rk, ind):
        p = '    ' * ind
        out = []
        for f in self.fields(rk):
            t = C.parse(tstr(f.get('type')))
            if f.get('key') in self.cs.arrptr_fields() and isinstance(t, C.Ptr):
                t = C.ArrPtr(t.t)
            nm = self.cs.field_name(f.get('key'), f.get('name'))
            try:
                if isinstance(t, (C.LRef, C.RRef)):
                    if self.cs.by_ref(t.t):
                        out.append(p + 'public global::Cpp.Box<%s> %s;   // a reference member' % (self.cs.cs(t.t), nm))
                        continue
                    out.append(p + 'public %s %s;' % (self.cs.cs(t.t), nm))
                    continue
                out.append(p + 'public %s %s;' % (self.cs.cs(t, 'field'), nm))
            except Todo as e:
                out.append(p + '// TODO field %s: %s' % (nm, e))
        return out

    def field_init(self, fn, rk, f, init_node):
        """C# statement initializing field f (in an __init method) from a ctor initializer or the default."""
        t = C.parse(tstr(f.get('type')))
        if f.get('key') in self.cs.arrptr_fields() and isinstance(t, C.Ptr):
            t = C.ArrPtr(t.t)
        nm = self.cs.field_name(f.get('key'), f.get('name'))
        src = init_node
        if src is not None and strip_trans(src).get('kind') == 'CXXDefaultInitExpr':
            src = None
        if src is None:
            dflt = [c for c in kids(f) if c.get('kind') not in ('FullComment',)]
            if f.get('hasInClassInitializer') and dflt:
                src = dflt[0]
        if src is None:
            if isinstance(t, (C.LRef, C.RRef)):
                return None
            return 'this.%s = %s;' % (nm, self.cs.default_value(t))
        if isinstance(t, (C.LRef, C.RRef)):
            if self.cs.by_ref(t.t):
                return 'this.%s = %s;' % (nm, std_cs.box_of(fn, src, t.t))
            e = fn.x(src)
            return 'this.%s = %s;' % (nm, e.code)
        if isinstance(t, C.ArrPtr):
            return 'this.%s = %s;' % (nm, std_cs.as_arrptr(fn, src, fn.x(src), t.t))
        v = fn.init_value(t, src)
        return 'this.%s = %s;' % (nm, v)

    def ctor_lines(self, rk, ind, emit_fn):
        """The C# constructors and factories of record rk."""
        p = '    ' * ind
        cls = self.cs.rec_simple(rk)
        full = self.cs.rec_name(rk)
        base = self.base(rk)
        out = []
        out.append(p + 'protected %s(global::Cpp.Raw _)%s { }' % (cls, ' : base(_)' if base else ''))
        ctors = [c for c in self.m.ctors(rk)]
        has_default = False
        for c in ctors:
            params = [x for x in kids(c) if x.get('kind') == 'ParmVarDecl']
            if c.get('deleted'):
                continue
            nm = self.ctor_name(c)
            fn = Fn(self.cs, c, rk)
            try:
                plist = emit_fn.param_list(fn, params)
            except Todo as e:
                out.append(p + '// TODO constructor %s: %s' % (nm, e))
                continue
            body = self.ctor_body(fn, rk, c, ind + 1)
            cm = self.copy_or_move(rk, c, params, fn)
            if cm:
                # X(const X&) = default / X(X&&) = default with no definition in the dump: member by member
                body = [p + '    ' + cm]
            out += statics_lines(fn, p)
            out.append(p + 'public void __init_%s(%s)' % (nm, plist))
            out.append(p + '{')
            out += body
            out.append(p + '}')
            args = ', '.join(emit_fn.param_pass(fn, x) for x in params)
            if not self.is_abstract(rk):      # an abstract class is only ever constructed as a base
                out.append(p + 'public static %s New%s(%s) { var o = new %s(global::Cpp.Raw.V); o.__init_%s(%s); return o; }' % (
                    full, '' if nm == 'd' else '_' + nm, plist, full, nm, args))
            if not params:
                has_default = True
        if not ctors:
            # no constructor seen anywhere: the implicit default constructor
            fn = Fn(self.cs, {'kind': 'CXXConstructorDecl', 'inner': []}, rk)
            out.append(p + 'public void __init_d()')
            out.append(p + '{')
            out += self.ctor_body(fn, rk, None, ind + 1)
            out.append(p + '}')
            has_default = True
        if has_default and not self.is_abstract(rk):
            out.append(p + 'public %s() : this(global::Cpp.Raw.V) { __init_d(); }' % cls)
        elif has_default:
            out.append(p + 'protected %s() : this(global::Cpp.Raw.V) { __init_d(); }' % cls)
        elif not self.is_abstract(rk):
            # no default constructor in C++: this one makes only the empty shell a move leaves behind
            out.append(p + 'public %s() : this(global::Cpp.Raw.V) { }' % cls)
        return out

    def copy_or_move(self, rk, c, params, fn):
        """For a defaulted (or implicit) copy or move constructor that clang left without member initializers, the
        C# statement doing its work; None otherwise."""
        if not (c.get('explicitlyDefaulted') or c.get('isImplicit')) or len(params) != 1:
            return None
        if any(ci.get('kind') == 'CXXCtorInitializer' for ci in kids(c)):
            return None
        pt = C.parse(tstr(params[0].get('type')))
        if not isinstance(pt, (C.LRef, C.RRef)) or self.m.key_of_type(pt.t.strip()) != rk:
            return None
        pn = fn.names.get(params[0].get('local')) or ident(params[0].get('name') or 'p')
        if isinstance(pt, C.RRef):
            return 'MoveFrom(%s);' % pn
        return '__CopyFrom_%s(%s);' % (self.cs.rec_simple(rk), pn)

    def ctor_name(self, c):
        params = [C.parse(tstr(x.get('type'))) for x in kids(c) if x.get('kind') == 'ParmVarDecl']
        if not params:
            return 'd'
        from names import type_suffix
        return '_'.join(type_suffix(t) for t in params)

    def ctor_body(self, fn, rk, c, ind):
        p = '    ' * ind
        out = []
        inits = {}
        base_init = None
        delegating = None
        params = []
        if c is not None:
            params = [x for x in kids(c) if x.get('kind') == 'ParmVarDecl']
            for x in params:
                if x.get('local') not in fn.names:     # param_list declared it already
                    fn.declare(x.get('local'), x.get('name'), C.parse(tstr(x.get('type'))))
                t = C.parse(tstr(x.get('type')))
                if isinstance(t, (C.LRef, C.RRef)) and self.cs.by_ref(t.t) and not t.t.const:
                    fn.refs.add(x.get('local'))
            for ci in kids(c):
                if ci.get('kind') != 'CXXCtorInitializer':
                    continue
                if 'anyInit' in ci:
                    inits[ci['anyInit'].get('name')] = kids(ci)[0] if kids(ci) else None
                elif 'baseInit' in ci:
                    base_init = kids(ci)[0] if kids(ci) else None
                elif 'delegatingInit' in ci or ci.get('isDelegating'):
                    delegating = kids(ci)[0] if kids(ci) else None
        base = self.base(rk)
        try:
            if delegating is not None:
                out.append(p + std_cs.delegating_ctor(fn, rk, delegating) + ';')
            else:
                if base:
                    out.append(p + std_cs.base_ctor_call(fn, base, base_init) + ';')
                for f in self.fields(rk):
                    try:
                        s = self.field_init(fn, rk, f, inits.get(f.get('name')))
                        out += [p + x for x in fn.pre]
                        fn.pre = []
                        if s:
                            out.append(p + s)
                    except Todo as e:
                        fn.pre = []
                        out.append(p + 'throw new global::System.NotImplementedException("TODO field init %s: %s");' % (
                            f.get('name'), str(e).replace('"', "'")))
        except Todo as e:
            out.append(p + 'throw new global::System.NotImplementedException("TODO ctor init: %s");' % str(e).replace('"', "'"))
        b = body_of(c) if c is not None else None
        if b is not None:
            fn.ret_t = None
            out += fn.block(b, ind)
        return out

    def is_abstract(self, rk):
        return any(self.m.d[x].get('pure') for x in self.m.methods.get(rk, []))

    def copy_lines(self, rk, ind, emit_fn):
        """Clone (copy constructor), Assign (copy assignment) and MoveFrom (move) for rk."""
        p = '    ' * ind
        cls = self.cs.rec_name(rk)
        base = self.base(rk)
        out = []
        user_copy = None
        user_assign = None
        for x in self.m.methods.get(rk, []):
            d = self.m.d[x]
            if d['kind'] == 'CXXConstructorDecl' and not d.get('isImplicit') and body_of(d) is not None:
                ps = [q for q in kids(d) if q.get('kind') == 'ParmVarDecl']
                if len(ps) == 1:
                    pt = C.parse(tstr(ps[0].get('type')))
                    if isinstance(pt, C.LRef) and self.m.key_of_type(pt.t.strip()) == rk:
                        user_copy = d
            if d['kind'] == 'CXXMethodDecl' and d.get('name') == 'operator=' and not d.get('isImplicit') and not d.get('explicitlyDefaulted') and body_of(d) is not None:
                ps = [q for q in kids(d) if q.get('kind') == 'ParmVarDecl']
                pt = C.parse(tstr(ps[0].get('type'))) if len(ps) == 1 else None
                # the copy assignment: its parameter is this class (operator=(const std::string&) is not)
                if isinstance(pt, C.LRef) and self.m.key_of_type(pt.t.strip()) == rk:
                    user_assign = d
        newkw = 'new ' if base else ''
        out.append(p + 'public %s%s Clone()' % (newkw, cls))
        out.append(p + '{')
        if self.is_abstract(rk):
            out.append(p + '    throw new global::System.InvalidOperationException("copy of an abstract class");')
            out.append(p + '}')
        else:
            out.append(p + '    var o = new %s(global::Cpp.Raw.V);' % cls)
        if self.is_abstract(rk):
            pass
        elif user_copy is not None:
            out.append(p + '    o.__init_%s(this);' % self.ctor_name(user_copy))
        else:
            out.append(p + '    o.__CopyFrom_%s(this);' % self.cs.rec_simple(rk))
        if not self.is_abstract(rk):
            out.append(p + '    return o;')
            out.append(p + '}')
        # memberwise copy (the implicit copy constructor and copy assignment)
        out.append(p + 'protected void __CopyFrom_%s(%s s)' % (self.cs.rec_simple(rk), cls))
        out.append(p + '{')
        if base:
            out.append(p + '    __CopyFrom_%s(s);' % self.cs.rec_simple(base))
        for f in self.fields(rk):
            t = C.parse(tstr(f.get('type')))
            nm = self.cs.field_name(f.get('key'), f.get('name'))
            out.append(p + '    this.%s = %s;' % (nm, std_cs.copy_of_field(self.cs, 's.' + nm, t)))
        out.append(p + '}')
        if user_assign is not None:
            out.append(p + 'public void Assign(%s s) { %s(s); }' % (cls, self.cs.fn_name(user_assign)))
        else:
            out.append(p + 'public void Assign(%s s)' % cls)
            out.append(p + '{')
            out.append(p + '    if (ReferenceEquals(this, s)) return;')
            if base:
                out.append(p + '    base.Assign(s);')
            for f in self.fields(rk):
                t = C.parse(tstr(f.get('type')))
                nm = self.cs.field_name(f.get('key'), f.get('name'))
                out.append(p + '    ' + std_cs.assign_field(self.cs, 'this.' + nm, 's.' + nm, t))
            out.append(p + '}')
        out.append(p + 'public void MoveFrom(%s s)' % cls)
        out.append(p + '{')
        out.append(p + '    if (ReferenceEquals(this, s)) return;')
        if base:
            out.append(p + '    base.MoveFrom(s);')
        for f in self.fields(rk):
            t = C.parse(tstr(f.get('type')))
            nm = self.cs.field_name(f.get('key'), f.get('name'))
            out.append(p + '    ' + std_cs.move_field(self.cs, 'this.' + nm, 's.' + nm, t))
        out.append(p + '}')
        if not self.is_abstract(rk):
            out.append(p + 'public static %s%s MoveNew(%s s) { var o = new %s(global::Cpp.Raw.V); o.MoveFrom(s); return o; }' % (
                newkw, cls, cls, cls))
        return out


def statics_lines(fn, p):
    """The class fields holding the static locals of a constructor or destructor body."""
    out = []
    for (sn, st, init, flag) in fn.statics:
        if flag:
            out.append(p + 'static bool %s;' % flag)
        out.append(p + 'static %s %s = %s;' % (st, sn, init))
    return out


# ---------------------------------------------------------------------------------------------- destructors
# Classes whose destructors only feed the optional profiling counters (perf::*, printed only when profiling is
# switched on by environment variable): their destruction is not translated.
NO_DESTROY = {'openswmm::perf::ScopedTimer', 'openswmm::perf::GatedTimer', 'openswmm::perf::CounterBatch',
              'openswmm::perf::ProcessTimer'}
DESTROY_STREAMS = {'std::basic_ofstream', 'std::basic_ifstream', 'std::basic_fstream'}
DESTROY_HOLDERS = {'std::unique_ptr', 'std::vector', 'std::optional', 'std::array', 'std::deque', 'std::list'}


class Destroy:
    """Which C++ types have a destructor with an effect, and the C# Dispose methods that run it."""

    def __init__(self, cs):
        self.cs, self.m = cs, cs.m
        self.memo = {}
        self.derived = {}
        for k in self.m.records:
            for b, _ in self.m.bases(k):
                self.derived.setdefault(b, set()).add(k)

    def own(self, rk):
        """rk's own destructor has a body with statements."""
        d = self.m.dtor(rk)
        if not d or d.get('isImplicit') or rk in NO_DESTROY:
            return False
        b = body_of(d)
        return bool(b and kids(b))

    def rec(self, rk):
        """Destroying an rk runs code: its destructor, a member's or a base's."""
        if rk in self.memo:
            return self.memo[rk]
        self.memo[rk] = False          # cycles through pointers end here
        res = self.cs.resolver
        st = res.save()
        r0 = self.m.records[rk]
        res.set(r0.get('scope', ''), rk, r0.get('file'))
        try:
            r = self.own(rk) or any(self.type(C.parse(tstr(f.get('type')))) for f in
                                     r0.get('fields', []) if f.get('kind') == 'FieldDecl') or \
                any(b in self.m.records and self.rec(b) for b, _ in self.m.bases(rk))
        finally:
            res.restore(st)
        self.memo[rk] = r
        return r

    def poly(self, rk):
        """Deleting through an rk pointer may destroy rk or a class derived from it."""
        seen, stack = set(), [rk]
        while stack:
            k = stack.pop()
            if k in seen:
                continue
            seen.add(k)
            if k in self.m.records and self.rec(k):
                return True
            stack += list(self.derived.get(k, ()))
        return False

    def type(self, t):
        if isinstance(t, (C.LRef, C.RRef, C.Ptr)):
            return False
        if isinstance(t, C.Arr):
            return self.type(t.t)
        if not isinstance(t, C.Rec):
            return False
        k = self.m.key_of_type(t)
        if k in self.m.records:
            return self.rec(k)
        sn = std_cs.std_name(t)
        if sn in DESTROY_STREAMS:
            return True
        if sn == 'std::unique_ptr' and t.args:
            a = t.args[0]
            ak = self.m.key_of_type(a) if isinstance(a, C.Rec) else None
            return bool(ak and ak in self.m.records and self.poly(ak))
        if sn in DESTROY_HOLDERS and t.args:
            return self.type(t.args[0])
        if sn in ('std::unordered_map', 'std::map') and len(t.args) > 1:
            return self.type(t.args[1])
        return False

    def has_dispose_above(self, rk):
        for b, _ in self.m.bases(rk):
            if b in self.m.records and (self.rec(b) or self.has_dispose_above(b)):
                return True
        return False

    def lines(self, rk, ind):
        """public void Dispose(): the destructor body, then the members in reverse order, then the base."""
        if not self.rec(rk):
            return []
        p = '    ' * ind
        above = self.has_dispose_above(rk)
        out = [p + 'public %s void Dispose()' % ('override' if above else 'virtual'), p + '{']
        d = self.m.dtor(rk)
        if self.own(rk):
            fn = Fn(self.cs, d, rk)
            try:
                out += fn.stmt(body_of(d), ind + 1)
            except Todo as e:
                out.append(p + '    throw new global::System.NotImplementedException("TODO destructor: %s");' % e)
            out = statics_lines(fn, p) + out
        for f in reversed([f for f in self.m.records[rk].get('fields', []) if f.get('kind') == 'FieldDecl']):
            if self.type(C.parse(tstr(f.get('type')))):
                out.append(p + '    global::Cpp.Dtor.Run(this.%s);' % self.cs.field_name(f.get('key'), f.get('name')))
        if above:
            out.append(p + '    base.Dispose();')
        out.append(p + '}')
        return out
