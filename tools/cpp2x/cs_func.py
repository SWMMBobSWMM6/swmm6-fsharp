"""Function bodies for the C# back end: statements and expressions.
Written by Claude (Anthropic); see AI-USE.md."""
import re

import ctypes as C
from model import kids, body_of, tstr, FUNC_KINDS
from names import ident
import std_cs
from emit_cs import E, Todo, ty, lit_double, BUILTIN_CS, INT_SIZES

PASS = {'ParenExpr'}
TRANSPARENT = {'MaterializeTemporaryExpr', 'CXXBindTemporaryExpr', 'ExprWithCleanups', 'ConstantExpr',
               'CXXDefaultArgExpr_resolved', 'SubstNonTypeTemplateParmExpr', 'FullExpr'}
ARITH = {'+', '-', '*', '/', '%', '<<', '>>', '&', '|', '^'}
CMP = {'<', '>', '<=', '>=', '==', '!='}


def strip_trans(n):
    while n.get('kind') in TRANSPARENT or n.get('kind') == 'ParenExpr' or (
            n.get('kind') == 'ImplicitCastExpr' and n.get('castKind') in ('NoOp', 'LValueToRValue')):
        if not n.get('inner'):
            break
        n = n['inner'][0]
    return n


class Fn:
    def __init__(self, cs, f, rec_key=None, is_lambda=False, parent=None):
        self.cs, self.m, self.f = cs, cs.m, f
        self.rec = rec_key
        self.parent = parent
        self.names = parent.names if parent else {}       # local key -> C# name
        self.types = parent.types if parent else {}       # local key -> C++ type
        self.refs = parent.refs if parent else set()      # local keys held as C# refs
        self.byte_refs = parent.byte_refs if parent else set()   # char& locals held as `ref byte` (a string's byte)
        self.used = parent.used if parent else set()
        self.tmpn = [0] if not parent else parent.tmpn
        self.statics = parent.statics if parent else []   # hoisted static locals: (cs name, type, init code)
        self.defers = parent.defers if parent else {}     # local key -> its scope-end destroyer (using var)
        # pointers to engine objects used as arrays (p[i], p + k): these are Cpp.Ptr<T>, not object references
        self.arrptr = parent.arrptr if parent else set()
        if not parent and not f.get('_types_repaired'):
            f['_types_repaired'] = True
            self.repair_types(f)
        if not parent:
            self.arrptr |= self.cs.arrptr_locals(f)
            mark_arrptr(f, self.arrptr, self.cs.arrptr_fields(), self.cs)
        self.ret_t = None
        self.is_lambda = is_lambda
        self.labels_needed = set()
        self.pre = []         # statements to emit before the current statement (temporaries)
        self.post = []        # statements to emit after it (copy-back of boxed out-parameters of void calls)
        self.backs = []       # boxed out-parameters awaiting their call: (target lvalue, box)

    # ---------------------------------------------------------------- types
    def unresolved(self, t):
        if isinstance(t, C.Rec) and '(unnamed enum at' in t.name:
            return False
        if isinstance(t, C.Rec):
            if t.name.startswith(('std::', '__gnu_cxx::')) or t.name in ('FILE', '_IO_FILE'):
                return any(self.unresolved(a) for a in t.args if not isinstance(a, C.Value))
            k = self.m.key_of_type(t)
            return not (k in self.m.records or k in self.m.enums or t.name in self.m.enums)
        if isinstance(t, (C.Ptr, C.LRef, C.RRef, C.Arr)):
            return self.unresolved(t.t)
        if isinstance(t, C.Func):
            return self.unresolved(t.ret) or any(self.unresolved(p) for p in t.params)
        return isinstance(t, C.Opaque)

    def T(self, n):
        """The type of node n; when clang's spelling cannot be resolved, the type of what n is made of."""
        t = ty(n)
        if not self.unresolved(t):
            return t
        k = n.get('kind')
        ch = kids(n)
        if k == 'DeclRefExpr':
            key = (n.get('referencedDecl') or {}).get('key')
            if key in self.types:
                lt = self.types[key]
                return lt.t if isinstance(lt, (C.LRef, C.RRef)) else lt
        if k in ('ImplicitCastExpr', 'CXXStaticCastExpr', 'CStyleCastExpr', 'ParenExpr', 'MaterializeTemporaryExpr',
                 'CXXBindTemporaryExpr', 'ExprWithCleanups', 'ConstantExpr', 'CXXFunctionalCastExpr') and ch:
            if n.get('castKind') in (None, 'NoOp', 'LValueToRValue', 'DerivedToBase', 'UncheckedDerivedToBase'):
                return self.T(ch[0])
        if k == 'UnaryOperator' and ch:
            op = n.get('opcode')
            if op == '&':
                return C.Ptr(self.T(ch[0]))
            if op == '*':
                it = self.T(ch[0]).strip()
                return it.t if isinstance(it, C.Ptr) else it
        if k in ('CXXOperatorCallExpr', 'CXXMemberCallExpr') and ch:
            # an element of a standard container, typed by the container's arguments (clang says value_type&)
            if k == 'CXXOperatorCallExpr':
                nm, obj = std_cs.op_name(n), (ch[1] if len(ch) > 1 else None)
            else:
                me = strip_trans(ch[0])
                nm, obj = me.get('name'), (kids(me)[0] if kids(me) else None)
            if obj is not None and nm in ('operator[]', 'at', 'front', 'back', 'operator*', 'operator->'):
                ot = self.T(obj).strip()
                if isinstance(ot, C.Ptr):
                    ot = ot.t.strip()
                sn = std_cs.std_name(ot)
                if sn in ('std::vector', 'std::array', 'std::deque', 'std::span', 'std::basic_string') and ot.args:
                    return ot.args[0]
                if sn in ('std::map', 'std::unordered_map') and len(ot.args) > 1 and nm in ('operator[]', 'at'):
                    return ot.args[1]
                if sn in ('__gnu_cxx::__normal_iterator',) and ot.args and isinstance(ot.args[0], C.Ptr):
                    return ot.args[0].t
                if sn in ('std::__detail::_Node_iterator', 'std::__detail::_Node_const_iterator',
                          'std::_Rb_tree_iterator', 'std::_Rb_tree_const_iterator') and ot.args:
                    return ot.args[0]
                if sn in ('std::optional', 'std::unique_ptr', 'std::shared_ptr') and ot.args and \
                        nm in ('operator*', 'operator->'):
                    return ot.args[0]
        if k == 'MemberExpr' and ch and n.get('name') in ('first', 'second'):
            bt = self.T(ch[0]).strip()
            if isinstance(bt, C.Ptr):
                bt = bt.t.strip()
            if std_cs.std_name(bt) == 'std::pair' and len(bt.args) == 2:
                return bt.args[0 if n.get('name') == 'first' else 1]
        if k == 'ConditionalOperator' and len(ch) == 3:
            a = self.T(ch[1])
            return a if not self.unresolved(a) else self.T(ch[2])
        if k in ('VarDecl', 'ParmVarDecl') and ch:
            it = self.T(ch[0])
            if isinstance(t, C.LRef):
                r = C.LRef(it.strip() if isinstance(it, (C.LRef, C.RRef)) else it)
                return r
            if isinstance(t, C.RRef):
                return C.RRef(it.strip() if isinstance(it, (C.LRef, C.RRef)) else it)
            return it.strip() if isinstance(it, (C.LRef, C.RRef)) else it
        return t

    def repair_types(self, f):
        """clang spells some types by a member typedef the back end cannot look up (value_type&, const value_type *,
        auto deduced from them). Before emitting, give each such node the type of what it is made of (Fn.T), so
        every later ty(n) sees a resolvable type. Children first, in document order, so a local's repaired type
        reaches the names that use it."""
        locals_t = {}

        def visit(x):
            for c in x.get('inner', ()):
                visit(c)
            if 'type' not in x or not isinstance(x.get('type'), dict):
                return
            k = x.get('kind')
            if k in ('LambdaExpr', 'CXXRecordDecl', 'FunctionTemplateDecl', 'CXXMethodDecl', 'TypedefDecl', 'TypeAliasDecl'):
                return
            try:
                t = C.of(x)
            except Exception:
                return
            if not self.unresolved(t):
                return
            t2 = None
            if k == 'DeclRefExpr':
                lk = (x.get('referencedDecl') or {}).get('key')
                if lk in locals_t:
                    t2 = locals_t[lk]
            if t2 is None:
                try:
                    t2 = self.T(x)
                except Exception:
                    return
            if t2 is None or self.unresolved(t2):
                return
            sp = repr(t2)
            try:
                if self.unresolved(C.parse(sp)):
                    return
            except Exception:
                return
            x['type'] = {'qualType': sp}
            if k in ('VarDecl', 'ParmVarDecl', 'BindingDecl') and x.get('local'):
                locals_t[x['local']] = t2
                if isinstance(x.get('referencedDecl'), dict):
                    pass
        visit(f)
        # names of repaired locals (their DeclRefExprs carry the declared type in referencedDecl too)
        def fix_refs(x):
            rd = x.get('referencedDecl')
            if isinstance(rd, dict) and rd.get('key') in locals_t and isinstance(rd.get('type'), dict):
                rd['type'] = {'qualType': repr(locals_t[rd['key']])}
            for c in x.get('inner', ()):
                fix_refs(c)
        if locals_t:
            fix_refs(f)

    def label_name(self, decl_id):
        """The name of the label a goto targets (clang gives the goto only the label's declaration id)."""
        root = self
        while root.parent:
            root = root.parent
        if not hasattr(root, '_labels'):
            root._labels = {}
            stack = [root.f]
            while stack:
                x = stack.pop()
                if x.get('kind') == 'LabelStmt' and x.get('declId'):
                    root._labels[x['declId']] = x.get('name')
                stack.extend(x.get('inner', ()))
        return root._labels.get(decl_id)

    def arrptr_ref(self, n):
        """n (under casts) names a local or field that is a pointer-to-object used as an array."""
        x = strip_trans(n)
        while x.get('kind') in ('ImplicitCastExpr', 'ParenExpr') and kids(x):
            x = strip_trans(kids(x)[0])
        return bool(x.get('_arrptr'))

    def arr_t(self, key, t):
        """The type a local is declared with: an array pointer to objects becomes Ptr<T>."""
        if key in self.arrptr and isinstance(t, C.Ptr):
            return C.ArrPtr(t.t)
        return t

    # ---------------------------------------------------------------- locals
    def declare(self, key, name, t):
        base = ident(name or 'p')
        nm, i = base, 1
        while nm in self.used or nm in ('_', 'global'):
            i += 1
            nm = '%s_%d' % (base, i)
        self.used.add(nm)
        self.names[key] = nm
        self.types[key] = t
        return nm

    def tmp(self, base='t'):
        self.tmpn[0] += 1
        nm = '__%s%d' % (base, self.tmpn[0])
        self.used.add(nm)
        return nm

    # ---------------------------------------------------------------- statements
    def block(self, n, ind):
        out = []
        for c in kids(n):
            out += self.stmt(c, ind)
        return out

    def stmt(self, n, ind):
        p = '    ' * ind
        k = n.get('kind')
        try:
            if k == 'CompoundStmt':
                return [p + '{'] + self.block(n, ind + 1) + [p + '}']
            if k == 'DeclStmt':
                out = []
                for d in kids(n):
                    try:
                        out += self.local_decl(d, ind)
                    except Todo as t:
                        # the TODO throws; the variable is still declared so the rest of the function compiles
                        self.pre = []
                        out.append(p + 'throw new global::System.NotImplementedException(%s); // line %s' % (
                            std_cs.cs_string('TODO ' + str(t)), d.get('line') or n.get('line')))
                        out += self.fallback_decl(d, ind)
                return out
            if k in ('VarDecl', 'FunctionDecl', 'TypedefDecl', 'TypeAliasDecl'):
                return self.local_decl(n, ind)
            if k == 'ReturnStmt':
                if not kids(n):
                    return [p + 'return;']
                v = self.ret_value(kids(n)[0])
                # returning a local by value: the caller gets that object, so it is not destroyed here
                # (C++ elides the copy, or destroys only the moved-from shell)
                rv = strip_trans(kids(n)[0])
                while rv.get('kind') in ('CXXConstructExpr', 'ImplicitCastExpr') and kids(rv):
                    rv = strip_trans(kids(rv)[0])
                cancel = []
                rk_ = (rv.get('referencedDecl') or {}).get('key') if rv.get('kind') == 'DeclRefExpr' else None
                if rk_ in self.defers:
                    cancel = [p + '%s.Cancel();' % self.defers[rk_]]
                return self.flush(ind) + cancel + [p + 'return %s;' % v]
            if k == 'IfStmt':
                return self.if_stmt(n, ind)
            if k == 'ForStmt':
                return self.for_stmt(n, ind)
            if k == 'WhileStmt':
                ch = kids(n)
                cond = self.cond(ch[-2])
                return self.flush(ind) + [p + 'while (%s)' % cond] + self.sub(ch[-1], ind)
            if k == 'DoStmt':
                ch = kids(n)
                body = self.sub(ch[0], ind)
                cond = self.cond(ch[1])
                return [p + 'do'] + body + self.flush(ind) + [p + 'while (%s);' % cond]
            if k == 'CXXForRangeStmt':
                return self.range_for(n, ind)
            if k == 'SwitchStmt':
                return self.switch(n, ind)
            if k == 'BreakStmt':
                return [p + 'break;']
            if k == 'ContinueStmt':
                return [p + 'continue;']
            if k == 'NullStmt':
                return [p + ';']
            if k == 'GotoStmt':
                return [p + 'goto %s;' % ident(self.label_name(n.get('targetLabelDeclId')) or n.get('name') or 'L')]
            if k == 'LabelStmt':
                return [p + '%s:' % ident(n.get('name'))] + (self.stmt(kids(n)[0], ind) if kids(n) else [p + ';'])
            if k == 'CXXTryStmt':
                return self.try_stmt(n, ind)
            if k == 'AttributedStmt':
                return self.stmt(kids(n)[-1], ind)
            if k in ('OMPParallelForDirective', 'OMPParallelDirective', 'OMPForDirective', 'OMPMasterDirective',
                     'OMPSingleDirective', 'OMPCriticalDirective', 'OMPBarrierDirective', 'OMPAtomicDirective',
                     'OMPSimdDirective', 'OMPParallelForSimdDirective', 'OMPForSimdDirective', 'OMPTaskwaitDirective'):
                return self.omp(n, ind)
            if k in ('CaseStmt', 'DefaultStmt'):
                return self.todo('case outside switch', n)
            # an expression statement
            e = self.expr_stmt(n)
            out = self.flush(ind) + ([p + e + ';'] if e else [])
            out += [p + x for x in self.post]
            self.post = []
            return out
        except Todo as t:
            self.pre = []
            return [p + 'throw new global::System.NotImplementedException(%s); // line %s' % (
                std_cs.cs_string('TODO ' + str(t)), n.get('line'))]

    def flush(self, ind):
        p = '    ' * ind
        out = [p + s for s in self.pre]
        self.pre = []
        return out

    def sub(self, n, ind):
        """A loop or if body: always braced."""
        if n.get('kind') == 'CompoundStmt':
            return self.stmt(n, ind)
        return ['    ' * ind + '{'] + self.stmt(n, ind + 1) + ['    ' * ind + '}']

    def todo(self, what, n=None):
        self.cs.todo(what, n)

    def omp(self, n, ind):
        """OpenMP regions run on one thread: emit the statement they wrap."""
        for c in kids(n):
            if c.get('kind') == 'CapturedStmt':
                inner = kids(c)
                for d in inner:
                    if d.get('kind') == 'CapturedDecl':
                        # the region's statement; the declarations listed after it (the captured context, the
                        # loop body's own variables) belong to that statement and are emitted with it
                        body = [x for x in kids(d) if not x.get('kind', '').endswith('Decl')]
                        out = []
                        for b in body[:1]:
                            out += self.stmt(b, ind)
                        return out
                    return self.stmt(d, ind)
            elif c.get('kind') and not c['kind'].startswith('OMP') and not c['kind'].endswith('Clause'):
                # master, critical and single hold their statement directly (no CapturedStmt)
                return self.stmt(c, ind)
        return []

    def if_stmt(self, n, ind):
        p = '    ' * ind
        ch = kids(n)
        if n.get('isConstexpr'):
            # if constexpr in an instantiation: clang has decided; keep the branch taken
            i = int(bool(n.get('hasInit'))) + int(bool(n.get('hasVar')))
            c = ch[i]
            v = c.get('value') if c.get('kind') == 'ConstantExpr' else None
            if v is None:
                cc = strip_trans(c)
                if cc.get('kind') == 'CXXBoolLiteralExpr':
                    v = 'true' if cc.get('value') else 'false'
            if v is not None:
                taken = (str(v).lower() in ('true', '1'))
                br = ch[i + 1] if taken else (ch[i + 2] if len(ch) > i + 2 else None)
                return self.sub(br, ind) if br is not None else []
        # IfStmt children: [init] [condition variable DeclStmt] cond then [else]
        i = 0
        out = []
        pre = []
        if n.get('hasInit'):
            pre += self.stmt(ch[i], ind + 1)
            i += 1
        if n.get('hasVar'):
            pre += self.stmt(ch[i], ind + 1)
            i += 1
        cond = self.cond(ch[i])
        then = ch[i + 1]
        els = ch[i + 2] if n.get('hasElse') or len(ch) > i + 2 else None
        head = self.flush(ind + (1 if pre else 0))
        body = [('    ' if pre else '') + p + 'if (%s)' % cond] + self.sub(then, ind + (1 if pre else 0))
        if els is not None:
            if els.get('kind') == 'IfStmt' and not pre:
                eb = self.if_stmt(els, ind)
                eb[0] = p + 'else ' + eb[0].lstrip()
                body += eb
            else:
                body += [('    ' if pre else '') + p + 'else'] + self.sub(els, ind + (1 if pre else 0))
        if pre:
            return [p + '{'] + pre + head + body + [p + '}']
        return head + body

    def for_stmt(self, n, ind):
        p = '    ' * ind
        ch = kids(n)
        # ForStmt children: init, condition variable, cond, inc, body ({} for absent parts)
        init, _, cond, inc, body = ch[0], ch[1], ch[2], ch[3], ch[4]
        out = [p + '{']
        if init:
            out += self.stmt(init, ind + 1)
        c = self.cond(cond) if cond else 'true'
        cpre = self.pre
        self.pre = []
        incs = self.expr_stmt(inc) if inc else ''
        ipre = self.pre
        self.pre = []
        if cpre or ipre:
            # conditions or increments that need temporaries: lower to while(true)
            out += [p + '    while (true)', p + '    {']
            out += [p + '        ' + s for s in cpre]
            out += [p + '        if (!(%s)) break;' % c]
            bl = self.sub(body, ind + 2)
            out += bl
            out += [p + '        ' + s for s in ipre]
            if incs:
                out += [p + '        %s;' % incs]
            out += [p + '    }']
        else:
            out += [p + '    for (; %s; %s)' % (c, incs)] + self.sub(body, ind + 1)
        out.append(p + '}')
        return out

    def range_for(self, n, ind):
        p = '    ' * ind
        ch = kids(n)
        # children: init, range DeclStmt, begin DeclStmt, end DeclStmt, cond, inc, loop var DeclStmt, body
        rng_decl = kids(ch[1])[0]
        loopvar = kids(ch[6])[0]
        body = ch[7]
        rng = kids(rng_decl)[0]
        rt = ty(rng).strip()
        lt = C.parse(tstr(loopvar.get('type')))
        out = [p + '{']
        if ch[0]:
            out += self.stmt(ch[0], ind + 1)
        r = self.x(rng)
        out += self.flush(ind + 1)
        rv = self.tmp('r')
        out.append(p + '    var %s = %s;' % (rv, r.code))
        iv = self.tmp('i')
        it = std_cs.range_iter(self, rt, rv, iv, n)     # (count expr, element access (ref-able), kind)
        cnt, elem, mode = it
        nm = self.declare(loopvar.get('local'), loopvar.get('name') or 'e', lt)
        byref = isinstance(lt, (C.LRef, C.RRef)) and self.cs.by_ref(lt.t) and mode != 'value'
        decomp = loopvar.get('kind') == 'DecompositionDecl'
        if mode == 'map':
            out.append(p + '    foreach (var %s in %s)' % (nm, cnt))
            if decomp:
                out.append(p + '    {')
                out += self.bindings(loopvar, ind + 2)
                out += self.sub(body, ind + 2)
                out.append(p + '    }')
            else:
                out += self.sub(body, ind + 1)
        else:
            out.append(p + '    for (long %s = 0, %s_n = %s; %s < %s_n; %s++)' % (iv, iv, cnt, iv, iv, iv))
            out.append(p + '    {')
            str_ref = (mode == 'value' and isinstance(lt, C.LRef) and isinstance(lt.t, C.Builtin)
                       and lt.t.name == 'char' and not lt.t.const and elem.startswith('((sbyte)') and elem.endswith(')'))
            if str_ref:
                # for (char& c : s): c is the string's byte itself, so writes to c change s
                self.byte_refs.add(loopvar.get('local'))
                out.append(p + '        ref byte %s = ref %s;' % (nm, elem[len('((sbyte)'):-1]))
            elif byref:
                self.refs.add(loopvar.get('local'))
                out.append(p + '        ref %s %s = ref %s;' % (self.cs.cs(lt.t), nm, elem))
            else:
                v = elem
                if not isinstance(lt, (C.LRef, C.RRef)) and self.cs.is_class(lt):
                    v = std_cs.copy_of(self, elem, lt)
                out.append(p + '        var %s = %s;' % (nm, v))
            if decomp:
                # for (auto& [a, b] : range): the element, then one local per binding
                out += self.bindings(loopvar, ind + 2)
            bl = self.sub(body, ind + 2)
            out += bl
            out.append(p + '    }')
        out.append(p + '}')
        return out

    def switch(self, n, ind):
        p = '    ' * ind
        ch = kids(n)
        i = 0
        out = [p + '{']
        if n.get('hasInit'):
            out += self.stmt(ch[0], ind + 1)
            i += 1
        if n.get('hasVar'):
            out += self.stmt(ch[i], ind + 1)
            i += 1
        e = self.x(ch[i])
        body = ch[i + 1]
        items = kids(body) if body.get('kind') == 'CompoundStmt' else [body]
        sections = []
        for it in items:
            if it.get('kind') in ('CaseStmt', 'DefaultStmt'):
                labels = []
                x = it
                while x.get('kind') in ('CaseStmt', 'DefaultStmt'):
                    xc = kids(x)
                    if x.get('kind') == 'CaseStmt':
                        labels.append('case %s:' % self.case_label(xc[0], e.t))
                        x = xc[1] if len(xc) > 1 else {'kind': 'NullStmt'}
                    else:
                        labels.append('default:')
                        x = xc[0] if xc else {'kind': 'NullStmt'}
                if sections and not sections[-1][1]:
                    sections[-1][0].extend(labels)
                else:
                    sections.append([labels, []])
                sections[-1][1].append(x)
            elif sections:
                sections[-1][1].append(it)
        sw = e.code
        out += self.flush(ind + 1)
        out.append(p + '    switch (%s)' % sw)
        out.append(p + '    {')
        for si, (labels, stmts) in enumerate(sections):
            for lab in labels:
                out.append(p + '        ' + lab)
            out.append(p + '        {')
            for s in stmts:
                out += self.stmt(s, ind + 3)
            out.append(p + '        }')
            if not terminates(stmts):
                if si + 1 < len(sections):
                    nxt = sections[si + 1][0][0]
                    out.append(p + '        ' + ('goto default;' if nxt == 'default:' else 'goto ' + nxt[:-1] + ';'))
                else:
                    out.append(p + '        break;')
        out.append(p + '    }')
        out.append(p + '}')
        return out

    def case_label(self, n, swt):
        v = strip_trans(n)
        if n.get('kind') == 'ConstantExpr' and 'value' in n:
            val = n['value']
            st = swt.strip() if swt else None
            if isinstance(st, C.Rec) and (self.m.key_of_type(st) in self.m.enums or st.name in self.m.enums):
                return '(%s)(%s)' % (self.cs.cs(st), val)
            if isinstance(st, C.Builtin) and st.name in ('char', 'signed char'):
                return '(sbyte)(%s)' % val
            return str(val)
        return self.x(n).code

    def try_stmt(self, n, ind):
        p = '    ' * ind
        ch = kids(n)
        out = [p + 'try'] + self.sub(ch[0], ind)
        for c in ch[1:]:
            cc = kids(c)
            decl, blk = (cc[0], cc[1]) if len(cc) > 1 else ({}, cc[0])
            if decl and decl.get('kind') == 'VarDecl':
                et = C.parse(tstr(decl.get('type'))).strip()
                nm = self.declare(decl.get('local'), decl.get('name') or 'e', et)
                out.append(p + 'catch (%s %s)' % (std_cs.exception_type(self, et), nm))
            else:
                nm = self.tmp('ex')
                out.append(p + 'catch (global::System.Exception %s)' % nm)
            # std::current_exception() inside this handler is the exception it caught
            self.caught = getattr(self, 'caught', []) + [nm]
            try:
                body = self.sub(blk, ind)
            finally:
                self.caught = self.caught[:-1]
            # a debugging aid: with SWMM6_CS_TRACE set, every caught exception is printed to stderr
            body.insert(1, p + '    global::Cpp.Dbg.Caught(%s);' % nm)
            out += body
        return out

    def local_decl(self, d, ind):
        p = '    ' * ind
        k = d.get('kind')
        if k in ('TypedefDecl', 'TypeAliasDecl', 'UsingDecl', 'UsingDirectiveDecl', 'StaticAssertDecl',
                 'CXXRecordDecl', 'EnumDecl', 'NamespaceAliasDecl', 'UsingShadowDecl', 'EmptyDecl'):
            return []
        if k == 'DecompositionDecl':
            return self.decomposition(d, ind)
        if k == 'FunctionDecl':
            return []          # a function declared inside a body: nothing to emit
        if k != 'VarDecl':
            return self.todo('local ' + k, d)
        t = self.T(d)
        init = [c for c in kids(d) if c.get('kind') not in ('FullComment',)]
        if d.get('storageClass') == 'static' or d.get('tls'):
            return self.static_local(d, t, init, ind)
        nm = self.declare(d.get('local'), d.get('name'), t)
        if isinstance(t, (C.LRef, C.RRef)):
            inner = t.t
            e = self.x(init[0])
            out = self.flush(ind)
            if self.cs.by_ref(inner):
                if not e.lv:
                    # a reference bound to a temporary: an ordinary local holds it
                    return out + [p + '%s %s = %s;' % (self.cs.cs(inner), nm, e.code)]
                self.refs.add(d.get('local'))
                return out + [p + 'ref %s %s = ref %s;' % (self.cs.cs(inner), nm, e.code)]
            return out + [p + '%s %s = %s;' % (self.cs.cs(inner), nm, e.code)]
        cst = self.cs.cs(t, 'local')
        if not init:
            return [p + '%s %s = %s;' % (cst, nm, self.cs.default_value(t))] + self.defer(d, t, nm, ind)
        if isinstance(t, C.ArrPtr):
            v = std_cs.as_arrptr(self, init[0], self.x(init[0]), t.t)
            return self.flush(ind) + [p + '%s %s = %s;' % (cst, nm, v)]
        if refers_to(init[0], d.get('local')) and std_cs.std_name(t.strip()) == 'std::function':
            # std::function<...> f = [&](...) { ... f(...) ... }: C# needs f assigned before the lambda reads it
            v = self.init_value(t, init[0], d.get('init'))
            return self.flush(ind) + [p + '%s %s = null;' % (cst, nm), p + '%s = %s;' % (nm, v)]
        v = self.init_value(t, init[0], d.get('init'))
        if self.arrptr_ref(init[0]) and isinstance(t, C.Ptr):
            v = '%s.Deref' % v
        return self.flush(ind) + [p + '%s %s = %s;' % (cst, nm, v)] + self.defer(d, t, nm, ind)

    def defer(self, d, t, nm, ind):
        """A local whose destruction has an effect is destroyed when its block ends (C# runs using declarations'
        Dispose in reverse order at the end of the enclosing block, on every exit, as C++ runs destructors)."""
        if not self.cs.destroy.type(t):
            return []
        dv = self.tmp('dtor')
        self.defers[d.get('local')] = dv
        return ['    ' * ind + 'using var %s = new global::Cpp.Defer(() => global::Cpp.Dtor.Run(%s));' % (dv, nm)]

    def fallback_decl(self, d, ind):
        """After a TODO: declare the variables d declares, with default values, where their types translate."""
        p = '    ' * ind
        out = []
        vs = [d] if d.get('kind') == 'VarDecl' else [b for b in kids(d) if b.get('kind') == 'BindingDecl'] \
            if d.get('kind') == 'DecompositionDecl' else []
        for v in vs:
            if v.get('storageClass') == 'static' or not v.get('local'):
                continue
            t = self.T(v) if v.get('kind') == 'VarDecl' else C.parse(tstr(v.get('type')))
            t = t.t if isinstance(t, (C.LRef, C.RRef)) else t
            try:
                cst = self.cs.cs(t, 'value')
            except Todo:
                continue
            if cst in ('var',):
                continue
            # the name may be registered already (local_decl declares before translating the initializer)
            nm = self.names.get(v.get('local')) or self.declare(v.get('local'), v.get('name'), t)
            self.refs.discard(v.get('local'))
            out.append(p + '%s %s = default!;' % (cst, nm))
        return out

    def static_local(self, d, t, init, ind):
        """A function-scope static: a static field of the enclosing class, initialized on first pass."""
        # unique in the whole program: several functions' statics can share one C# class (_F of a namespace)
        self.cs.static_n = getattr(self.cs, 'static_n', 0) + 1
        fname = 'S_%s_%d' % (ident(d.get('name') or 's'), self.cs.static_n)
        for k in (d.get('local'), d.get('key')):
            if k:
                self.names[k] = fname
                self.types[k] = t
                self.refs.add(k)
        cst = self.cs.cs(t.strip() if isinstance(t, (C.LRef, C.RRef)) else t)
        if init:
            v = self.init_value(t, init[0], d.get('init'))
            pre = self.pre
            self.pre = []
            simple = not pre and is_constant_init(init[0])
            if simple:
                self.statics.append((fname, cst, v, None))
                return []
            flag = fname + '_done'
            self.statics.append((fname, cst, self.cs.default_value(t), flag))
            p = '    ' * ind
            return [p + 'if (!%s)' % flag, p + '{'] + ['    ' + p + s for s in pre] + [
                p + '    %s = %s;' % (fname, v), p + '    %s = true;' % flag, p + '}']
        self.statics.append((fname, cst, self.cs.default_value(t), None))
        return []

    def decomposition(self, d, ind):
        """auto [a, b] = expr; -> a temporary and one local per binding."""
        p = '    ' * ind
        ch = kids(d)
        init = [c for c in ch if c.get('kind') != 'BindingDecl']
        binds = [c for c in ch if c.get('kind') == 'BindingDecl']
        e = self.x(init[0])
        tv = self.tmp('d')
        out = self.flush(ind) + [p + 'var %s = %s;' % (tv, e.code)]
        self.names[d.get('local')] = tv
        return out + self.bindings(d, ind)

    def bindings(self, d, ind):
        """The locals of a structured binding's names, from the decomposed object already named."""
        p = '    ' * ind
        out = []
        binds = [c for c in kids(d) if c.get('kind') == 'BindingDecl']
        for bi, b in enumerate(binds):
            bt = C.parse(tstr(b.get('type')))
            nm = self.declare(b.get('local'), b.get('name'), bt)
            bexpr = [c for c in kids(b)]
            if bexpr and bexpr[0].get('kind') == 'VarDecl' and kids(bexpr[0]):
                # a tuple-like binding (std::pair, std::tuple): clang holds it in a variable initialized with
                # get<i>(e), i the binding's position
                be = E('global::Cpp.Std.get%d(%s)' % (bi, self.names.get(d.get('local'))), bt, True)
                out += self.flush(ind)
                if self.cs.by_ref(bt.strip()) and isinstance(bt, (C.LRef, C.RRef)) or self.cs.by_ref(bt):
                    self.refs.add(b.get('local'))
                    out.append(p + 'ref %s %s = ref %s;' % (self.cs.cs(bt.strip()), nm, be.code))
                else:
                    out.append(p + 'var %s = %s;' % (nm, be.code))
                continue
            if bexpr:
                be = self.x(bexpr[0])
                out += self.flush(ind)
                if be.lv and self.cs.by_ref(bt):
                    self.refs.add(b.get('local'))
                    out.append(p + 'ref %s %s = ref %s;' % (self.cs.cs(bt), nm, be.code))
                else:
                    out.append(p + 'var %s = %s;' % (nm, be.code))
            else:
                self.todo('structured binding without expression', b)
        return out

    def init_value(self, t, n, style=None):
        """The C# value initializing an object of type t from expression n (a copy if n is a named object)."""
        e = self.x(n)
        return self.value_for(t, e, n)

    def value_for(self, t, e, n):
        """e converted for storing into a fresh object of type t: copies of named class objects are cloned."""
        if self.cs.is_class(t) and e.lv:
            return std_cs.copy_of(self, e.code, t)
        return e.code

    def ret_value(self, n):
        rt = self.ret_t
        e = self.x(n)
        rts = rt.strip() if rt is not None else None
        if isinstance(rts, C.ArrPtr):
            return std_cs.as_arrptr(self, n, e, rts.t)
        if isinstance(rts, C.Ptr) and not isinstance(rts, C.ArrPtr) and isinstance(rts.t, C.Rec) and \
                self.m.key_of_type(rts.t) in self.m.records:
            r = std_cs.ptr_to_ref(self, n, e)
            if r is not None:
                return r
        if rt is not None and isinstance(rt, (C.LRef, C.RRef)) and self.cs.by_ref(rt.t):
            if e.lv:
                return 'ref ' + e.code
            return self.todo('returning a reference to a temporary', n)
        if rt is not None and self.cs.is_class(rt) and not isinstance(rt, (C.LRef, C.RRef)) and e.lv:
            return std_cs.copy_of(self, e.code, rt)
        return e.code

    def cond(self, n):
        e = self.x(n)
        return self.as_bool(e)

    def as_bool(self, e):
        t = e.t.strip() if e.t is not None else None
        if isinstance(t, C.Builtin) and t.name == 'bool':
            return e.code
        if isinstance(t, C.Builtin):
            return '(%s) != 0' % e.code
        if isinstance(t, C.Ptr):
            return std_cs.nonnull(self, e)
        return e.code

    def expr_stmt(self, n):
        k = n.get('kind')
        if not n:
            return ''
        if k in ('ExprWithCleanups', 'ParenExpr'):
            return self.expr_stmt(kids(n)[0])
        if k == 'CStyleCastExpr' and n.get('castKind') == 'ToVoid' or (
                k == 'CXXStaticCastExpr' and n.get('castKind') == 'ToVoid'):
            e = self.x(kids(n)[0])
            if is_pure(kids(n)[0]):
                return ''
            return '_ = %s' % e.code
        e = self.x(n)
        code = e.code
        if is_statement_expr(code):
            return code
        if wrapped(code) and is_statement_expr(code[1:-1]):
            return code[1:-1]
        nt = ty(n)
        if isinstance(nt, C.Builtin) and nt.name == 'void':
            # a call of type void (through a cast delegate, say): already a statement; it has no value to discard
            while wrapped(code) and match_close(code, 0) == len(code) - 1 and not code.startswith('((global::'):
                code = code[1:-1]
            return code
        return '_ = %s' % code

    # ---------------------------------------------------------------- expressions
    def x(self, n):
        k = n.get('kind')
        h = getattr(self, 'x_' + k, None)
        if h is None:
            return self.todo('expression ' + k, n)
        return h(n)

    def x_ParenExpr(self, n):
        e = self.x(kids(n)[0])
        return E('(%s)' % e.code, e.t, e.lv)

    def x_MaterializeTemporaryExpr(self, n):
        e = self.x(kids(n)[0])
        return E(e.code, e.t, False)

    def x_CXXBindTemporaryExpr(self, n):
        return self.x(kids(n)[0])

    def x_ExprWithCleanups(self, n):
        return self.x(kids(n)[0])

    def x_ConstantExpr(self, n):
        return self.x(kids(n)[0])

    def x_SubstNonTypeTemplateParmExpr(self, n):
        return self.x(kids(n)[-1])

    def x_OpaqueValueExpr(self, n):
        if kids(n):
            return self.x(kids(n)[0])
        return self.todo('opaque value without source', n)

    def x_IntegerLiteral(self, n):
        t = ty(n)
        v = n.get('value')
        cst = self.cs.cs(t)
        suffix = {'ulong': 'UL', 'long': 'L', 'uint': 'U'}.get(cst, '')
        if cst in ('int',) or cst == '':
            return E(str(v), t)
        if suffix:
            return E(str(v) + suffix, t)
        return E('((%s)%s)' % (cst, v), t)

    def x_CharacterLiteral(self, n):
        t = ty(n)
        return E('((%s)%s)' % (self.cs.cs(t), n.get('value')), t)

    def x_FloatingLiteral(self, n):
        t = ty(n)
        v = lit_double(n.get('value'))
        if isinstance(t, C.Builtin) and t.name == 'float':
            return E(v.replace('.0', '.0') + 'f' if 'e' not in v and 'E' not in v and 'Infinity' not in v and 'NaN' not in v else '((float)%s)' % v, t)
        return E(v, t)

    def x_CXXBoolLiteralExpr(self, n):
        return E('true' if n.get('value') else 'false', ty(n))

    def x_CXXNullPtrLiteralExpr(self, n):
        return E('null', ty(n))

    def x_GNUNullExpr(self, n):
        return E('null', ty(n))

    def x_StringLiteral(self, n):
        t = ty(n)
        code = std_cs.literal(self, n.get('value'))
        if isinstance(t, C.Arr) and t.n and t.n > len(std_cs.c_unescape(n.get('value'))) + 1 and \
                code.startswith('global::Cpp.R.Lit(') and code.endswith(')'):
            # char buf[64] = "": a 64-byte array holding the literal, not the literal's own bytes
            return E('global::Cpp.R.LitN(%s, %d)' % (code[len('global::Cpp.R.Lit('):-1], t.n), t)
        return E(code, t)

    def x_CXXThisExpr(self, n):
        return E('this', ty(n))

    def x_DeclRefExpr(self, n):
        rd = n.get('referencedDecl') or {}
        key = rd.get('key')
        t = ty(n)
        kind = rd.get('kind')
        if key and key in self.names:
            nm = self.names[key]
            if key in self.byte_refs:
                return E('((sbyte)%s)' % nm, t, 'cptr:%s' % nm)
            if self.unresolved(t):
                t = self.T(n)
            return E(nm, t, True)
        if kind == 'EnumConstantDecl':
            if key in self.m.enumerators:
                ek, c = self.m.enumerators[key]
                if not self.m.enums[ek].get('name'):
                    return E('((int)%s.%s)' % (self.cs.enum_name(ek), ident(c.get('name'))), t)
                return E('%s.%s' % (self.cs.enum_name(ek), ident(c.get('name'))), t)
            return std_cs.std_enumerator(self, rd, n)
        if key and key in self.m.vars:
            return E(self.cs.var_name(self.m.vars[key]), t, True)
        if key and key in self.m.funcs:
            f = self.m.funcs[key]
            return E(self.cs.fn_name(f), t)
        if kind in ('ParmVarDecl', 'VarDecl', 'BindingDecl') and key:
            # a local of an enclosing function seen inside a lambda
            if key in self.names:
                return E(self.names[key], t, True)
            rdt = tstr(rd.get('type'))
            if any(x in rdt for x in ('_Ios_Openmode', '_Ios_Fmtflags', '_Ios_Iostate', '_Ios_Seekdir')):
                # std::ios_base::binary, ::dec, ::failbit, ::beg: the libstdc++ bit values
                return E('global::Cpp.Ios.%s' % ident(rd.get('name')), t)
            if rd.get('name') in ('npos', 'stdout', 'stderr', 'stdin', 'cout', 'cerr', 'clog', 'errno', 'nullopt'):
                return std_cs.std_declref(self, rd, n)
            if (rd.get('name') or '').startswith('memory_order_'):
                # std::memory_order_relaxed and the like (a std inline variable): ignored, the ports are single-threaded
                return E('0', t)
            return self.todo('unknown local ' + (rd.get('name') or '?'), n)
        return std_cs.std_declref(self, rd, n)

    def x_MemberExpr(self, n):
        base_n = kids(n)[0]
        mk = n.get('referencedMemberDecl')
        t = ty(n)
        name = n.get('name')
        if mk and mk in self.m.fields:
            rk, fdecl = self.m.fields[mk]
            fn = self.cs.field_name(mk, name)
            ftd = C.parse(tstr(fdecl.get('type')))
            if isinstance(ftd, C.LRef) and self.cs.by_ref(ftd.t):
                fn += '.V'                       # a reference member: the place its Box refers to
            b = self.x(base_n)
            if b.code == 'this' and base_n.get('implicit'):
                return E(fn, t, True)
            if n.get('isArrow') and self.arrptr_ref(base_n):
                return E('%s.Deref.%s' % (self.obj(b), fn), t, True)
            return E('%s.%s' % (self.obj(b), fn), t, True)
        if mk and mk in self.m.vars:
            return E(self.cs.var_name(self.m.vars[mk]), t, True)
        if mk and mk in self.m.funcs:
            b = self.x(base_n)
            f = self.m.funcs[mk]
            if f.get('storageClass') == 'static':
                return E(self.cs.rec_name(f['parent']) + '.' + self.cs.fn_name(f), t)
            return E('%s.%s' % (self.obj(b), self.cs.fn_name(f)), t)
        # a member of a standard-library type (pair::first, ...)
        return std_cs.std_member(self, n, base_n)

    def obj(self, b):
        """An object expression used with '.': parenthesize anything that is not a simple path."""
        c = b.code
        if re.match(r'^[A-Za-z_@][A-Za-z_0-9@.]*(\[[^\[\]]*\])*$', c) or c == 'this':
            return c
        return '(%s)' % c

    # ---- casts
    def x_ImplicitCastExpr(self, n):
        return self.cast(n, kids(n)[0], n.get('castKind'), implicit=True)

    def x_CStyleCastExpr(self, n):
        return self.cast(n, kids(n)[0], n.get('castKind'))

    def x_CXXStaticCastExpr(self, n):
        return self.cast(n, kids(n)[0], n.get('castKind'))

    def x_CXXFunctionalCastExpr(self, n):
        return self.cast(n, kids(n)[0], n.get('castKind'))

    def x_CXXConstCastExpr(self, n):
        return self.cast(n, kids(n)[0], n.get('castKind'))

    def x_CXXReinterpretCastExpr(self, n):
        return std_cs.reinterpret(self, n, kids(n)[0])

    def x_CXXDynamicCastExpr(self, n):
        t = ty(n)
        e = self.x(kids(n)[0])
        tt = t.strip()
        target = tt.t if isinstance(tt, C.Ptr) else tt
        return E('(%s as %s)' % (e.code, self.cs.cs(target)), t)

    def cast(self, n, sub, ck, implicit=False):
        t = self.T(n)
        if ck in ('NoOp', 'LValueToRValue', 'FunctionToPointerDecay', 'DerivedToBase', 'UncheckedDerivedToBase',
                  'BuiltinFnToFnPtr', 'AtomicToNonAtomic', 'NonAtomicToAtomic'):
            e = self.x(sub)
            return E(e.code, t, e.lv and ck in ('NoOp', 'DerivedToBase', 'UncheckedDerivedToBase'))
        if ck == 'ToVoid':
            e = self.x(sub)
            return E(e.code, t)
        st = self.T(sub)
        if ck in ('IntegralCast', 'IntegralToFloating', 'FloatingCast', 'FloatingToIntegral', 'IntegralToBoolean',
                  'FloatingToBoolean', 'BooleanToSignedIntegral'):
            e = self.x(sub)
            return E(self.arith_cast(e, st, t, ck), t)
        if ck == 'PointerToBoolean':
            e = self.x(sub)
            return E('(%s)' % std_cs.nonnull(self, e), t)
        if ck == 'NullToPointer':
            if self.unresolved(t):
                return E('default', t)       # c ? &v[i] : nullptr: C# takes the type from the other branch
            return E(self.cs.default_value(t), t)
        if ck == 'ArrayToPointerDecay':
            e = self.x(sub)
            return std_cs.decay(self, e, st, t, sub)
        if ck in ('ConstructorConversion', 'UserDefinedConversion'):
            return self.x(sub)
        if ck == 'BaseToDerived':
            e = self.x(sub)
            tt = t.strip()
            target = tt.t if isinstance(tt, C.Ptr) else tt
            return E('((%s)%s)' % (self.cs.cs(target), e.code), t, e.lv)
        if ck == 'BitCast':
            return std_cs.bitcast(self, n, sub)
        if ck == 'NullToMemberPointer':
            return self.todo('member pointer', n)
        if ck == 'Dynamic':
            return self.x_CXXDynamicCastExpr(n)
        return self.todo('cast ' + str(ck), n)

    def arith_cast(self, e, st, t, ck):
        sst, tt = st.strip(), t.strip()
        if isinstance(tt, C.Builtin) and tt.name == 'bool':
            if isinstance(sst, C.Builtin) and sst.name == 'bool':
                return e.code
            if isinstance(sst, C.Builtin) and sst.name in ('double', 'float'):
                return '(%s != 0.0)' % e.code
            if isinstance(sst, C.Rec) and (self.m.key_of_type(sst) in self.m.enums or sst.name in self.m.enums):
                return '((long)%s != 0)' % e.code
            return '(%s != 0)' % e.code
        tcs = self.cs.cs(tt)
        if isinstance(sst, C.Builtin) and sst.name == 'bool':
            return '((%s)(%s ? 1 : 0))' % (tcs, e.code)
        if ck == 'FloatingToIntegral':
            return std_cs.float_to_int(self, e, sst, tt)
        scs = self.cs.cs(sst)
        if scs == tcs:
            return e.code
        if is_enum(self, sst) and is_enum(self, tt):
            return '((%s)(long)%s)' % (tcs, e.code)
        if tcs in ('ulong', 'uint', 'ushort', 'byte') and re.match(r'^\(*-', e.code.strip()):
            return 'unchecked((%s)(%s))' % (tcs, e.code)    # a negative constant wraps, as in C++
        return '((%s)%s)' % (tcs, e.code)

    # ---- operators
    def x_BinaryOperator(self, n):
        op = n.get('opcode')
        a, b = kids(n)
        t = ty(n)
        if op == ',':
            ea = self.x(a)
            self.pre.append(self.as_stmt(ea.code) + ';')
            eb = self.x(b)
            return eb
        if op == '=':
            return self.assign(n, a, b)
        ea, eb = self.x(a), self.x(b)
        at, bt = ty(a).strip(), ty(b).strip()
        if op in ('.*', '->*'):
            # obj.*pm: the member pointer applied to the object, a reference to the field
            return E('(%s)(%s)' % (eb.code, ea.code), t, True)
        if op in ('&&', '||'):
            return E('(%s %s %s)' % (self.as_bool(ea), op, self.as_bool(eb)), t)
        if isinstance(at, C.Ptr) or isinstance(bt, C.Ptr):
            return std_cs.pointer_op(self, op, ea, eb, at, bt, t, n)
        if op in CMP:
            if isinstance(at, C.Rec) and not is_enum(self, at):
                return std_cs.class_compare(self, op, ea, eb, at, n)
            return E('(%s %s %s)' % (ea.code, op, eb.code), t)
        if op in ('<<', '>>'):
            # C# shift counts are int
            return E('((%s)(%s %s (int)(%s)))' % (self.cs.cs(t), ea.code, op, eb.code), t)
        tcs = self.cs.cs(t)
        if is_enum(self, at) or is_enum(self, bt):
            return E('((%s)((long)%s %s (long)%s))' % (tcs, ea.code, op, eb.code), t)
        code = '(%s %s %s)' % (ea.code, op, eb.code)
        if tcs in ('byte', 'sbyte', 'short', 'ushort'):
            code = '((%s)%s)' % (tcs, code)
        return E(code, t)

    def assign(self, n, a, b):
        t = ty(n)
        ea = self.x(a)
        eb = self.x(b)
        at = ty(a).strip()
        if isinstance(at, C.ArrPtr):
            return E('(%s = %s)' % (ea.code, std_cs.as_arrptr(self, b, eb, at.t)), t, True)
        if isinstance(ea.lv, str) and ea.lv.startswith(('cptr:', 'ucptr:')):
            # a char in a byte buffer: store the byte, the expression's value is the char
            k_, place = ea.lv.split(':', 1)
            return E('((%s)(%s = unchecked((byte)(%s))))' % ('sbyte' if k_ == 'cptr' else 'byte', place, eb.code), t)
        if isinstance(at, C.Ptr) and self.arrptr_ref(b):
            return E('(%s = %s.Deref)' % (ea.code, self.obj(eb)), t, True)
        if self.cs.is_class(at):
            return std_cs.class_assign(self, ea, eb, at, b)
        return E('(%s = %s)' % (ea.code, eb.code), t, True)

    def x_CompoundAssignOperator(self, n):
        op = n.get('opcode')[:-1]
        a, b = kids(n)
        t = ty(n)
        ea, eb = self.x(a), self.x(b)
        lt = C.parse(tstr(n.get('computeLHSType'))).strip()
        rt = C.parse(tstr(n.get('computeResultType'))).strip()
        at = ty(a).strip()
        if isinstance(ea.lv, str) and ea.lv.startswith(('cptr:', 'ucptr:')):
            k_, place = ea.lv.split(':', 1)
            return E('((%s)(%s = unchecked((byte)((int)%s %s (int)(%s)))))' % ('sbyte' if k_ == 'cptr' else 'byte', place, ea.code, op, eb.code), t)
        if isinstance(at, C.Ptr):
            return std_cs.pointer_compound(self, op, ea, eb, at, n)
        if isinstance(at, C.Rec) and not is_enum(self, at):
            return std_cs.class_compound(self, op, ea, eb, at, n)
        acs, lcs = self.cs.cs(at), self.cs.cs(lt)
        if op in ('<<', '>>'):
            return E('(%s %s= (int)(%s))' % (ea.code, op, eb.code), t, True)
        if acs == lcs and not is_enum(self, at):
            if acs in ('byte', 'sbyte', 'short', 'ushort'):
                return E('(%s = (%s)(%s %s %s))' % (ea.code, acs, ea.code, op, eb.code), t, True)
            return E('(%s %s= %s)' % (ea.code, op, eb.code), t, True)
        # the left side is converted to the computation type, combined, and converted back
        left = self.arith_cast(E(ea.code, at), at, lt, conv_kind(at, lt))
        res = '(%s %s %s)' % (left, op, eb.code)
        back = self.arith_cast(E(res, rt), rt, at, conv_kind(rt, at))
        return E('(%s = %s)' % (ea.code, back), t, True)

    def x_UnaryOperator(self, n):
        op = n.get('opcode')
        a = kids(n)[0]
        t = ty(n)
        at = ty(a).strip()
        if op == '&':
            return std_cs.address_of(self, a, n)
        if op == '*':
            e = self.x(a)
            if self.arrptr_ref(a):
                return E('%s.Deref' % self.obj(e), t, True)
            return std_cs.deref(self, e, at, n)
        e = self.x(a)
        if op in ('++', '--'):
            if isinstance(at, C.Ptr):
                return std_cs.pointer_incdec(self, op, e, at, n.get('isPostfix'), n)
            if is_enum(self, at):
                return self.todo('++ on an enum', n)
            if isinstance(at, C.Builtin) and at.name == 'bool':
                return self.todo('++ on a bool', n)
            if n.get('isPostfix'):
                return E('%s%s' % (e.code, op), t)
            return E('%s%s' % (op, e.code), t, True)
        if op == '!':
            return E('!(%s)' % self.as_bool(e), t)
        if op == '-':
            code = '(-%s)' % e.code
            tcs = self.cs.cs(t)
            if tcs in ('byte', 'sbyte', 'short', 'ushort', 'ulong', 'uint'):
                code = '((%s)%s)' % (tcs, code) if tcs not in ('ulong', 'uint') else '((%s)(0 - %s))' % (tcs, e.code)
            return E(code, t)
        if op == '+':
            return E(e.code, t)
        if op == '~':
            return E('((%s)~%s)' % (self.cs.cs(t), e.code), t)
        if op in ('__extension__',):
            return e
        return self.todo('unary ' + op, n)

    def x_ConditionalOperator(self, n):
        c, a, b = kids(n)
        t = ty(n)
        ec = self.cond(c)
        ea, eb = self.x(a), self.x(b)
        if n.get('valueCategory') == 'lvalue' and ea.lv and eb.lv and self.cs.by_ref(t):
            return E('(%s ? ref %s : ref %s)' % (ec, ea.code, eb.code), t, True)
        ac, bc = ea.code, eb.code
        tc = self.cs.cs(t)
        if ac == 'null' or bc == 'null':
            if ac == 'null':
                ac = '(%s)null' % tc
            else:
                bc = '(%s)null' % tc
        return E('(%s ? %s : %s)' % (ec, ac, bc), t, ea.lv and eb.lv and n.get('valueCategory') == 'lvalue')

    def x_BinaryConditionalOperator(self, n):
        return self.todo('?: with omitted middle', n)

    def x_ArraySubscriptExpr(self, n):
        a, i = kids(n)
        ea = self.x(a)
        ei = self.x(i)
        if is_enum(self, ty(i).strip()):
            ei = E('((long)%s)' % ei.code, C.Builtin('long'))      # an enumerator used as an index converts to int
        return std_cs.subscript(self, ea, ei, ty(a).strip(), ty(n), n)

    def x_UnaryExprOrTypeTraitExpr(self, n):
        nm = n.get('name')
        if nm in ('sizeof', 'alignof'):
            if kids(n):
                t = ty(kids(n)[0])
            else:
                t = C.parse(tstr(n.get('argType')))
            return E('%dUL' % sizeof(self, t, n), ty(n))
        return self.todo('trait ' + str(nm), n)

    def x_CXXDefaultArgExpr(self, n):
        # resolved by the caller (call_args); reaching here means a default argument outside a call
        return self.todo('default argument outside a call', n)

    def x_CXXDefaultInitExpr(self, n):
        return self.todo('default member initializer outside its constructor', n)

    def x_ImplicitValueInitExpr(self, n):
        return E(self.cs.default_value(ty(n)), ty(n))

    def x_CXXScalarValueInitExpr(self, n):
        return E(self.cs.default_value(ty(n)), ty(n))

    def x_InitListExpr(self, n):
        return std_cs.init_list(self, n)

    def x_CXXStdInitializerListExpr(self, n):
        inner = kids(n)[0]
        while inner.get('kind') in ('MaterializeTemporaryExpr', 'ExprWithCleanups', 'CXXBindTemporaryExpr'):
            inner = kids(inner)[0]
        t = ty(n)
        et = t.args[0] if isinstance(t, C.Rec) and t.args else None
        if inner.get('kind') == 'InitListExpr':
            items = [self.value_for(et, self.x(c), c) if et is not None else self.x(c).code for c in kids(inner)]
            if isinstance(et, C.Ptr) and isinstance(et.t, C.Rec) and self.m.key_of_type(et.t) in self.m.records and \
                    kids(inner) and all(std_cs.is_ptr_valued(self, c) for c in kids(inner)):
                # {arr1, arr2, ...} of arrays of objects: a list of array pointers
                return E('new global::Cpp.Ptr<%s>[] { %s }' % (self.cs.cs(et.t, 'elem'), ', '.join(items)), t)
            return E('new %s[] { %s }' % (self.cs.cs(et, 'elem'), ', '.join(items)), t)
        return self.todo('initializer list from ' + inner.get('kind', '?'), n)

    def x_ArrayInitLoopExpr(self, n):
        # copying an array member (implicit copy constructor of a class with an array field)
        src = kids(n)[0]
        e = self.x(src)
        return E('global::Cpp.Arr.Clone(%s)' % e.code, ty(n))

    def x_LambdaExpr(self, n):
        return std_cs.lambda_expr(self, n)

    def x_CXXThrowExpr(self, n):
        if not kids(n):
            return E('throw', ty(n))
        e = self.x(kids(n)[0])
        return E('throw %s' % e.code, ty(n))

    def x_CXXNewExpr(self, n):
        return std_cs.new_expr(self, n)

    def x_CXXDeleteExpr(self, n):
        return std_cs.delete_expr(self, n)

    def wrap_backs(self, e, start):
        """Copy boxed out-parameters back into their variables right after the call that filled them."""
        items = self.backs[start:]
        del self.backs[start:]
        if not items:
            return e
        t = e.t.strip() if e.t is not None else None
        if isinstance(t, C.Builtin) and t.name == 'void':
            self.post += ['%s = %s[0];' % (x, b) for x, b in items]
            return e
        code = e.code
        for x, b in items:
            code = 'global::Cpp.Mv.Back(%s, ref %s, %s)' % (code, x, b)
        return E(code, e.t)

    def x_CXXConstructExpr(self, n):
        k = len(self.backs)
        return self.wrap_backs(std_cs.construct(self, n), k)

    def x_CXXTemporaryObjectExpr(self, n):
        k = len(self.backs)
        return self.wrap_backs(std_cs.construct(self, n), k)

    def x_CXXMemberCallExpr(self, n):
        k = len(self.backs)
        return self.wrap_backs(std_cs.member_call(self, n), k)

    def x_CXXOperatorCallExpr(self, n):
        k = len(self.backs)
        return self.wrap_backs(std_cs.operator_call(self, n), k)

    def x_CallExpr(self, n):
        k = len(self.backs)
        return self.wrap_backs(std_cs.call(self, n), k)

    def x_CXXRewrittenBinaryOperator(self, n):
        return self.x(kids(n)[0])

    def x_CXXTypeidExpr(self, n):
        return self.todo('typeid', n)

    def x_CXXNoexceptExpr(self, n):
        return E('true', ty(n))

    def x_CXXInheritedCtorInitExpr(self, n):
        return self.todo('inherited constructor', n)

    def x_StmtExpr(self, n):
        return self.todo('statement expression', n)

    def x_CXXParenListInitExpr(self, n):
        return std_cs.init_list(self, n)

    def x_ChooseExpr(self, n):
        return self.todo('__builtin_choose_expr', n)

    def x_PredefinedExpr(self, n):
        return E(std_cs.literal(self, n.get('name', '')), ty(n))

    def x_SourceLocExpr(self, n):
        return E('global::Cpp.R.Lit("")', ty(n))

    def x_CXXPseudoDestructorExpr(self, n):
        return E('0', ty(n))

    def x_UnresolvedLookupExpr(self, n):
        return self.todo('dependent name (uninstantiated template code)', n)

    def x_CXXDependentScopeMemberExpr(self, n):
        return self.todo('dependent member (uninstantiated template code)', n)

    def x_UnresolvedMemberExpr(self, n):
        return self.todo('dependent member (uninstantiated template code)', n)

    def as_stmt(self, code):
        if is_statement_expr(code):
            return code
        return '_ = ' + code


