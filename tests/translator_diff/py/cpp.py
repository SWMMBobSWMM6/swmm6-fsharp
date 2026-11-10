"""Reference implementations of the helpers tools/cs2x/Cs2Py.cs emits (for the differential test only; the real
runtime is written separately from py/cs2py_runtime.txt and py/RUNTIME_HELPERS.md).
Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md."""
import math, struct
_f = struct.Struct('<f'); _d = struct.Struct('<d'); _q = struct.Struct('<q'); _Q = struct.Struct('<Q')
_INF = float('inf')

def f64_from_bits(b): return _d.unpack(_Q.pack(b & 0xFFFFFFFFFFFFFFFF))[0]
def f64_to_bits_signed(x): return _q.unpack(_d.pack(x))[0]
DNAN = f64_from_bits(0xFFF8000000000000)        # x86's default NaN: what sqrt(-1), 0/0, inf-inf give
def signbit(x): return _q.unpack(_d.pack(x))[0] < 0

class Cell:
    __slots__ = ('v',)
    def __init__(self, v): self.v = v

class ArrRef:
    __slots__ = ('a', 'i')
    def __init__(self, a, i): self.a = a; self.i = i
    @property
    def v(self): return self.a[self.i]
    @v.setter
    def v(self, x): self.a[self.i] = x

class GotoSignal(Exception):
    def __init__(self, k): self.k = k

class CppException(Exception):
    def __init__(self, *a): Exception.__init__(self)

def throw_(e): raise e

def _f32_int(n):
    # an integer rounded to single precision directly (no double rounding)
    if -(1 << 53) <= n <= (1 << 53): return f32(float(n))
    s = -1 if n < 0 else 1; m = abs(n); e = m.bit_length() - 24
    q, r = divmod(m, 1 << e); half = 1 << (e - 1)
    if r > half or r == half and q & 1: q += 1
    v = float(q << e) if (q << e).bit_length() <= 1024 else _INF
    return f32(s * v)

def f32(x):
    if type(x) is int: return _f32_int(x)
    try: return _f.unpack(_f.pack(x))[0]
    except OverflowError: return math.copysign(_INF, x)

def f2i32(x):
    return int(x) if -2147483649.0 < x < 2147483648.0 else -2147483648
def f2i64(x):
    return int(x) if -9223372036854775808.0 <= x < 9223372036854775808.0 else -9223372036854775808
def f2u32(x): return f2i64(x) & 0xFFFFFFFF
def f2u64(x):
    if x < 9223372036854775808.0: return f2i64(x) & 0xFFFFFFFFFFFFFFFF
    return (f2i64(x - 9223372036854775808.0) & 0xFFFFFFFFFFFFFFFF) ^ 0x8000000000000000
def f2i16(x): v = f2i32(x) & 0xFFFF; return v - 0x10000 if v & 0x8000 else v
def f2u16(x): return f2i32(x) & 0xFFFF
def f2i8(x): v = f2i32(x) & 0xFF; return v - 0x100 if v & 0x80 else v
def f2u8(x): return f2i32(x) & 0xFF
Conv_D2I = f2i32

def idiv(a, b):
    q = abs(a) // abs(b)          # raises ZeroDivisionError for b == 0, as C# raises DivideByZeroException
    return q if (a < 0) == (b < 0) else -q
def irem(a, b):
    r = abs(a) % abs(b)
    return -r if a < 0 else r

def fdiv0(a, b):
    # a / b with b == 0 (+0.0, -0.0 or int 0): IEEE
    a = float(a)
    if a != a or a == 0.0: return DNAN
    neg = (a < 0) != signbit(float(b))
    return -_INF if neg else _INF

def fmod(a, b):
    a = float(a); b = float(b)
    if a != a or b != b or math.isinf(a) or b == 0.0: return a * b if (a != a or b != b) else DNAN
    if math.isinf(b): return a
    return math.fmod(a, b)

def Math_Sqrt_d(x):
    if x != x: return x
    if x < 0: return DNAN
    return math.sqrt(x)

def Math_Floor_d(x):
    x = float(x)
    if x != x or math.isinf(x) or x == 0.0: return x
    r = float(math.floor(x))
    return -0.0 if r == 0.0 and x < 0 else r

def _net_r(x):
    # .NET Core 3.0+ double.ToString(): the shortest round-trip digits, E notation below 1e-5 and from 1e15
    if x != x: return 'NaN'
    if math.isinf(x): return '-∞' if x < 0 else '∞'
    if x == 0.0: return '-0' if signbit(x) else '0'
    r = repr(abs(x)); sign = '-' if x < 0 else ''
    if 'e' in r: mant, ex = r.split('e'); ex = int(ex)
    else: mant, ex = r, 0
    if '.' in mant: ip, fp = mant.split('.')
    else: ip, fp = mant, ''
    if fp == '0': fp = ''
    digits = (ip + fp).lstrip('0'); point = len(ip) + ex
    if ip.strip('0') == '':
        lead = len(fp) - len(fp.lstrip('0')); point = -lead + ex
    digits = digits.rstrip('0') or '0'
    e10 = point - 1
    if e10 < -5 or e10 >= 15:
        m = digits[0] + ('.' + digits[1:] if len(digits) > 1 else '')
        return sign + m + 'E' + ('+' if e10 >= 0 else '-') + '%02d' % abs(e10)
    if point <= 0: return sign + '0.' + '0' * (-point) + digits
    if point >= len(digits): return sign + digits + '0' * (point - len(digits))
    return sign + digits[:point] + '.' + digits[point:]

def cs_tostring(v):
    if v is None: return ''
    if v is True: return 'True'
    if v is False: return 'False'
    if type(v) is int: return str(v)
    if type(v) is float: return _net_r(v)
    if type(v) is str: return v
    return v.ToString()

class IDisposable:
    __slots__ = ()
