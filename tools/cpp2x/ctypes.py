"""C++ type strings, as clang's JSON AST prints them, parsed into small Type objects.

  parse('const std::vector<double> &')  ->  LRef(Rec('std::vector', [Builtin('double')]), const inside)
  parse('double (*)(const openswmm::XSectParams &, double)')  ->  Ptr(Func(...))

Kinds: Builtin, Rec (class/struct/union/enum by qualified name, with template arguments), Ptr, LRef, RRef,
Arr (fixed size or unknown), Func, Closure (a lambda's type), Value (a non-type template argument).
Written by Claude (Anthropic); see AI-USE.md."""
import re

BUILTINS = {
    'void', 'bool', 'char', 'signed char', 'unsigned char', 'short', 'unsigned short', 'int', 'unsigned int',
    'long', 'unsigned long', 'long long', 'unsigned long long', 'float', 'double', 'long double', 'wchar_t',
    'char8_t', 'char16_t', 'char32_t', '__int128', 'unsigned __int128', 'std::nullptr_t', 'nullptr_t',
}
WORDS = {'unsigned', 'signed', 'short', 'long', 'int', 'char', 'double', 'float', 'bool', 'void', 'wchar_t',
         'char8_t', 'char16_t', 'char32_t', '__int128'}


class Type:
    const = False
    volatile = False

    def strip(self):
        """The type without references and top-level const."""
        t = self
        while isinstance(t, (LRef, RRef)):
            t = t.t
        return t

    def __repr__(self):
        return ('const ' if self.const else '') + self._r()


class Builtin(Type):
    def __init__(self, name):
        self.name = name

    def _r(self):
        return self.name

    def __eq__(self, o):
        return isinstance(o, Builtin) and o.name == self.name

    def __hash__(self):
        return hash(self.name)


class Rec(Type):
    """A named class, struct, union or enum: qualified name and template arguments."""

    def __init__(self, name, args=None, tag=None):
        self.name, self.args, self.tag = name, args or [], tag

    def _r(self):
        return self.name + ('<%s>' % ', '.join(map(repr, self.args)) if self.args else '')

    def base(self):
        return self.name


class Ptr(Type):
    def __init__(self, t):
        self.t = t

    def _r(self):
        return repr(self.t) + ' *'


class ArrPtr(Ptr):
    """A pointer to an engine object that the code indexes (p[i]) or offsets (p + k): held as Cpp.Ptr<T>."""

    def _r(self):
        return repr(self.t) + ' *'


class LRef(Type):
    def __init__(self, t):
        self.t = t

    def _r(self):
        return repr(self.t) + ' &'


class RRef(Type):
    def __init__(self, t):
        self.t = t

    def _r(self):
        return repr(self.t) + ' &&'


class Arr(Type):
    def __init__(self, t, n):
        self.t, self.n = t, n

    def _r(self):
        return '%r[%s]' % (self.t, '' if self.n is None else self.n)


class Func(Type):
    def __init__(self, ret, params, variadic=False, quals=''):
        self.ret, self.params, self.variadic, self.quals = ret, params, variadic, quals

    def _r(self):
        return '%r (%s)%s' % (self.ret, ', '.join(map(repr, self.params)) + (', ...' if self.variadic else ''),
                              (' ' + self.quals) if self.quals else '')


class MemPtr(Type):
    """A pointer to data member: T C::*."""

    def __init__(self, cls, t):
        self.cls, self.t = cls, t

    def _r(self):
        return '%r %r::*' % (self.t, self.cls)


class Closure(Type):
    """A lambda's closure type: (lambda at FILE:LINE:COL)."""

    def __init__(self, where):
        self.where = where

    def _r(self):
        return '(lambda at %s)' % self.where


class Value(Type):
    """A non-type template argument such as the 4 in std::array<double, 4>."""

    def __init__(self, v):
        self.v = v

    def _r(self):
        return str(self.v)


TOK = re.compile(r'\s*(\(lambda at [^)]*\)|\((?:unnamed|anonymous) (?:struct|union|class|enum) at [^)]*\)|\(anonymous namespace\)|'
                 r'\(anon:[^)]*\)|\(anon@[^)]*\)|'
                 r'<dependent type>|decltype\((?:[^()]|\([^()]*\))*\)|->|::|&&|[A-Za-z_][A-Za-z_0-9]*|'
                 r'-?\d+[A-Za-z]*|[<>(),*&\[\]]|\.\.\.|.)')


def tokens(s):
    out, i = [], 0
    while i < len(s):
        m = TOK.match(s, i)
        if not m:
            break
        t = m.group(1)
        i = m.end()
        if t.strip():
            out.append(t)
    return out


