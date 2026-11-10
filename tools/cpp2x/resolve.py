"""C++ name lookup for type names clang printed unqualified: `SimulationContext` inside namespace openswmm is
openswmm::SimulationContext, `fv::INetworkSolver` inside openswmm::hydraulics is openswmm::hydraulics::fv::..., a
typedef or alias becomes the type it names, and the standard library's aliases (std::string, std::size_t,
std::uint64_t, ...) become what libstdc++ defines them as.
Written by Claude (Anthropic); see AI-USE.md."""
import ctypes as C
from model import tstr

U8 = lambda n: C.Builtin(n)
STD_ALIASES = {
    'string': lambda a: C.Rec('std::basic_string', [C.Builtin('char')]),
    'string_view': lambda a: C.Rec('std::basic_string_view', [C.Builtin('char')]),
    'size_t': lambda a: C.Builtin('unsigned long'), 'ptrdiff_t': lambda a: C.Builtin('long'),
    'uint8_t': lambda a: C.Builtin('unsigned char'), 'int8_t': lambda a: C.Builtin('signed char'),
    'uint16_t': lambda a: C.Builtin('unsigned short'), 'int16_t': lambda a: C.Builtin('short'),
    'uint32_t': lambda a: C.Builtin('unsigned int'), 'int32_t': lambda a: C.Builtin('int'),
    'uint64_t': lambda a: C.Builtin('unsigned long'), 'int64_t': lambda a: C.Builtin('long'),
    'uintptr_t': lambda a: C.Builtin('unsigned long'), 'intptr_t': lambda a: C.Builtin('long'),
    'time_t': lambda a: C.Builtin('long'), 'streamsize': lambda a: C.Builtin('long'),
    'ssize_t': lambda a: C.Builtin('long'), 'off_t': lambda a: C.Builtin('long'), '__off_t': lambda a: C.Builtin('long'),
    '__ssize_t': lambda a: C.Builtin('long'), 'mode_t': lambda a: C.Builtin('unsigned int'),
    'streamoff': lambda a: C.Builtin('long'), 'streampos': lambda a: C.Builtin('long'),
    'size_type': lambda a: C.Builtin('unsigned long'), 'difference_type': lambda a: C.Builtin('long'),
    'FILE': lambda a: C.Rec('FILE'),
    'ostream': lambda a: C.Rec('std::basic_ostream', [C.Builtin('char')]),
    'istream': lambda a: C.Rec('std::basic_istream', [C.Builtin('char')]),
    'ifstream': lambda a: C.Rec('std::basic_ifstream', [C.Builtin('char')]),
    'ofstream': lambda a: C.Rec('std::basic_ofstream', [C.Builtin('char')]),
    'fstream': lambda a: C.Rec('std::basic_fstream', [C.Builtin('char')]),
    'ostringstream': lambda a: C.Rec('std::basic_ostringstream', [C.Builtin('char')]),
    'istringstream': lambda a: C.Rec('std::basic_istringstream', [C.Builtin('char')]),
    'stringstream': lambda a: C.Rec('std::basic_stringstream', [C.Builtin('char')]),
    'ios': lambda a: C.Rec('std::basic_ios', [C.Builtin('char')]),
    'nullptr_t': lambda a: C.Builtin('std::nullptr_t'),
    'max_align_t': lambda a: C.Builtin('long double'),
}


STD_TEMPLATES = {'vector', 'basic_string', 'basic_string_view', 'pair', 'array', 'map', 'unordered_map', 'set',
                 'unordered_set', 'optional', 'allocator', 'char_traits', 'function', 'unique_ptr', 'shared_ptr',
                 'tuple', 'span', 'deque', 'list', 'less', 'hash', 'equal_to', 'variant', 'atomic', 'stack', 'queue'}


