#!/usr/bin/env python3
"""Merge the per-translation-unit AST dumps (tools/cpp2x/dump_ast.py) into one flat table of the engine's
declarations, with every reference rewritten from a dump-local id to a stable key.

  collect.py AST_DIR SRC_DIR OUT.jsonl.gz

Keys: functions, methods, constructors and variables with linkage use clang's mangled name; records, enums,
typedefs and namespaces their qualified name (template specializations with their arguments); fields
'<record>::<name>'; enumerators '<enum>::<name>'; locals and parameters 'L<n>' unique within their function.
Each line of the output is one declaration. Records keep their fields, bases and nested declarations by key;
member functions are separate lines with a "parent" key. A function seen in several translation units is kept
once, preferring a copy with a body. Comments and source ranges are dropped; each node keeps its line number.
Written by Claude (Anthropic); see AI-USE.md."""
import gzip, json, os, sys, collections

SKIP_KINDS = {'FullComment', 'ParagraphComment', 'TextComment', 'BlockCommandComment', 'ParamCommandComment',
              'InlineCommandComment', 'VerbatimBlockComment', 'VerbatimBlockLineComment', 'VerbatimLineComment',
              'HTMLStartTagComment', 'HTMLEndTagComment', 'TParamCommandComment'}
FUNC_KINDS = {'FunctionDecl', 'CXXMethodDecl', 'CXXConstructorDecl', 'CXXDestructorDecl', 'CXXConversionDecl'}
RECORD_KINDS = {'CXXRecordDecl', 'ClassTemplateSpecializationDecl', 'ClassTemplatePartialSpecializationDecl'}
DROP_KEYS = {'loc', 'range', 'id', 'typeAliasDeclId', 'previousDecl', 'parentDeclContextId', 'ownedTagDecl'}


class TU:
    """One translation unit's dump. clang prints a location's "file" and "line" only when they differ from the
    last ones it printed, so both are followed here in document order and stored per node id."""

    def __init__(self, src):
        self.src = src
        self.ids = {}          # decl id -> (key, file relative to src, parent key)
        self.recscope = {}     # record decl id -> the scope its members live in (for out-of-line definitions)
        self.pos = {}          # node id -> (file, line)
        self.cur = ''
        self.cur_line = None
        self.nsname = {}       # anonymous namespace id -> '(anon:<file>)'
        self.nsscope = {}      # namespace decl id -> its scope (for a class first named inside a class body)
        self.spec_params = {}  # class template specialization id -> its template's parameter types (None: a type)

    def _bare(self, B):
        if not B:
            return None
        if 'file' in B:
            self.cur = B['file']
        if 'line' in B:
            self.cur_line = B['line']
        return (self.cur, self.cur_line)

    def _loc(self, L):
        if 'spellingLoc' in L or 'expansionLoc' in L:
            if 'spellingLoc' in L:
                self._bare(L['spellingLoc'])
            return self._bare(L.get('expansionLoc'))
        return self._bare(L)

    def track(self, n):
        """Advance the file/line state over n's own locations; return the file and line of n's location."""
        p = None
        if 'loc' in n:
            p = self._loc(n['loc'])
        r = n.get('range')
        if r:
            for e in ('begin', 'end'):
                if e in r:
                    self._loc(r[e])
        if p is None:
            p = (self.cur, self.cur_line)
        if 'id' in n:
            self.pos[n['id']] = p
        return p[0]

    def line(self, n):
        p = self.pos.get(n.get('id'))
        return p[1] if p else None


def rel(path, src):
    p = os.path.normpath(path)
    return os.path.relpath(p, src) if p.startswith(src) else p


def tstr_of(c):
    t = c.get('type') or {}
    return t.get('desugaredQualType') or t.get('qualType')


def unfold_array_filler(e):
    """clang's JSON dumper names a node's whole child array after its first child's label. An InitListExpr with an
    array filler visits the filler first, labelled "array_filler", so the list's elements follow it there and the
    node has no "inner": int a[7] = {x, y} dumps as "array_filler": [ImplicitValueInitExpr, x, y]. Put the
    elements back where every pass looks for children, leaving the filler alone under its label."""
    stack = [e]
    while stack:
        n = stack.pop()
        af = n.get('array_filler')
        if isinstance(af, list) and len(af) > 1:
            n['inner'] = af[1:] + n.get('inner', [])
            n['array_filler'] = af[:1]
        for c in n.get('inner', ()):
            if isinstance(c, dict):
                stack.append(c)
        for c in n.get('array_filler', ()):
            if isinstance(c, dict):
                stack.append(c)