class Parser:
    def __init__(self, s):
        self.toks = tokens(s)
        self.i = 0
        self.src = s

    def peek(self, k=0):
        j = self.i + k
        return self.toks[j] if j < len(self.toks) else None

    def take(self, t=None):
        x = self.peek()
        if t is not None and x != t:
            raise ValueError('expected %r at %r in %r' % (t, x, self.src))
        self.i += 1
        return x

    def type(self):
        """A full type, including a declarator such as ' *', ' &', '[4]', ' (int)' or ' (*)(int)'."""
        t = self.spec()
        return self.declarator(t)

    def spec(self):
        const = volatile = False
        tag = None
        while self.peek() in ('const', 'volatile', 'struct', 'class', 'union', 'enum', 'typename'):
            w = self.take()
            if w == 'const':
                const = True
            elif w == 'volatile':
                volatile = True
            elif w != 'typename':
                tag = w
        x = self.peek()
        if x is None:
            raise ValueError('empty type in %r' % self.src)
        if x == '<dependent type>' or x.startswith('decltype('):
            self.take()
            t = Rec(x)
        elif x.startswith('(lambda at'):
            self.take()
            t = Closure(x[len('(lambda at '):-1])
        elif x.startswith('(unnamed') or (x.startswith('(anonymous') and x != '(anonymous namespace)'):
            self.take()
            t = Rec(x, tag=tag)
        elif x in WORDS:
            words = []
            while self.peek() in WORDS:
                words.append(self.take())
            nm = ' '.join(words)
            if nm in ('unsigned', 'signed'):
                nm += ' int'
            nm = {'long int': 'long', 'unsigned long int': 'unsigned long', 'short int': 'short',
                  'unsigned short int': 'unsigned short', 'long long int': 'long long',
                  'unsigned long long int': 'unsigned long long', 'signed int': 'int'}.get(nm, nm)
            t = Builtin(nm)
        else:
            t = self.qname(tag)
        while self.peek() in ('const', 'volatile', '__restrict', '__restrict__'):
            w = self.take()
            if w == 'const':
                const = True
            elif w == 'volatile':
                volatile = True
        t.const = const or t.const
        t.volatile = volatile
        return t

    def qname(self, tag):
        parts = []
        args = []
        if self.peek() == '::':
            self.take()
        while True:
            nm = self.take()
            if nm == '(anonymous namespace)' or (nm or '').startswith('(anon:'):
                parts.append(nm)
                self.take('::')
                continue
            if (nm or '').startswith('(unnamed ') or (nm or '').startswith('(anonymous struct') or \
                    (nm or '').startswith('(anonymous union'):
                parts.append(nm)          # ns::(unnamed struct at FILE:L:C)
                break
            if (nm or '').startswith('(anon@'):
                parts.append(nm)          # a keyed unnamed record (the repaired types of the C# back end)
                if self.peek() == '::' and self.peek(1) != '*':
                    self.take()
                    continue
                break
            if not re.match(r'[A-Za-z_]', nm or ''):
                raise ValueError('bad name %r in %r' % (nm, self.src))
            if self.peek() == '<':
                a = self.targs()
                nm_full = nm
                if self.peek() == '::':
                    # a nested name inside a template: keep the arguments in the scope part
                    parts.append(nm_full + '<%s>' % ', '.join(map(repr, a)))
                    self.take()
                    continue
                parts.append(nm_full)
                args = a
            else:
                parts.append(nm)
            if self.peek() == '::' and self.peek(1) != '*':
                self.take()
                continue
            if self.peek() == '::' and self.peek(1) == '*':
                self.take()
                self.take('*')
            break
        name = '::'.join(parts)
        if name in ('std::nullptr_t', 'nullptr_t'):
            return Builtin('std::nullptr_t')
        return Rec(name, args, tag)

    def targs(self):
        self.take('<')
        out = []
        if self.peek() == '>':
            self.take()
            return out
        while True:
            x = self.peek()
            if x is not None and (re.match(r'-?\d', x) or x in ('true', 'false')):
                self.take()
                out.append(Value(x))
            else:
                out.append(self.type())
            if self.peek() == ',':
                self.take()
                continue
            self.take('>')
            return out

    def declarator(self, t):
        while True:
            x = self.peek()
            if x == '*':
                self.take()
                t = Ptr(t)
            elif x == '&':
                self.take()
                t = LRef(t)
            elif x == '&&':
                self.take()
                t = RRef(t)
            elif x in ('const', 'volatile', '__restrict', '__restrict__'):
                self.take()
                if x == 'const':
                    t.const = True
            elif x == '[':
                self.take()
                n = None
                if self.peek() != ']':
                    n = int(re.match(r'-?\d+', self.take()).group(0))
                self.take(']')
                t = self.arrays(t, n)
            elif x is not None and (x == '::' or re.match(r'[A-Za-z_]', x)) and self.memptr_ahead():
                cls = self.qname(None)       # stops at the '::' before the '*'
                t = MemPtr(cls, t)
            elif x == '(':
                # 'R (params)' is a function type; 'R (*)(params)' / 'R (&)(params)' point or refer to one;
                # 'T (*)[N]' / 'T (&)[N]' point or refer to an array
                if self.peek(1) in ('*', '&', '&&'):
                    self.take('(')
                    kinds = []
                    while self.peek() != ')':
                        kinds.append(self.take())
                    self.take(')')
                    if self.peek() == '[':
                        self.take('[')
                        n = int(re.match(r'-?\d+', self.take()).group(0))
                        self.take(']')
                        f = self.arrays(t, n)
                    else:
                        f = self.function(t)
                    for k in kinds:
                        if k == '*':
                            f = Ptr(f)
                        elif k == '&':
                            f = LRef(f)
                        elif k == '&&':
                            f = RRef(f)
                    t = f
                else:
                    t = self.function(t)
            else:
                return t

    def memptr_ahead(self):
        """Whether the next tokens spell 'C::*' (a class name, then '::*')."""
        j = 0
        if self.peek(j) == '::':
            j += 1
        while True:
            x = self.peek(j)
            if x is None or not re.match(r'[A-Za-z_]', x):
                return False
            if self.peek(j + 1) != '::':
                return False
            if self.peek(j + 2) == '*':
                return True
            j += 2

    def arrays(self, t, n):
        # 'int [2][3]' is an array of 2 arrays of 3
        dims = [n]
        while self.peek() == '[':
            self.take()
            m = int(re.match(r'-?\d+', self.take()).group(0))
            self.take(']')
            dims.append(m)
        for d in reversed(dims):
            t = Arr(t, d)
        return t

    def function(self, ret):
        self.take('(')
        params, variadic = [], False
        while self.peek() != ')':
            if self.peek() == '...':
                self.take()
                variadic = True
            else:
                p = self.type()
                if not (isinstance(p, Builtin) and p.name == 'void' and self.peek() == ')' and not params):
                    params.append(p)
            if self.peek() == ',':
                self.take()
        self.take(')')
        quals = []
        while self.peek() in ('const', 'noexcept', '&', '&&', 'volatile'):
            q = self.take()
            quals.append(q)
            if q == 'noexcept' and self.peek() == '(':
                depth = 0
                while True:
                    x = self.take()
                    depth += x == '('
                    depth -= x == ')'
                    if depth == 0:
                        break
        if self.peek() == '->':
            self.take()
            ret = self.type()
        return Func(ret, params, variadic, ' '.join(quals))