STD_ENUMS = ('std::errc', 'std::byte', 'std::_Ios_Openmode', 'std::_Ios_Fmtflags', 'std::_Ios_Iostate',
             'std::_Ios_Seekdir', 'std::memory_order', 'std::float_round_style', 'std::float_denorm_style')


def is_enum(fn, t):
    return isinstance(t, C.Rec) and (fn.m.key_of_type(t) in fn.m.enums or t.name in fn.m.enums
                                     or t.name in STD_ENUMS)


def conv_kind(a, b):
    fa = isinstance(a, C.Builtin) and a.name in ('double', 'float', 'long double')
    fb = isinstance(b, C.Builtin) and b.name in ('double', 'float', 'long double')
    if fa and fb:
        return 'FloatingCast'
    if fa:
        return 'FloatingToIntegral'
    if fb:
        return 'IntegralToFloating'
    return 'IntegralCast'


def terminates(stmts):
    if not stmts:
        return False
    last = stmts[-1]
    k = last.get('kind')
    if k in ('BreakStmt', 'ReturnStmt', 'ContinueStmt', 'GotoStmt'):
        return True
    if k in ('ExprWithCleanups',) and kids(last) and kids(last)[0].get('kind') == 'CXXThrowExpr':
        return True
    if k == 'CXXThrowExpr':
        return True
    if k == 'CompoundStmt':
        return terminates(kids(last))
    if k == 'IfStmt' and last.get('hasElse'):
        ch = kids(last)
        return terminates([ch[-2]]) and terminates([ch[-1]])
    return False


