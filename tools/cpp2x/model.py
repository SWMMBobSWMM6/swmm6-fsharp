"""The engine's declarations as the C# (and later) back ends see them: loaded from tools/cpp2x/collect.py's table,
indexed by key, with records, their fields and methods, enumerations, the reachable set from the census, and
helpers to find constructors and in-class initializers.
Written by Claude (Anthropic); see AI-USE.md."""
import collections, gzip, json, re

import ctypes as C

FUNC_KINDS = {'FunctionDecl', 'CXXMethodDecl', 'CXXConstructorDecl', 'CXXDestructorDecl', 'CXXConversionDecl'}
RECORD_KINDS = {'CXXRecordDecl', 'ClassTemplateSpecializationDecl'}


def tstr(t):
    return (t or {}).get('desugaredQualType') or (t or {}).get('qualType') or ''


def kids(n):
    return n.get('inner', ())


def body_of(d):
    for c in kids(d):
        if c.get('kind') in ('CompoundStmt', 'CXXTryStmt'):
            return c
    return None


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


class Model:
    def __init__(self, path, reachable=None):
        self.d = {}
        self.order = []
        self.unreached_parent = {}      # function key -> its class, for functions the census did not reach
        self.lambdas = []               # every LambdaExpr in every function, reached or not
        with gzip.open(path, 'rt') as g:
            for line in g:
                x = json.loads(line)
                if x['kind'] in FUNC_KINDS:
                    self.lambdas.extend(find_lambdas(x))
                if reachable is not None and x['kind'] in FUNC_KINDS and x['key'] not in reachable:
                    self.unreached_parent[x['key']] = x.get('parent')
                    continue
                self.d[x['key']] = x
                self.order.append(x['key'])
        self.reachable = reachable
        self.records = {k: v for k, v in self.d.items() if v['kind'] in RECORD_KINDS}
        self.enums = {k: v for k, v in self.d.items() if v['kind'] == 'EnumDecl'}
        # classes and enums local to a function: function key -> {name as written: key}
        self.local_types = collections.defaultdict(dict)
        for k, v in self.d.items():
            if v.get('local_in'):
                self.local_types[v['local_in']][v.get('local_name')] = k
                # a class local to a method is placed inside the method's class (its scope names that class)
                owner = self.d.get(v['local_in'], {}).get('parent') or self.unreached_parent.get(v['local_in'])
                if owner and not v.get('parent'):
                    v['parent'] = owner
        self.funcs = {k: v for k, v in self.d.items() if v['kind'] in FUNC_KINDS}
        # records by the name a type string uses (qualified, with template arguments as clang prints them)
        self.rec_by_name = {}
        for k, r in self.records.items():
            self.rec_by_name[k] = r
        self.enum_by_name = dict(self.enums)
        self.methods = collections.defaultdict(list)
        for k, f in self.funcs.items():
            if f.get('parent'):
                self.methods[f['parent']].append(k)
        self.fields = {}
        for k, r in self.records.items():
            for f in r.get('fields', []):
                if f.get('key'):
                    self.fields[f['key']] = (k, f)
        self.enumerators = {}
        for k, e in self.enums.items():
            for c in e.get('enumerators', []):
                if c.get('key'):
                    self.enumerators[c['key']] = (k, c)
        self.vars = {k: v for k, v in self.d.items() if v['kind'] == 'VarDecl'}

    def record(self, name):
        return self.rec_by_name.get(name)

    def is_engine_record(self, t):
        t = t.strip() if isinstance(t, C.Type) else t
        return isinstance(t, C.Rec) and self.key_of_type(t) in self.records

    def key_of_type(self, t):
        """The record key a Rec type names (template arguments included)."""
        if not isinstance(t, C.Rec):
            return None
        nm = t.name
        if t.args:
            nm = nm + '<' + ', '.join(_targ_repr(a) for a in t.args) + '>'
        return nm

    def ctors(self, rec_key):
        return [self.d[m] for m in self.methods.get(rec_key, []) if self.d[m]['kind'] == 'CXXConstructorDecl']

    def dtor(self, rec_key):
        for m in self.methods.get(rec_key, []):
            if self.d[m]['kind'] == 'CXXDestructorDecl':
                return self.d[m]
        return None

    def bases(self, rec_key):
        r = self.records.get(rec_key)
        if not r:
            return []
        out = []
        for b in r.get('bases', []):
            bt = C.parse(tstr(b.get('type')))
            out.append((self.key_of_type(bt), b))
        return out


def _targ_repr(a):
    if isinstance(a, C.Value):
        return a.v
    return repr(a)