class Resolver:
    def __init__(self, model):
        self.m = model
        self.aliases = {k: v for k, v in model.d.items() if v['kind'] in ('TypedefDecl', 'TypeAliasDecl')}
        self.key = None
        self.locals = {}
        self.file = None
        self.scopes = ['']
        self.recs = []
        self._busy = set()

    def set(self, scope, rec, file=None, func=None):
        """The context: the namespace path of the declaration being emitted, its enclosing record, its file, and
        the function whose local classes are in scope (the one emitted, or the one a local class belongs to)."""
        self.file = file
        self.locals = {}
        r = rec
        while r and r in self.m.records and not func:
            func = self.m.records[r].get('local_in')
            r = self.m.records[r].get('parent')
        if func:
            self.locals = self.m.local_types.get(func, {})
        chain = []
        r = rec
        while r and r in self.m.records:
            chain.append(r)
            r = self.m.records[r].get('parent')
        parts = [p for p in (scope or '').split('::') if p]
        self.scopes = ['::'.join(parts[:i]) for i in range(len(parts), -1, -1)]
        self.recs = chain
        self.key = (scope, rec, file, func if self.locals else None)

    def save(self):
        return (self.key, self.scopes, self.recs, self.locals, self.file)

    def restore(self, st):
        self.key, self.scopes, self.recs, self.locals, self.file = st

    def known(self, name):
        if not hasattr(self, '_templates'):
            # an engine class template is known only by its specializations (ns::Batch<true>); a type that names
            # it with arguments (Batch<true>) is looked up by the template's name
            self._templates = {k[:k.index('<')] for k, r in self.m.records.items()
                               if r.get('kind') == 'ClassTemplateSpecializationDecl' and '<' in k}
        return name in self.m.records or name in self.m.enums or name in self._templates

    def unnamed(self, name):
        """'(unnamed struct at /.../src/engine/x.cpp:159:11)' -> the record key '...(anon@src/engine/x.cpp:159)'."""
        import re
        mm = re.match(r'\((?:unnamed|anonymous) (?:struct|union|class) at (.*):(\d+):\d+\)$', name)
        if not mm:
            return None
        path, line = mm.group(1), mm.group(2)
        i = path.find('/src/')
        if i < 0:
            i = path.find('/include/')
        rel = path[i + 1:] if i >= 0 else path
        if not hasattr(self, '_anon_recs'):
            self._anon_recs = {}
            for k in self.m.records:
                m2 = re.search(r'\(anon@([^:()]+):(\d+)\)$', k)
                if m2:
                    self._anon_recs[(m2.group(1), m2.group(2))] = k
        return self._anon_recs.get((rel, line))

    def __call__(self, name, args):
        last = name.split('::')[-1] if '::(' in name else name
        if last.startswith('(unnamed ') or last.startswith('(anonymous struct') or last.startswith('(anonymous union'):
            k = self.unnamed(last)
            return C.Rec(k) if k else None
        if name.startswith('std::') or name.startswith('__gnu_cxx::'):
            short = name[5:] if name.startswith('std::') else None
            if short in STD_ALIASES and not args:
                return STD_ALIASES[short](args)
            return None
        if name in STD_TEMPLATES:
            # clang prints a template argument substituted inside std unqualified: vector<basic_string<char>>
            if not self.known(name) and self._find(name) is None:
                return C.Rec('std::' + name, args)
        if args:
            # a template written unqualified: try the scopes for its specialization
            full = self._find(name)
            if full and full != name:
                return C.Rec(full, args)
            return None
        if self.known(name):
            return None
        if name in STD_ALIASES:
            return STD_ALIASES[name](args)
        full = self._find(name)
        if full is None:
            return None
        if full in self.aliases:
            if full in self._busy:
                return None
            a = self.aliases[full]
            self._busy.add(full)
            try:
                saved = (self.key, self.scopes, self.recs, self.locals, self.file)
                self.set(a.get('scope', ''), a.get('parent'))
                t = C.parse(tstr(a.get('type')))
                self.key, self.scopes, self.recs, self.locals, self.file = saved
            finally:
                self._busy.discard(full)
            return t
        if full != name:
            return C.Rec(full)
        return None

    def _find(self, name):
        if name in self.locals:
            return self.locals[name]
        if '(anonymous namespace)' in name:
            if self.file:
                c = name.replace('(anonymous namespace)', '(anon:%s)' % self.file)
                if self.known(c) or c in self.aliases:
                    return c
            return self._unique(name)
        for r in self.recs:
            c = r + '::' + name
            if self.known(c) or c in self.aliases:
                return c
        for s in self.scopes:
            c = (s + '::' + name) if s else name
            if self.known(c) or c in self.aliases:
                return c
            if self.file:
                # names of this file's anonymous namespace are visible in the namespace around it
                c = ((s + '::') if s else '') + '(anon:%s)::%s' % (self.file, name)
                if self.known(c) or c in self.aliases:
                    return c
        # names the scope chain does not reach (a using-declaration or using-directive): unique by last component
        return self._unique(name)

    def _unique(self, name):
        if not hasattr(self, '_by_last'):
            from collections import defaultdict
            self._by_last = defaultdict(set)
            for k in list(self.m.records) + list(self.m.enums) + list(self.aliases):
                self._by_last[split_last(k)].add(k)
        tail = split_last(name)
        cands = [k for k in self._by_last.get(tail, ()) if k.endswith('::' + name.split('::')[-1])]
        if '::' in name:
            q = name.replace('(anonymous namespace)', '')
            sub = [k for k in cands if k.replace('::', '|').endswith(q.replace('::', '|').lstrip('|'))]
            if len(sub) == 1:
                return sub[0]
        return cands[0] if len(cands) == 1 else None


def split_last(k):
    depth, cut = 0, 0
    for i, ch in enumerate(k):
        if ch == '<':
            depth += 1
        elif ch == '>':
            depth -= 1
        elif depth == 0 and k.startswith('::', i):
            cut = i + 2
    return k[cut:]