def match_close(c, i):
    """Index of the bracket closing the one at c[i] (skipping string and char literals), or -1."""
    pairs = {'(': ')', '[': ']', '{': '}'}
    stack = []
    j = i
    while j < len(c):
        ch = c[j]
        if ch in '"\'':
            q = ch
            j += 1
            while j < len(c) and c[j] != q:
                j += 2 if c[j] == '\\' else 1
        elif ch in pairs:
            stack.append(pairs[ch])
        elif ch in ')]}':
            if not stack or stack.pop() != ch:
                return -1
            if not stack:
                return j
        j += 1
    return -1


def wrapped(c):
    """Whether c is one parenthesized expression: its first '(' closes at its last character."""
    return c.startswith('(') and c.endswith(')') and match_close(c, 0) == len(c) - 1


def top_level(c):
    """c with every bracketed group and literal blanked to '#', so only its top-level text shows."""
    out, j = [], 0
    while j < len(c):
        if c[j] in '([{':
            k = match_close(c, j)
            if k < 0:
                return c
            out.append('#')
            j = k + 1
        elif c[j] in '"\'':
            q, k = c[j], j + 1
            while k < len(c) and c[k] != q:
                k += 2 if c[k] == '\\' else 1
            out.append('#')
            j = k + 1
        else:
            out.append(c[j])
            j += 1
    return ''.join(out)