def targ(c, ptype=None):
    """One template argument, spelled as clang spells it in a type name. ptype is the template parameter's type."""
    t = c.get('type')
    if t:
        return t.get('desugaredQualType') or t.get('qualType')
    if 'value' in c:
        if ptype == 'bool' and str(c['value']) in ('-1', '0', '1'):
            # clang 18's JSON dumper prints a bool argument as a 1-bit signed integer (true is -1), where type
            # names say Batch<true>: the key must be spelled the way the types that name it are
            return 'false' if str(c['value']) == '0' else 'true'
        return str(c['value'])
    if c.get('inner'):
        return targ(c['inner'][0]) if c['inner'][0].get('kind') == 'TemplateArgument' else '?'
    return '?'


def index(tu, n, scope, parent, in_func=False):
    """Pass 1: record every declaration's id with its stable key (in file order, so the file tracking works)."""
    if n.get('kind') in SKIP_KINDS:
        return
    f = tu.track(n)
    k = n.get('kind')
    nm = n.get('name')
    key = None
    if k == 'NamespaceDecl':
        if not nm:
            nm = '(anon:%s)' % rel(f, tu.src)
            tu.nsname[n['id']] = nm
        sub = scope + [nm]
        tu.nsscope[n['id']] = sub
        for c in n.get('inner', []):
            index(tu, c, sub, parent)
        return
    if k in FUNC_KINDS or (k == 'VarDecl' and n.get('mangledName')):
        key = n.get('mangledName') or ('::'.join(scope + [nm or '?']) + '@' + str(tu.line(n)))
        # internal linkage: a static function or variable, or a static local, is one per translation unit, and
        # two files may hold different ones with the same mangled name
        if (n.get('storageClass') == 'static' and (parent is None or in_func)) or any(x.startswith('(anon:') for x in scope):
            key += '@' + rel(f, tu.src)
    elif k in RECORD_KINDS or k in ('EnumDecl', 'TypedefDecl', 'TypeAliasDecl'):
        # a nested class defined outside its class (struct Router::RestartState {...}) belongs to that class
        pid = n.get('parentDeclContextId')
        if parent is None and pid in tu.recscope:
            parent = tu.ids[pid][0]
            scope = tu.recscope[pid]
        elif parent is not None and pid in tu.nsscope:
            # `const class IPluginComponentInfo* info` in a member's parameter list names a class of the
            # enclosing namespace, though clang lists it inside the class body
            parent, scope = None, tu.nsscope[pid]
        if nm and in_func and k != 'TypedefDecl' and k != 'TypeAliasDecl':
            # a class or enum local to a function: unique by where it is defined
            key = '::'.join(scope + ['%s@%s:%s' % (nm, rel(f, tu.src), tu.line(n))])
        elif nm:
            key = '::'.join(scope + [nm])
        else:
            key = '::'.join(scope + ['(anon@%s:%s)' % (rel(f, tu.src), tu.line(n))])
        if k == 'ClassTemplateSpecializationDecl':
            ptypes = tu.spec_params.get(n.get('id'), [])
            targs = [c for c in n.get('inner', []) if c.get('kind') == 'TemplateArgument']
            key = '::'.join(scope + [nm + '<' + ', '.join(targ(c, ptypes[i] if i < len(ptypes) else None)
                                                         for i, c in enumerate(targs)) + '>'])
    elif k == 'FieldDecl':
        key = (parent or '?') + '::' + (nm or '(anon@%s)' % tu.line(n))
    elif k == 'EnumConstantDecl':
        key = (parent or '?') + '::' + nm
    if 'id' in n and key is not None:
        tu.ids[n['id']] = (key, rel(f, tu.src), parent)
    if k in RECORD_KINDS:
        sub = scope + [key.split('::')[-1] if key else (nm or '?')]
        if 'id' in n and key is not None:
            tu.recscope[n['id']] = sub
        for c in n.get('inner', []):
            index(tu, c, sub, key)
        return
    if k == 'EnumDecl':
        for c in n.get('inner', []):
            index(tu, c, scope, key)
        return
    if k in ('ClassTemplateDecl', 'FunctionTemplateDecl'):
        # the template's parameters come first; its specializations follow and take their argument spelling from them
        ptypes = [tstr_of(c) if c.get('kind') == 'NonTypeTemplateParmDecl' else None for c in n.get('inner', [])
                  if c.get('kind') in ('TemplateTypeParmDecl', 'NonTypeTemplateParmDecl', 'TemplateTemplateParmDecl')]
        for c in n.get('inner', []):
            if c.get('kind') == 'ClassTemplateSpecializationDecl' and 'id' in c:
                tu.spec_params[c['id']] = ptypes
            index(tu, c, scope, parent)
        return
    if k in ('LinkageSpecDecl', 'ExportDecl'):
        for c in n.get('inner', []):
            index(tu, c, scope, parent)
        return
    # walk into everything else for the file tracking (function bodies, initializers); an array filler is
    # printed before the list's elements (see unfold_array_filler)
    for c in n.get('array_filler', [])[:1] + n.get('inner', []):
        index(tu, c, scope, parent, in_func or k in FUNC_KINDS)