_cache = {}
_resolver = None          # set by a back end: rewrites names in parsed types for the current scope
_rcache = {}


def set_resolver(r):
    """r(name) -> a Type to substitute for a Rec named name, or None; r.key identifies its context."""
    global _resolver
    _resolver = r


def resolve(t, r):
    """A copy of t with every record name passed through r (template arguments first)."""
    if isinstance(t, Rec):
        args = [resolve(a, r) for a in t.args]
        sub = r(t.name, args)
        if sub is not None:
            out = sub if not t.const else _with_const(sub)
            return out
        n = Rec(t.name, args, t.tag)
        n.const = t.const
        return n
    if isinstance(t, (Ptr, LRef, RRef)):
        n = type(t)(resolve(t.t, r))
        n.const = t.const
        return n
    if isinstance(t, Arr):
        n = Arr(resolve(t.t, r), t.n)
        n.const = t.const
        return n
    if isinstance(t, MemPtr):
        n = MemPtr(resolve(t.cls, r), resolve(t.t, r))
        n.const = t.const
        return n
    if isinstance(t, Func):
        n = Func(resolve(t.ret, r), [resolve(p, r) for p in t.params], t.variadic, t.quals)
        n.const = t.const
        return n
    return t


def _with_const(t):
    import copy
    c = copy.copy(t)
    c.const = True
    return c


class Opaque(Type):
    """A spelling clang uses for something that is not a real type here (a bound member function, a builtin
    function, a generic lambda's conversion function). The emitter treats any value of this type as an error."""

    def __init__(self, s):
        self.s = s

    def _r(self):
        return '<opaque %s>' % self.s


def parse(s):
    t = parse_raw(s)
    if _resolver is None:
        return t
    k = (s, _resolver.key)
    if k not in _rcache:
        _rcache[k] = resolve(t, _resolver)
    return _rcache[k]


def parse_raw(s):
    if s in _cache:
        return _cache[s]
    if s.startswith('<') and s.endswith('>') and s != '<dependent type>':
        t = Opaque(s)
    else:
        try:
            p = Parser(s)
            t = p.type()
            if p.peek() is not None:
                raise ValueError('trailing %r in %r' % (p.toks[p.i:], s))
        except ValueError:
            t = Opaque(s)
    _cache[s] = t
    return t


def of(node):
    """The parsed type of an AST node (desugared when clang gives the desugared spelling). A node the C# back end
    marked as an object pointer used as an array has that pointer type (ArrPtr)."""
    t = node.get('type') or {}
    r = parse(t.get('desugaredQualType') or t.get('qualType'))
    if node.get('_arrptr'):
        base = r.t if isinstance(r, (LRef, RRef)) else r
        if isinstance(base, Ptr) and not isinstance(base, ArrPtr):
            a = ArrPtr(base.t)
            a.const = base.const
            return LRef(a) if isinstance(r, LRef) else a
    return r