def refers_to(n, key):
    """n mentions the local key (a lambda that calls itself through the variable it initializes)."""
    if not key:
        return False
    stack = [n]
    while stack:
        x = stack.pop()
        if x.get('kind') == 'DeclRefExpr' and (x.get('referencedDecl') or {}).get('key') == key:
            return True
        stack.extend(x.get('inner', ()))
    return False


def mark_arrptr(f, keys, fields=(), cs=None):
    """Mark the declarations of keys, the names that refer to them (and to the array-pointer fields), calls to
    functions returning array pointers, and the casts around those."""
    stack = [(f, None)]
    while stack:
        x, parent = stack.pop()
        k = x.get('kind')
        if cs is not None and k in ('CallExpr', 'CXXMemberCallExpr'):
            g = cs.call_target(x)
            if g is not None and cs.arrptr_returning(g):
                x['_arrptr'] = True
        if k in ('ParmVarDecl', 'VarDecl') and x.get('local') in keys:
            x['_arrptr'] = True
        if k == 'DeclRefExpr' and (x.get('referencedDecl') or {}).get('key') in keys:
            x['_arrptr'] = True
        if k == 'MemberExpr' and x.get('referencedMemberDecl') in fields:
            x['_arrptr'] = True
        for c in x.get('inner', ()):
            stack.append((c, x))
    # casts that only read the value (LValueToRValue, NoOp) keep the mark
    changed = True
    while changed:
        changed = False
        stack = [f]
        while stack:
            x = stack.pop()
            ch = x.get('inner', ())
            if x.get('kind') in ('ImplicitCastExpr', 'ParenExpr') and ch and ch[0].get('_arrptr') and not x.get('_arrptr') \
                    and x.get('castKind') in (None, 'LValueToRValue', 'NoOp'):
                x['_arrptr'] = True
                changed = True
            stack.extend(ch)