def local_types(n):
    """The named classes and enums declared in a function body (not those of lambdas, not nested in them)."""
    out = []
    def walk(x):
        for c in x.get('inner', []):
            k = c.get('kind')
            if k in RECORD_KINDS or k == 'EnumDecl':
                # named ones, and unnamed ones a variable is declared with (const struct {...} rows[] = {...})
                if not c.get('isImplicit') and c.get('completeDefinition', k == 'EnumDecl'):
                    out.append(c)
                continue
            if k == 'LambdaExpr':
                continue
            walk(c)
    walk(n)
    return out


# what only the first (in-class) declaration of a function says: the out-of-line definition repeats none of it
DECL_ONLY = ('virtual', 'pure', 'storageClass', 'access', 'inline', 'explicitlyDeleted', 'explicitlyDefaulted')


def merge_defaults(keep, other):
    """Give the copy kept the default arguments only the other copy has. The specializations of a member function
    template whose argument is a closure type share one key (the closure is erased, so their C# is one method), and
    clang instantiates a default argument only in the specializations whose calls use it: refreshDummyFlows(dt,
    forcing, [&](...){...}) relies on `bool book_receipts = true`, which the first specialization kept may lack."""
    kp = [c for c in keep.get('inner', []) if c.get('kind') == 'ParmVarDecl']
    op = [c for c in other.get('inner', []) if c.get('kind') == 'ParmVarDecl']
    if len(kp) == len(op):
        for a, b in zip(op, kp):
            if a.get('init') and not b.get('init'):
                b['init'] = a['init']
                b['inner'] = a.get('inner', [])


def merge_decl(defn, decl):
    """Give a function definition what its earlier declaration said: virtual, static, access, the class it
    belongs to, and default arguments (which C++ allows only on the first declaration)."""
    for a in DECL_ONLY:
        if a in decl and a not in defn:
            defn[a] = decl[a]
    if decl.get('parent') and not defn.get('parent'):
        defn['parent'] = decl['parent']
        defn['scope'] = decl.get('scope')
    dp = [c for c in decl.get('inner', []) if c.get('kind') == 'ParmVarDecl']
    fp = [c for c in defn.get('inner', []) if c.get('kind') == 'ParmVarDecl']
    if len(dp) == len(fp):
        for a, b in zip(dp, fp):
            if a.get('init') and not b.get('init'):
                b['init'] = a['init']
                b['inner'] = a.get('inner', [])
    # attributes such as override and final are written on the declaration
    have = {c.get('kind') for c in defn.get('inner', [])}
    extra = [c for c in decl.get('inner', []) if c.get('kind', '').endswith('Attr') and c.get('kind') not in have]
    if extra:
        defn['inner'] = extra + defn.get('inner', [])


