"""C# names for the engine's declarations: namespaces, classes, members, overloads and locals.

A C++ namespace a::b becomes the C# namespace a.b; its free functions and variables live in a static class _F
there. Records keep their names (template specializations get their arguments spelled into the name); methods
keep their names unless overloaded, when every overload gets a suffix spelled from its parameter types, the
same suffix in a base and a derived class so overrides still override. Operators get readable names.
Written by Claude (Anthropic); see AI-USE.md."""
import re

import ctypes as C

CS_KEYWORDS = set('''abstract as base bool break byte case catch char checked class const continue decimal default
delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int
interface internal is lock long namespace new null object operator out override params private protected public
readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof
uint ulong unchecked unsafe ushort using virtual void volatile while value var dynamic record init'''.split())

OPS = {'operator==': 'op_Eq', 'operator!=': 'op_Ne', 'operator<': 'op_Lt', 'operator>': 'op_Gt', 'operator<=': 'op_Le',
       'operator>=': 'op_Ge', 'operator()': 'op_Call', 'operator[]': 'op_Index', 'operator=': 'op_Assign',
       'operator+': 'op_Add', 'operator-': 'op_Sub', 'operator*': 'op_Mul', 'operator/': 'op_Div', 'operator%': 'op_Mod',
       'operator+=': 'op_AddAssign', 'operator-=': 'op_SubAssign', 'operator*=': 'op_MulAssign', 'operator/=': 'op_DivAssign',
       'operator<<': 'op_Shl', 'operator>>': 'op_Shr', 'operator!': 'op_Not', 'operator->': 'op_Arrow', 'operator++': 'op_Inc',
       'operator--': 'op_Dec', 'operator<=>': 'op_Cmp', 'operator&&': 'op_And', 'operator||': 'op_Or', 'operator bool': 'op_Bool',
       'operator&': 'op_BitAnd', 'operator|': 'op_BitOr', 'operator^': 'op_Xor', 'operator~': 'op_Compl'}


def ident(s):
    s = re.sub(r'[^A-Za-z0-9_]', '_', s)
    if not s or s[0].isdigit():
        s = '_' + s
    if s in CS_KEYWORDS:
        s = '@' + s
    return s


def ns_name(scope):
    """C++ scope 'openswmm::outfall' -> C# namespace 'openswmm.outfall'; anonymous namespaces get their file."""
    parts = []
    for p in scope.split('::') if scope else []:
        m = re.match(r'\(anon:(.*)\)$', p)
        if m:
            parts.append('_anon_' + re.sub(r'[^A-Za-z0-9]', '_', m.group(1).replace('src/engine/', '')))
        elif p == '(anonymous)':
            parts.append('_anon')
        else:
            parts.append(ident(p).lstrip('@'))
    return '.'.join(parts) or '_global'


def type_suffix(t):
    """A short spelling of a parameter type for overload suffixes."""
    t0 = t
    out = ''
    while isinstance(t, (C.LRef, C.RRef, C.Ptr)):
        out += {C.LRef: 'r', C.RRef: 'm', C.Ptr: 'p'}[type(t)]
        t = t.t
    if isinstance(t, C.Builtin):
        out += {'double': 'd', 'float': 'f', 'int': 'i', 'unsigned int': 'u', 'long': 'l', 'unsigned long': 'ul',
                'long long': 'l', 'unsigned long long': 'ul', 'bool': 'b', 'char': 'c', 'unsigned char': 'uc',
                'short': 's', 'unsigned short': 'us', 'void': 'v', 'signed char': 'sc'}.get(t.name, ident(t.name))
    elif isinstance(t, C.Rec):
        nm = t.name.split('::')[-1]
        nm = {'basic_string': 'str', 'basic_string_view': 'sv', 'vector': 'vec', 'function': 'fn'}.get(nm, nm)
        out += ident(nm)
        if t.args and nm in ('vec', 'array', 'pair', 'optional', 'span'):
            out += '_' + '_'.join(type_suffix(a) if not isinstance(a, C.Value) else ident(a.v) for a in t.args)
    elif isinstance(t, C.Arr):
        out += 'a' + type_suffix(t.t)
    elif isinstance(t, C.Func):
        out += 'f'
    else:
        out += 'x'
    if t0.strip().const and not isinstance(t0, (C.LRef, C.RRef)):
        pass
    return out


def method_base_name(name):
    if name in OPS:
        return OPS[name]
    if name.startswith('operator '):
        return 'op_To_' + ident(name[9:])
    return ident(name)