def is_statement_expr(code):
    c = code.strip()
    if c == 'throw' or c.startswith('throw '):
        return True
    # (x).f(...), a[i].g(...), f(a)(b): an invocation whose top level holds no operator
    tl = top_level(c)
    # (x).v = e, a[i].f += e: an assignment at the top level
    if re.match(r'^[#\w@.:]+\s*(=|\+=|-=|\*=|/=|%=|&=|\|=|\^=|<<=|>>=)\s', tl) and not c.startswith('(('):
        return True
    if tl.endswith('#') and c.endswith(')') and re.fullmatch(r'(global::)?[#\w@.:<>]*[\w@>\]#]#', tl) and \
            not re.fullmatch(r'#+', tl) and not tl.startswith('##'):
        return True
    if re.match(r'^\(?[A-Za-z_@][\w@.\[\]]*\s*(=|\+=|-=|\*=|/=|%=|&=|\|=|\^=|<<=|>>=)\s', c):
        return not c.startswith('(')
    if re.match(r'^(\+\+|--)[\w@.\[\]]+$', c) or re.match(r'^[\w@.\[\]]+(\+\+|--)$', c):
        return True
    if c.startswith('new '):
        return True
    return False


def is_pure(n):
    k = n.get('kind')
    if k in ('DeclRefExpr', 'IntegerLiteral', 'FloatingLiteral', 'CXXBoolLiteralExpr', 'MemberExpr'):
        return all(is_pure(c) for c in kids(n))
    if k in ('ImplicitCastExpr', 'ParenExpr', 'CStyleCastExpr', 'CXXStaticCastExpr'):
        return all(is_pure(c) for c in kids(n))
    return False