class Rewriter:
    """Pass 2: copy a declaration without comments and locations, references turned into keys."""

    def __init__(self, tu):
        self.tu = tu
        self.locals = None

    def node(self, n):
        k = n.get('kind')
        if k in SKIP_KINDS:
            return None
        out = {}
        for a, v in n.items():
            if a in DROP_KEYS or a == 'inner':
                continue
            out[a] = v
        L = self.tu.line(n)
        if L:
            out['line'] = L
        if k in ('ParmVarDecl', 'VarDecl', 'BindingDecl', 'DecompositionDecl') and not n.get('mangledName') and self.locals is not None:
            out['local'] = self.local(n['id'])
        elif k == 'VarDecl' and n.get('mangledName') and self.tu.ids.get(n.get('id')):
            out['key'] = self.tu.ids[n['id']][0]     # a static local: references to it use this key
        rd = n.get('referencedDecl')
        if isinstance(rd, dict):
            out['referencedDecl'] = self.ref(rd)
        rm = n.get('referencedMemberDecl')
        if isinstance(rm, str):
            t = self.tu.ids.get(rm)
            out['referencedMemberDecl'] = t[0] if t else None
        for a in ('foundReferencedDecl', 'decl', 'templateParams'):
            pass
        inner = []
        for c in n.get('inner', []):
            r = self.node(c)
            if r is not None:
                inner.append(r)
        if inner:
            out['inner'] = inner
        return out

    def local(self, i):
        if i not in self.locals:
            self.locals[i] = 'L%d' % len(self.locals)
        return self.locals[i]

    def ref(self, rd):
        r = {a: v for a, v in rd.items() if a != 'id'}
        i = rd.get('id')
        if rd.get('kind') in ('ParmVarDecl', 'VarDecl', 'BindingDecl', 'DecompositionDecl') and self.locals is not None and i in self.locals:
            r['key'] = self.locals[i]
            return r
        t = self.tu.ids.get(i)
        if t:
            r['key'] = t[0]
        elif rd.get('kind') in ('ParmVarDecl', 'VarDecl', 'BindingDecl') and self.locals is not None:
            # a local declared later in source order than its use cannot occur; one declared in an enclosing
            # lambda's scope can - give it its key now
            r['key'] = self.local(i)
        return r


def has_body(n):
    return any(c.get('kind') in ('CompoundStmt', 'CXXTryStmt') for c in n.get('inner', []))


class Store:
    def __init__(self):
        self.decls = {}           # key -> flattened decl
        self.order = []

    def put(self, key, d):
        old = self.decls.get(key)
        if old is None:
            self.decls[key] = d
            self.order.append(key)
            return
        if d['kind'] in FUNC_KINDS and has_body(d) and not has_body(old):
            merge_decl(d, old)
            self.decls[key] = d
        elif d['kind'] in FUNC_KINDS and has_body(old) and not has_body(d):
            merge_decl(old, d)
        elif d['kind'] in FUNC_KINDS:
            merge_defaults(old, d)
        elif d['kind'] in RECORD_KINDS:
            # keep the definition; union of member keys (implicit members appear only where they are used)
            if d.get('completeDefinition') and not old.get('completeDefinition'):
                d['members'] = list(dict.fromkeys(d.get('members', []) + old.get('members', [])))
                self.decls[key] = d
            else:
                old['members'] = list(dict.fromkeys(old.get('members', []) + d.get('members', [])))