def is_constant_init(n):
    k = n.get('kind')
    if k in ('IntegerLiteral', 'FloatingLiteral', 'CXXBoolLiteralExpr', 'StringLiteral', 'CharacterLiteral'):
        return True
    if k in ('ImplicitCastExpr', 'ParenExpr', 'ConstantExpr', 'CStyleCastExpr', 'CXXStaticCastExpr', 'UnaryOperator',
             'BinaryOperator'):
        return all(is_constant_init(c) for c in kids(n))
    if k == 'DeclRefExpr' and (n.get('referencedDecl') or {}).get('kind') == 'EnumConstantDecl':
        return True
    return False


def sizeof(fn, t, n):
    t = t.strip() if isinstance(t, (C.LRef, C.RRef)) else t
    if isinstance(t, C.Builtin):
        return INT_SIZES[t.name]
    if isinstance(t, C.Ptr):
        return 8
    if isinstance(t, C.Arr):
        return (t.n or 0) * sizeof(fn, t.t, n)
    if isinstance(t, C.Rec):
        if is_enum(fn, t):
            e = fn.m.enums.get(fn.m.key_of_type(t)) or fn.m.enums.get(t.name)
            fixed = (e or {}).get('fixedUnderlyingType')
            if fixed:
                return sizeof(fn, C.parse(tstr(fixed)), n)
            return 4
        s = std_cs.sizeof_std(t)
        if s:
            return s
        # libstdc++ on x86-64 (the reference build): the sizes the checkpoint archive charges to its budget
        if t.name in ('std::basic_string', 'std::__cxx11::basic_string', 'std::string'):
            return 32
        if t.name == 'std::vector':
            return 24
        if t.name == 'std::array' and len(t.args) == 2:
            return int(repr(t.args[1])) * sizeof(fn, t.args[0], n)
        if t.name == 'std::pair' and len(t.args) == 2:
            off, al = 0, 1
            for a in t.args:
                sa, aa = sizeof(fn, a, n), alignof(fn, a, n)
                off = (off + aa - 1) // aa * aa + sa
                al = max(al, aa)
            return (off + al - 1) // al * al
        k = fn.m.key_of_type(t)
        if k in fn.m.records:
            return record_size(fn, k, n)
    return fn.todo('sizeof ' + repr(t), n)