def flatten(tu, n, scope, parent, store, rw):
    """Pass 3: emit flattened declarations from a dump element."""
    k = n.get('kind')
    if k in SKIP_KINDS:
        return
    if k == 'NamespaceDecl':
        for c in n.get('inner', []):
            flatten(tu, c, scope + [n.get('name') or tu.nsname.get(n.get('id'), '(anonymous)')], parent, store, rw)
        return
    if k in ('LinkageSpecDecl', 'ExportDecl'):
        for c in n.get('inner', []):
            flatten(tu, c, scope, parent, store, rw)
        return
    if k in ('ClassTemplateDecl', 'FunctionTemplateDecl', 'TypeAliasTemplateDecl', 'VarTemplateDecl'):
        # keep only the concrete specializations; the pattern itself is dependent code
        # the first record or function inside is the dependent pattern; the specializations follow it
        for c in n.get('inner', []):
            if c.get('kind') == 'ClassTemplateSpecializationDecl':
                flatten(tu, c, scope, parent, store, rw)
        specs = [c for c in n.get('inner', []) if c.get('kind') in FUNC_KINDS]
        for c in specs[1:]:
            flatten(tu, c, scope, parent, store, rw)
        return
    meta = tu.ids.get(n.get('id'))
    key = meta[0] if meta else None
    if k in RECORD_KINDS or k == 'EnumDecl':
        pid = n.get('parentDeclContextId')
        if parent is None and pid in tu.recscope:
            parent = tu.ids[pid][0]
            scope = tu.recscope[pid]
        elif parent is not None and pid in tu.nsscope:
            # `const class IPluginComponentInfo* info` in a member's parameter list names a class of the
            # enclosing namespace, though clang lists it inside the class body
            parent, scope = None, tu.nsscope[pid]
    if k in RECORD_KINDS:
        if n.get('isImplicit') and n.get('name'):
            return     # the injected class name
        if k == 'ClassTemplatePartialSpecializationDecl':
            return
        d = {a: v for a, v in n.items() if a not in DROP_KEYS and a != 'inner'}
        d.update(key=key, scope='::'.join(scope), parent=parent, file=meta[1] if meta else None, line=tu.line(n))
        fields, members, bases = [], [], n.get('bases', [])
        sub = scope + [key.split('::')[-1] if key else (n.get('name') or '?')]
        for c in n.get('inner', []):
            ck = c.get('kind')
            if ck == 'FieldDecl':
                rw.locals = {}
                fields.append(rw.node(c) | {'key': tu.ids.get(c.get('id'), (None,))[0]})
                rw.locals = None
            elif ck in ('AccessSpecDecl', 'FriendDecl', 'StaticAssertDecl', 'UsingDecl', 'UsingShadowDecl'):
                continue
            elif ck in RECORD_KINDS and c.get('isImplicit') and c.get('name'):
                continue    # the injected class name
            else:
                ckey = tu.ids.get(c.get('id'), (None,))[0]
                if ckey:
                    members.append(ckey)
                flatten(tu, c, sub, key, store, rw)
        d['fields'] = fields
        d['members'] = members
        d['bases'] = bases
        store.put(key, d)
        return
    if k == 'EnumDecl':
        d = {a: v for a, v in n.items() if a not in DROP_KEYS and a != 'inner'}
        rw.locals = {}
        d['enumerators'] = [rw.node(c) | {'key': tu.ids.get(c.get('id'), (None,))[0]}
                            for c in n.get('inner', []) if c.get('kind') == 'EnumConstantDecl']
        rw.locals = None
        d.update(key=key, scope='::'.join(scope), parent=parent, file=meta[1] if meta else None, line=tu.line(n))
        store.put(key, d)
        return
    if k in FUNC_KINDS or k in ('VarDecl', 'TypedefDecl', 'TypeAliasDecl'):
        if key is None:
            return
        rw.locals = {}
        d = rw.node(n)
        rw.locals = None
        # an out-of-line member definition (void C::f() {...}) sits at namespace scope; its class is the
        # declaration context clang names
        pid = n.get('parentDeclContextId')
        if parent is None and pid in tu.recscope:
            parent = tu.ids[pid][0]
            scope = tu.recscope[pid]
        d.update(key=key, scope='::'.join(scope), parent=parent, file=meta[1] if meta else None)
        if k in FUNC_KINDS:
            # classes and enums defined inside the body become declarations of their own
            locs = []
            for r in local_types(n):
                rk = tu.ids.get(r.get('id'), (None,))[0]
                if not rk:
                    continue
                flatten(tu, r, scope, parent, store, rw)     # inside a method: a nested class of its class
                if rk in store.decls:
                    store.decls[rk]['local_in'] = key
                    store.decls[rk]['local_name'] = r.get('name')
                    locs.append(rk)
            if locs:
                d['local_records'] = locs
        store.put(key, d)
        return
    # UsingDirectiveDecl, StaticAssertDecl, EmptyDecl and the like carry nothing to translate


def main():
    astdir, src, out = sys.argv[1], os.path.normpath(sys.argv[2]), sys.argv[3]
    files = sorted(f for f in os.listdir(astdir) if f.endswith('.jsonl.gz'))
    store = Store()
    for i, fn in enumerate(files):
        tu = TU(src)
        elems = []
        with gzip.open(os.path.join(astdir, fn), 'rt') as g:
            for line in g:
                e = json.loads(line)
                unfold_array_filler(e)
                tu.cur = e.pop('_file')
                elems.append((tu.cur, e))
        for f0, e in elems:
            tu.cur = f0
            index(tu, e, [], None)
        rw = Rewriter(tu)
        for f0, e in elems:
            flatten(tu, e, [], None, store, rw)
        print('%3d/%d %s  %d declarations so far' % (i + 1, len(files), fn, len(store.decls)), flush=True)
    with gzip.open(out, 'wt', compresslevel=3) as g:
        for key in store.order:
            g.write(json.dumps(store.decls[key], separators=(',', ':')) + '\n')
    c = collections.Counter(d['kind'] for d in store.decls.values())
    print(len(store.decls), 'declarations:', dict(c.most_common()))


if __name__ == '__main__':
    main()