def record_size(fn, k, n):
    """The size of a struct under the x86-64 ABI (only plain structs of numbers occur in sizeof)."""
    r = fn.m.records[k]
    off, align = 0, 1
    for b in fn.m.bases(k):
        if b[0] in fn.m.records:
            s = record_size(fn, b[0], n)
            off += s
            align = max(align, 8)
    for f in r.get('fields', []):
        t = C.parse(tstr(f.get('type')))
        s = sizeof(fn, t, n)
        a = alignof(fn, t, n)
        off = (off + a - 1) // a * a + s
        align = max(align, a)
    return (off + align - 1) // align * align if off else 1


def alignof(fn, t, n):
    if isinstance(t, C.Arr):
        return alignof(fn, t.t, n)
    if isinstance(t, C.Builtin) or isinstance(t, C.Ptr):
        return min(sizeof(fn, t, n), 8) or 1
    if isinstance(t, C.Rec):
        if is_enum(fn, t):
            return sizeof(fn, t, n)
        if t.name in ('std::array', 'std::pair') and t.args:
            return max(alignof(fn, a, n) for a in t.args if not isinstance(a, C.Value))
        k = fn.m.key_of_type(t)
        if k in fn.m.records:
            return max([alignof(fn, C.parse(tstr(f.get('type'))), n) for f in fn.m.records[k].get('fields', [])] or [1])
    return 8
