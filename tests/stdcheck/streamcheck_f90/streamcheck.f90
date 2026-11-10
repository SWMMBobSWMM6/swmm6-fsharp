! streamcheck.f90 - the cases of streamcheck.cpp / streamcheck.cs through the Fortran runtime's streams and file
! system (f90/runtime/cpp_rt_d.f90, part D, over parts A, C and B), statement for statement as streamcheck.cs. Fortran
! leaves the order of the calls in one expression to the compiler, so every call with a side effect is made first,
! in the C#'s left-to-right order, and its result kept. Section a must equal the C# and Go outputs (and the C++
! output but for the one glibc %#g quirk, depending on part C's formatter); section b the C# and Go outputs (the C#
! port's own behaviour); section c the C++ and C# outputs. ErrorCode's message() is the C#'s table, here (part C
! owns ErrorCode; only its v is used).
! Run from tests/stdcheck (paths are relative to it): streamcheck_f90 a|b|c   (run_stream_f90.sh builds and runs it)
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
program streamcheck
  use cpp_runtime
  implicit none
  character(len=:), allocatable :: sb
  integer :: sbn = 0
  character(len=*), parameter :: NL = achar(10)
  character(len=16) :: sec
  class(cpp_ostream), pointer :: co, rr
  class(cpp_arr_b), pointer :: ob

  allocate(character(len=65536) :: sb)
  call cpp_rt_d_init()
  sec = 'a'
  if (command_argument_count() > 0) call get_command_argument(1, sec)
  if (trim(sec) == 'a') then
    call sec_a()
  else if (trim(sec) == 'b') then
    call sec_b()
  else
    call sec_c()
  end if
  ob => bytes_arr(sb(1:sbn))
  co => cpp_streams_cout
  rr => cpp_ostream_write_cptr_l(co, cpp_cptr(b=ob, o=0), int(sbn, 8))
  if (associated(cpp_exc)) stop 3

contains

  ! ------------------------------------------------------------------------------------------- helpers
  subroutine W(s)
    character(len=*), intent(in) :: s
    character(len=:), allocatable :: t
    if (sbn + len(s) > len(sb)) then
      allocate(character(len=2 * (sbn + len(s))) :: t)
      t(1:sbn) = sb(1:sbn)
      call move_alloc(t, sb)
    end if
    sb(sbn + 1:sbn + len(s)) = s
    sbn = sbn + len(s)
  end subroutine W

  subroutine chk()
    if (associated(cpp_exc)) then
      call W('!! exception' // NL)
      cpp_exc => null()
    end if
  end subroutine chk

  function bytes_arr(s) result(a)
    character(len=*), intent(in) :: s
    class(cpp_arr_b), pointer :: a
    integer :: i, c
    a => cpp_arr_b_new(int(len(s) + 1, 8))
    do i = 1, len(s)
      c = ichar(s(i:i))
      if (c > 127) c = c - 256
      a%a(i - 1) = int(c, 1)
    end do
  end function bytes_arr

  !> new Str(string)
  function S(x) result(p)
    character(len=*), intent(in) :: x
    class(cpp_str), pointer :: p
    p => cpp_str_new_cptr_ul(cpp_cptr(b=bytes_arr(x), o=0), int(len(x), 8))
  end function S

  !> Str.ToString()
  function T(x) result(r)
    class(cpp_str), intent(in) :: x
    character(len=:), allocatable :: r
    integer :: i
    allocate(character(len=x%n) :: r)
    do i = 1, x%n
      r(i:i) = char(iand(int(x%b%a(i - 1)), 255))
    end do
  end function T

  !> FsPath.ToString()
  function P(x) result(r)
    class(cpp_fspath), pointer, intent(in) :: x
    character(len=:), allocatable :: r
    r = cpp_fspath_tostring(x)
  end function P

  !> R.Lit(string)
  function L(x) result(r)
    character(len=*), intent(in) :: x
    type(cpp_cptr) :: r
    r = cpp_r_lit(x)
  end function L

  !> R.Str(CPtr): the bytes up to the NUL
  function CS(x) result(r)
    type(cpp_cptr), intent(in) :: x
    character(len=:), allocatable :: r
    integer :: n
    n = 0
    do while (x%b%a(x%o + n) /= 0)
      n = n + 1
    end do
    allocate(character(len=n) :: r)
    do n = 1, len(r)
      r(n:n) = char(iand(int(x%b%a(x%o + n - 1)), 255))
    end do
  end function CS

  function St(x) result(r)
    class(cpp_iosbase), intent(inout) :: x
    character(len=4) :: r
    r = '----'
    if (cpp_iosbase_good(x)) r(1:1) = 'g'
    if (cpp_iosbase_eof(x)) r(2:2) = 'e'
    if (cpp_iosbase_fail(x)) r(3:3) = 'f'
    if (cpp_iosbase_bad(x)) r(4:4) = 'b'
  end function St

  function G(v) result(r)
    real(8), intent(in) :: v
    character(len=:), allocatable :: r
    class(cpp_ostringstream), pointer :: o
    class(cpp_ostream), pointer :: q
    o => cpp_ostringstream_new_0()
    q => cpp_ostream_put_d(o, v)
    r = T(cpp_ostringstream_str_0(o))
  end function G

  function ci(v) result(r)
    integer(8), intent(in) :: v
    character(len=:), allocatable :: r
    character(len=24) :: t
    write(t, '(i0)') v
    r = trim(t)
  end function ci

  function c4(v) result(r)
    integer(4), intent(in) :: v
    character(len=:), allocatable :: r
    r = ci(int(v, 8))
  end function c4

  function cu(v) result(r)
    integer(8), intent(in) :: v
    character(len=:), allocatable :: r
    character(len=24) :: t
    integer(16) :: x
    x = int(v, 16)
    if (x < 0) x = x + 18446744073709551616_16
    write(t, '(i0)') x
    r = trim(t)
  end function cu

  function B(x) result(r)
    logical, intent(in) :: x
    character(len=1) :: r
    r = merge('1', '0', x)
  end function B

  function Bits(u) result(d)
    integer(8), intent(in) :: u
    real(8) :: d
    d = transfer(u, 1.0_8)
  end function Bits

  !> ErrorCode: v and (C#'s) message()
  function emsg(e) result(r)
    class(cpp_errorcode), intent(in) :: e
    character(len=:), allocatable :: r, m
    select case (e%v)
    case (0)
      m = 'Success'
    case (2)
      m = 'No such file or directory'
    case (13)
      m = 'Permission denied'
    case (17)
      m = 'File exists'
    case (20)
      m = 'Not a directory'
    case (21)
      m = 'Is a directory'
    case (95)
      m = 'Operation not supported'
    case default
      m = 'Unknown error ' // c4(e%v)
    end select
    r = c4(e%v) // ' (' // m // ')'
  end function emsg

  function fp(x) result(q)
    character(len=*), intent(in) :: x
    class(cpp_fspath), pointer :: q
    q => cpp_fspath_new_str(S(x))
  end function fp

  ! ------------------------------------------------------------------------------------------- section c
  subroutine sec_c()
    class(cpp_ifstream), pointer :: f
    class(cpp_istream), pointer :: r
    class(cpp_arr_i), pointer :: bb
    integer(8) :: tg
    f => cpp_ifstream_new_str_i(S('build/streamcheck.bin'), ior(cpp_ios_in, cpp_ios_binary))
    bb => cpp_arr_i_new(1_8)
    r => cpp_istream_read_ptrt_l__i(f, cpp_ptr_i_new_ta_l(bb, 0_8), 4_8)
    r => cpp_istream_seekg(f, 4_8, cpp_ios_cur)
    r => cpp_istream_read_ptrt_l__i(f, cpp_ptr_i_new_ta_l(bb, 0_8), 4_8)
    tg = cpp_istream_tellg(f)
    call W('cur at8 ' // c4(bb%a(0)) // ' tellg ' // ci(tg) // ' ' // St(f) // NL)
  end subroutine sec_c

  ! ------------------------------------------------------------------------------------------- section a
  subroutine sec_a()
    real(8) :: vals(36)
    integer :: precs(13), iv, mode, sp, ip, pp
    class(cpp_ostringstream), pointer :: o, q, ff
    class(cpp_ostream), pointer :: r
    integer(4) :: k4
    integer(8) :: k8
    vals = [0.0_8, Bits(int(z'8000000000000000', 8)), 1.0_8, -1.0_8, 0.1_8, 0.5_8, 1.5_8, 2.5_8, 123.456_8, 1e-5_8, &
        1.23456789e-7_8, 1e21_8, 1.7976931348623157e308_8, Bits(1_8), 2.2250738585072014e-308_8, 1.0_8 / 3.0_8, &
        2.0_8 / 3.0_8, 100.0_8, 1e15_8, 123456789012345678.0_8, 0.000123456_8, 9.999999_8, 99.995_8, 0.125_8, &
        Bits(int(z'7ff8000000000000', 8)), Bits(int(z'7ff0000000000000', 8)), Bits(int(z'fff0000000000000', 8)), &
        1e100_8, 5e-310_8, 3.14159265358979_8, -0.0004567_8, 1234567.0_8, 0.95_8, 0.05_8, 1e-300_8, 6.02214076e23_8]
    precs = [0, 1, 2, 3, 4, 6, 8, 10, 15, 16, 17, 20, -1]
    do iv = 1, size(vals)
      do mode = 0, 3
        do sp = 0, 1
          do ip = 1, size(precs)
            pp = precs(ip)
            if (mode == 3 .and. pp /= 6) cycle
            o => cpp_ostringstream_new_0()
            if (mode == 1) r => cpp_ostream_manip_fixed(o)
            if (mode == 2) r => cpp_ostream_manip_scientific(o)
            if (mode == 3) k4 = cpp_iosbase_setf_i_i(o, ior(cpp_ios_fixed, cpp_ios_scientific), cpp_ios_floatfield)
            if (sp == 1) r => cpp_ostream_manip_showpoint(o)
            k8 = cpp_iosbase_precision_l(o, int(pp, 8))
            r => cpp_ostream_put_d(o, vals(iv))
            call W('d ' // c4(mode) // c4(sp) // ' ' // c4(pp) // ' [' // T(cpp_ostringstream_str_0(o)) // ']' // NL)
          end do
        end do
      end do
      ff => cpp_ostringstream_new_0()
      r => cpp_ostream_put_f(ff, real(vals(iv), 4))
      call W('f [' // T(cpp_ostringstream_str_0(ff)) // ']' // NL)
    end do
    call chk()
    ! ---- widths, fills, booleans, quoting, integers
    o => cpp_ostringstream_new_0()
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_manip_setw(o, 10_8)
    r => cpp_ostream_put_d(o, 3.5_8)
    r => cpp_ostream_put_cptr(o, L(']['))
    r => cpp_ostream_manip_left(o)
    r => cpp_ostream_manip_setw(o, 8_8)
    r => cpp_ostream_manip_setfill(o, ichar('*'))
    r => cpp_ostream_put_i(o, -42)
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_manip_right(o)
    r => cpp_ostream_manip_setw(o, 6_8)
    r => cpp_ostream_put_cptr(o, L('ab'))
    r => cpp_ostream_put_cptr(o, L(']['))
    r => cpp_ostream_manip_setw(o, 2_8)
    r => cpp_ostream_put_cptr(o, L('abcdef'))
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_manip_setw(o, 5_8)
    r => cpp_ostream_manip_setfill(o, ichar('0'))
    r => cpp_ostream_put_u(o, 7)
    r => cpp_ostream_put_cptr(o, L(']['))
    r => cpp_ostream_put_i(o, 123)
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_manip_boolalpha(o)
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_put_b(o, .true.)
    r => cpp_ostream_put_c(o, int(ichar(' '), 1))
    r => cpp_ostream_put_b(o, .false.)
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_manip_noboolalpha(o)
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_put_b(o, .true.)
    r => cpp_ostream_put_b(o, .false.)
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_manip_setw(o, 12_8)
    r => cpp_ostream_manip_setfill(o, ichar(' '))
    r => cpp_ostream_manip_quoted_str(o, S('a"b\c'))
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_manip_quoted_cptr(o, L('plain'))
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_manip_fixed(o)
    r => cpp_ostream_manip_setprecision(o, 3_8)
    r => cpp_ostream_manip_setw(o, 9_8)
    r => cpp_ostream_put_d(o, 2.0_8 / 3.0_8)
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_manip_defaultfloat(o)
    r => cpp_ostream_manip_setprecision(o, 4_8)
    r => cpp_ostream_put_d(o, 2.0_8 / 3.0_8)
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_put_l(o, -huge(0_8) - 1)
    r => cpp_ostream_put_c(o, int(ichar(' '), 1))
    r => cpp_ostream_put_ul(o, -1_8)
    r => cpp_ostream_put_c(o, int(ichar(' '), 1))
    r => cpp_ostream_put_s(o, -5_2)
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    r => cpp_ostream_put_c(o, int(ichar('['), 1))
    r => cpp_ostream_put_c(o, int(ichar('x'), 1))
    r => cpp_ostream_put_c(o, 65_1)
    r => cpp_ostream_put_c(o, int(ichar(']'), 1))
    call W('w ' // T(cpp_ostringstream_str_0(o)) // NL)
    call chk()
    ! ---- an initial string, seekp
    o => cpp_ostringstream_new_str(S('abcdef'))
    r => cpp_ostream_put_cptr(o, L('XY'))
    k8 = cpp_ostream_tellp(o)
    call W('init [' // T(cpp_ostringstream_str_0(o)) // '] tellp ' // ci(k8) // NL)
    q => cpp_ostringstream_new_0()
    r => cpp_ostream_put_cptr(q, L('hello world'))
    r => cpp_ostream_seekp(q, 6_8, cpp_ios_beg)
    r => cpp_ostream_put_cptr(q, L('W'))
    r => cpp_ostream_seekp(q, -1_8, cpp_ios_end)
    r => cpp_ostream_put_cptr(q, L('D'))
    r => cpp_ostream_seekp(q, 2_8, cpp_ios_beg)
    r => cpp_ostream_seekp(q, 1_8, cpp_ios_cur)
    r => cpp_ostream_put_cptr(q, L('L'))
    k8 = cpp_ostream_tellp(q)
    call W('seekp [' // T(cpp_ostringstream_str_0(q)) // '] tellp ' // ci(k8) // NL)
    call chk()
    call sec_a_in()
    call sec_a_files()
  end subroutine sec_a

  subroutine sec_a_in()
    class(cpp_istringstream), pointer :: is, e
    class(cpp_istream), pointer :: r, ip
    real(8) :: d
    integer(4) :: i, a, bq, c, ee, x, u
    integer(8) :: LL, UU, k8, gc, tg
    class(cpp_str), pointer :: sx, line, f, rest
    class(cpp_arr_b), pointer :: buf
    integer :: k
    character(len=:), allocatable :: tx
    ! ---- extraction
    is => cpp_istringstream_new_str(S('  3.25 -7 abc' // NL // 'line two' // NL // NL // 'last'))
    d = -1
    i = -1
    sx => cpp_str_new_0()
    r => cpp_istream_get_dr(is, d)
    call W('x d ' // G(d) // ' ' // St(is) // NL)
    r => cpp_istream_get_ir(is, i)
    call W('x i ' // c4(i) // ' ' // St(is) // NL)
    r => cpp_istream_get_str(is, sx)
    call W('x s ' // T(sx) // ' ' // St(is) // NL)
    line => cpp_str_new_0()
    ip => is
    do k = 0, 3
      r => cpp_streams_getline_istream_str(ip, line)
      call W('x line [' // T(line) // '] ' // St(is) // NL)
    end do
    ! ---- integers
    is => cpp_istringstream_new_str(S('12 4294967296 -2147483648 2147483648 -9223372036854775808 9000000000000000000 ' &
        // '4294967295 7'))
    a = -1
    bq = -1
    c = -1
    ee = -1
    r => cpp_istream_get_ir(is, a)
    r => cpp_istream_get_ir(r, bq)
    call W('x ints ' // c4(a) // ' ' // c4(bq) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    r => cpp_istream_get_ir(is, c)
    r => cpp_istream_get_ir(r, ee)
    call W('x ints ' // c4(c) // ' ' // c4(ee) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    LL = -1
    r => cpp_istream_get_lr(is, LL)
    call W('x long ' // ci(LL) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    UU = 0
    u = 0
    r => cpp_istream_get_ulr(is, UU)
    call W('x ulong ' // cu(UU) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    r => cpp_istream_get_ur(is, u)
    call W('x uint ' // cu(iand(int(u, 8), 4294967295_8)) // ' ' // St(is) // NL)
    ! ---- fields
    is => cpp_istringstream_new_str(S('a,b,,c'))
    f => cpp_str_new_0()
    ip => is
    do
      r => cpp_streams_getline_istream_str_c(ip, f, int(ichar(','), 1))
      if (.not. cpp_iosbase_ok(r)) exit
      call W('x field [' // T(f) // '] ' // St(is) // NL)
    end do
    call W('x field end ' // St(is) // NL)
    ! ---- ws
    is => cpp_istringstream_new_str(S('   ' // achar(9) // NL // '  tail'))
    r => cpp_istream_manip_ws(is)
    rest => cpp_str_new_0()
    ip => is
    r => cpp_streams_getline_istream_str(ip, rest)
    call W('x ws [' // T(rest) // '] ' // St(is) // NL)
    e => cpp_istringstream_new_str(S('   '))
    r => cpp_istream_manip_ws(e)
    call W('x ws-end ' // St(e) // NL)
    ! ---- unformatted reads and seeks
    is => cpp_istringstream_new_str(S('0123456789'))
    buf => cpp_arr_b_new(16_8)
    r => cpp_istream_read_cptr_l(is, cpp_cptr(b=buf, o=0), 4_8)
    tx = achar(buf%a(0)) // achar(buf%a(1)) // achar(buf%a(2)) // achar(buf%a(3))
    gc = cpp_istream_gcount(is)
    call W('r [' // tx // '] gc ' // ci(gc) // ' ' // St(is))
    tg = cpp_istream_tellg(is)
    call W(' tellg ' // ci(tg) // NL)
    x = cpp_istream_peek(is)
    call W('r peek ' // c4(x))
    x = cpp_istream_get_0(is)
    call W(' get ' // c4(x))
    x = cpp_istream_get_0(is)
    call W(' get ' // c4(x) // ' ' // St(is) // NL)
    r => cpp_istream_ignore(is, 3_8, ichar('8'))
    tg = cpp_istream_tellg(is)
    call W('r ignore tellg ' // ci(tg) // ' ' // St(is) // NL)
    r => cpp_istream_seekg(is, -2_8, cpp_ios_cur)
    tg = cpp_istream_tellg(is)
    x = cpp_istream_get_0(is)
    call W('r seekg cur tellg ' // ci(tg) // ' get ' // c4(x) // NL)
    r => cpp_istream_seekg(is, -3_8, cpp_ios_end)
    tg = cpp_istream_tellg(is)
    x = cpp_istream_get_0(is)
    call W('r seekg end tellg ' // ci(tg) // ' get ' // c4(x) // NL)
    r => cpp_istream_read_cptr_l(is, cpp_cptr(b=buf, o=0), 10_8)
    gc = cpp_istream_gcount(is)
    call W('r short gc ' // ci(gc) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    r => cpp_istream_seekg(is, 0_8, cpp_ios_beg)
    tg = cpp_istream_tellg(is)
    x = cpp_istream_get_0(is)
    call W('r rewind tellg ' // ci(tg) // ' get ' // c4(x) // ' ' // St(is) // NL)
    r => cpp_istream_ignore(is, 100_8, -1)
    call W('r ignore-all ' // St(is) // NL)
    x = cpp_istream_get_0(is)
    call W('r get-end ' // c4(x))
    call W(' ' // St(is) // NL)
    call W('r str [' // T(cpp_istringstream_str_0(is)) // ']' // NL)
    call chk()
    k8 = 0
  end subroutine sec_a_in

  subroutine sec_a_files()
    class(cpp_ofstream), pointer :: of
    class(cpp_ifstream), pointer :: f
    class(cpp_ostream), pointer :: r
    class(cpp_istream), pointer :: ri, ip
    class(cpp_arr_i), pointer :: ia, bb
    class(cpp_arr_d), pointer :: da
    class(cpp_arr_b), pointer :: m
    class(cpp_str), pointer :: tt
    real(8) :: x
    integer(4) :: n
    integer(8) :: k8, gc, tg
    of => cpp_ofstream_new_str_i(S('build/streamcheck.bin'), ior(cpp_ios_trunc, cpp_ios_binary))
    call W('f open ' // B(cpp_ofstream_is_open(of)) // ' ' // St(of) // NL)
    ia => cpp_arr_i_new(3_8)
    ia%a = [1, -2, 300000]
    da => cpp_arr_d_new(2_8)
    da%a = [1.5_8, -0.25_8]
    r => cpp_ostream_write_ptrt_l__i(of, cpp_ptr_i_new_ta_l(ia, 0_8), 12_8)
    r => cpp_ostream_write_ptrt_l__d(of, cpp_ptr_d_new_ta_l(da, 0_8), 16_8)
    r => cpp_ostream_write_cptr_l(of, L('MAGIC'), 5_8)
    k8 = cpp_ostream_tellp(of)
    call W('f tellp ' // ci(k8) // NL)
    r => cpp_ostream_put_d(of, 3.25_8)
    r => cpp_ostream_put_c(of, int(ichar(' '), 1))
    r => cpp_ostream_put_i(of, 42)
    r => cpp_ostream_put_c(of, 10_1)
    k8 = cpp_ostream_tellp(of)
    call W('f tellp ' // ci(k8) // NL)
    call cpp_ofstream_close(of)
    call W('f closed ' // B(cpp_ofstream_is_open(of)) // NL)
    of => cpp_ofstream_new_str_i(S('build/streamcheck.bin'), cpp_ios_app)
    r => cpp_ostream_put_cptr(of, L('tail' // NL))
    call cpp_ofstream_dispose(of)
    call chk()
    f => cpp_ifstream_new_str_i(S('build/streamcheck.bin'), ior(cpp_ios_in, cpp_ios_binary))
    call W('f in open ' // B(cpp_ifstream_is_open(f)) // ' ' // St(f) // NL)
    ia => cpp_arr_i_new(3_8)
    ri => cpp_istream_read_ptrt_l__i(f, cpp_ptr_i_new_ta_l(ia, 0_8), 12_8)
    gc = cpp_istream_gcount(f)
    tg = cpp_istream_tellg(f)
    call W('f ints ' // c4(ia%a(0)) // ' ' // c4(ia%a(1)) // ' ' // c4(ia%a(2)) // ' gc ' // ci(gc) // ' tellg ' // &
        ci(tg) // NL)
    da => cpp_arr_d_new(2_8)
    ri => cpp_istream_read_ptrt_l__d(f, cpp_ptr_d_new_ta_l(da, 0_8), 16_8)
    tg = cpp_istream_tellg(f)
    call W('f dbl ' // G(da%a(0)) // ' ' // G(da%a(1)) // ' tellg ' // ci(tg) // NL)
    m => cpp_arr_b_new(6_8)
    ri => cpp_istream_read_cptr_l(f, cpp_cptr(b=m, o=0), 5_8)
    call W('f magic ' // CS(cpp_cptr(b=m, o=0)) // NL)
    x = 0
    n = 0
    tt => cpp_str_new_0()
    ri => cpp_istream_get_dr(f, x)
    ri => cpp_istream_get_ir(ri, n)
    ri => cpp_istream_get_str(ri, tt)
    call W('f text ' // G(x) // ' ' // c4(n) // ' ' // T(tt) // ' ' // St(f) // NL)
    ri => cpp_istream_seekg(f, -5_8, cpp_ios_end)
    ip => f
    ri => cpp_streams_getline_istream_str(ip, tt)
    call W('f end-5 [' // T(tt) // '] ' // St(f) // NL)
    ri => cpp_streams_getline_istream_str(ip, tt)
    call W('f after ' // St(f) // NL)
    call cpp_iosbase_clear(f, 0)
    ri => cpp_istream_seekg(f, 0_8, cpp_ios_end)
    tg = cpp_istream_tellg(f)
    call W('f size ' // ci(tg) // NL)
    ri => cpp_istream_seekg(f, 4_8, cpp_ios_beg)
    bb => cpp_arr_i_new(1_8)
    ri => cpp_istream_read_ptrt_l__i(f, cpp_ptr_i_new_ta_l(bb, 0_8), 4_8)
    tg = cpp_istream_tellg(f)
    call W('f at4 ' // c4(bb%a(0)) // ' tellg ' // ci(tg) // NL)
    ri => cpp_istream_seekg(f, 1000_8, cpp_ios_beg)
    ri => cpp_istream_read_ptrt_l__i(f, cpp_ptr_i_new_ta_l(bb, 0_8), 4_8)
    gc = cpp_istream_gcount(f)
    call W('f past-end gc ' // ci(gc) // ' ' // St(f) // NL)
    call cpp_ifstream_close(f)
    f => cpp_ifstream_new_str_i(S('build/no-such-file.bin'), cpp_ios_in)
    call W('f missing open ' // B(cpp_ifstream_is_open(f)) // ' ' // St(f) // NL)
    call chk()
  end subroutine sec_a_files

  ! ------------------------------------------------------------------------------------------- section b
  subroutine sec_b()
    class(cpp_istringstream), pointer :: is
    class(cpp_istream), pointer :: r, ip
    class(cpp_str), pointer :: line
    integer(4) :: i, u
    integer(8) :: LL, UU
    real(8) :: d
    real(4) :: fl
    integer(1) :: c
    integer :: k
    is => cpp_istringstream_new_str(S('x' // NL // 'last'))
    line => cpp_str_new_0()
    ip => is
    do k = 0, 2
      r => cpp_streams_getline_istream_str(ip, line)
      call W('b line [' // T(line) // '] ' // St(is) // NL)
    end do
    is => cpp_istringstream_new_str(S('12abc 99999999999999999999 18446744073709551615 -1 1e400 inf 0x1p3 -.5 1e 5'))
    i = -1
    LL = 0
    UU = 0
    u = 0
    d = 0
    r => cpp_istream_get_ir(is, i)
    call W('b 12abc ' // c4(i) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    r => cpp_istream_get_lr(is, LL)
    call W('b long-over ' // ci(LL) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    r => cpp_istream_get_ulr(is, UU)
    call W('b ulong-max ' // cu(UU) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    r => cpp_istream_get_ur(is, u)
    call W('b uint-neg ' // cu(iand(int(u, 8), 4294967295_8)) // ' ' // St(is) // NL)
    call cpp_iosbase_clear(is, 0)
    do k = 0, 4
      r => cpp_istream_get_dr(is, d)
      call W('b dbl ' // G(d) // ' ' // St(is) // NL)
      call cpp_iosbase_clear(is, 0)
    end do
    r => cpp_istream_get_ir(is, i)
    call W('b last ' // c4(i) // ' ' // St(is) // NL)
    i = 77
    r => cpp_istream_get_ir(is, i)
    call W('b after-eof ' // c4(i) // ' ' // St(is) // NL)
    fl = 0
    call cpp_iosbase_clear(is, 0)
    call cpp_istringstream_str_str(is, S(' 0.1 '))
    r => cpp_istream_get_fr(is, fl)
    call W('b float ' // G(real(fl, 8)) // ' ' // St(is) // NL)
    c = 0
    call cpp_istringstream_str_str(is, S('  q'))
    r => cpp_istream_get_cr(is, c)
    call W('b char ' // c4(int(c, 4)) // ' ' // St(is) // NL)
    call chk()
    call sec_b_files()
    call sec_b_strings()
    call sec_b_paths()
    call sec_b_fs()
  end subroutine sec_b

  subroutine sec_b_files()
    class(cpp_ofstream), pointer :: f, gq, h
    class(cpp_ifstream), pointer :: rf, dir
    class(cpp_ostream), pointer :: r
    class(cpp_istream), pointer :: ri, ip
    class(cpp_str), pointer :: tq
    integer(8) :: k8
    f => cpp_ofstream_new_str_i(S('build/streamcheck.bin'), cpp_ios_app)
    k8 = cpp_ostream_tellp(f)
    call W('b app tellp ' // ci(k8) // NL)
    call cpp_ofstream_close(f)
    gq => cpp_ofstream_new_cptr_i(L('build/no-dir/x.txt'), cpp_ios_out)
    call W('b bad-open ' // B(cpp_ofstream_is_open(gq)) // ' ' // St(gq) // NL)
    r => cpp_ostream_put_cptr(gq, L('dropped'))
    h => cpp_ofstream_new_0()
    call W('b unopened ' // B(cpp_ofstream_is_open(h)) // ' ' // St(h) // NL)
    call cpp_ofstream_open_str_i(h, S('build/streamcheck.txt'), cpp_ios_out)
    r => cpp_ostream_put_d(h, 1.25_8)
    r => cpp_ostream_manip_endl(r)
    k8 = cpp_ostream_tellp(h)
    call W('b reopened ' // B(cpp_ofstream_is_open(h)) // ' ' // St(h) // ' tellp ' // ci(k8) // NL)
    r => cpp_ostream_seekp(h, 0_8, cpp_ios_beg)
    r => cpp_ostream_put_cptr(h, L('9'))
    r => cpp_ostream_seekp(h, 0_8, cpp_ios_end)
    r => cpp_ostream_put_cptr(h, L('z'))
    call cpp_ofstream_dispose(h)
    rf => cpp_ifstream_new_fspath_i(fp('build/streamcheck.txt'), cpp_ios_in)
    tq => cpp_str_new_0()
    ip => rf
    ri => cpp_streams_getline_istream_str(ip, tq)
    call W('b reread [' // T(tq) // '] ')
    ri => cpp_streams_getline_istream_str(ip, tq)
    call W('[' // T(tq) // '] ' // St(rf) // NL)
    call cpp_ifstream_close(rf)
    dir => cpp_ifstream_new_str_i(S('build'), cpp_ios_in)
    call W('b dir-open ' // B(cpp_ifstream_is_open(dir)) // ' ' // St(dir) // NL)
    call chk()
  end subroutine sec_b_files

  subroutine sec_b_strings()
    class(cpp_ostringstream), pointer :: o, o2, pq
    class(cpp_istringstream), pointer :: i1, i2
    class(cpp_fstream), pointer :: fs
    class(cpp_ostream), pointer :: r
    class(cpp_istream), pointer :: ri
    class(cpp_str), pointer :: ww
    integer(8) :: k8, pw, pp
    integer(4) :: k4, pf
    o => cpp_ostringstream_new_0()
    call cpp_ostringstream_str_str(o, S('abc'))
    r => cpp_ostream_put_cptr(o, L('XY'))
    k8 = cpp_ostream_tellp(o)
    call W('b str-set [' // T(cpp_ostringstream_str_0(o)) // '] tellp ' // ci(k8) // NL)
    k8 = cpp_iosbase_precision_l(o, 3_8)
    o2 => cpp_ostringstream_new_0()
    call cpp_ostringstream_movefrom(o2, o)
    r => cpp_ostream_put_d(o2, 1.0_8 / 3.0_8)
    r => cpp_ostream_put_cptr(o, L('q'))
    call W('b moved [' // T(cpp_ostringstream_str_0(o2)) // '] [' // T(cpp_ostringstream_str_0(o)) // ']' // NL)
    i1 => cpp_istringstream_new_str(S('one two'))
    ww => cpp_str_new_0()
    ri => cpp_istream_get_str(i1, ww)
    i2 => cpp_istringstream_new_0()
    call cpp_istringstream_movefrom(i2, i1)
    ri => cpp_istream_get_str(i2, ww)
    call W('b imoved [' // T(ww) // '] ' // St(i2) // ' str [' // T(cpp_istringstream_str_0(i2)) // ']' // NL)
    pq => cpp_ostringstream_new_0()
    r => cpp_ostream_put_d(pq, 1.5_8)
    r => cpp_ostream_manip_scientific(pq)
    r => cpp_ostream_put_d(pq, 1.5_8)
    r => cpp_ostream_manip_fixed(pq)
    r => cpp_ostream_put_d(pq, 1.5_8)
    r => cpp_ostream_manip_defaultfloat(pq)
    r => cpp_ostream_put_d(pq, 1.5_8)
    r => cpp_ostream_manip_showpoint(pq)
    r => cpp_ostream_put_d(pq, 1.5_8)
    r => cpp_ostream_manip_noshowpoint(pq)
    r => cpp_ostream_put_d(pq, 1.5_8)
    r => cpp_ostream_put_setw(pq, cpp_streams_setw(6))
    r => cpp_ostream_put_setfill(pq, cpp_streams_setfill(ichar('.')))
    r => cpp_ostream_put_setprec(pq, cpp_streams_setprecision(2))
    r => cpp_ostream_put_d(pq, 3.14159_8)
    r => cpp_ostream_put_setw(pq, cpp_streams_setw(4))
    r => cpp_ostream_put_setfill(pq, cpp_streams_setfill(ichar('#')))
    r => cpp_ostream_put_i(pq, 1)
    k4 = cpp_iosbase_setf_i_i(pq, cpp_ios_left, cpp_ios_adjustfield)
    k8 = cpp_iosbase_width_l(pq, 5_8)
    r => cpp_ostream_put_cptr(pq, L('ab'))
    call cpp_iosbase_unsetf(pq, cpp_ios_left)
    k8 = cpp_iosbase_width_l(pq, 5_8)
    r => cpp_ostream_put_cptr(pq, L('cd'))
    r => cpp_ostream_put_f(pq, 1.0_4 / 3.0_4)
    r => cpp_ostream_put_uc(pq, 66_1)
    r => cpp_ostream_put_u(pq, -2)                ! 2147483647u * 2u
    pw = cpp_iosbase_width_0(pq)
    pf = cpp_iosbase_fill_0(pq)
    pp = cpp_iosbase_precision_0(pq)
    call W('b manip [' // T(cpp_ostringstream_str_0(pq)) // '] w ' // ci(pw) // ' f ' // c4(pf) // ' p ' // ci(pp) // NL)
    fs => cpp_fstream_new_str_i(S('build/streamcheck.fs'), ior(cpp_ios_in, cpp_ios_out))
    r => cpp_ostream_put_cptr(fs, L('fstream'))
    k8 = cpp_ostream_tellp(fs)
    call W('b fstream ' // B(cpp_fstream_is_open(fs)) // ' tellp ' // ci(k8) // NL)
    call cpp_fstream_close(fs)
    call chk()
  end subroutine sec_b_strings

  subroutine sec_b_paths()
    character(len=16) :: paths(22)
    integer :: lens(22), k
    character(len=:), allocatable :: ps, ln
    class(cpp_fspath), pointer :: pa, q, q2, q3, q4, q5, a, bp, c, z
    integer(4) :: cmp
    paths = [character(len=16) :: '', '/', 'a', 'a/', '/a/b.c', 'a/b.tar.gz', '.hidden', 'dir/.hidden', 'a/..', '..', &
        '.', 'a/./b/../c', '/../x', '//a//b//', 'a/b/', 'x.', 'dir.d/file', '/a/b/../../..', '../a/../..', './', &
        'a//b', '/a']
    lens = [0, 1, 1, 2, 6, 10, 7, 11, 4, 2, 1, 10, 5, 8, 4, 2, 10, 13, 10, 2, 4, 2]
    do k = 1, 22
      ps = paths(k)(1:lens(k))
      pa => fp(ps)
      ln = 'p [' // ps // '] fn [' // P(cpp_fspath_filename(pa)) // '] par [' // P(cpp_fspath_parent_path(pa)) // &
          '] stem [' // P(cpp_fspath_stem(pa)) // '] ext [' // P(cpp_fspath_extension(pa)) // '] abs ' // &
          B(cpp_fspath_is_absolute(pa)) // B(cpp_fspath_is_relative(pa)) // ' has ' // B(cpp_fspath_has_filename(pa)) // &
          B(cpp_fspath_has_parent_path(pa)) // B(cpp_fspath_has_extension(pa)) // ' norm [' // &
          P(cpp_fspath_lexically_normal(pa)) // '] empty ' // B(cpp_fspath_empty(pa)) // NL
      call W(ln)
      z => cpp_fspath_new_0()
      ln = '  join [' // P(cpp_fspath_join_fspath(pa, fp('x'))) // '] [' // P(cpp_fspath_join_str(pa, S('/abs'))) // &
          '] [' // P(cpp_fspath_join_fspath(pa, z)) // '] [' // P(cpp_fspath_join_cptr(pa, L('y/z'))) // ']'
      call W(ln)
      q => cpp_fspath_clone(pa)
      q => cpp_fspath_replace_extension_fspath(q, fp('.out'))
      q2 => cpp_fspath_clone(pa)
      q2 => cpp_fspath_replace_extension_str(q2, S('txt'))
      q3 => cpp_fspath_clone(pa)
      z => null()
      q3 => cpp_fspath_replace_extension_fspath(q3, z)
      q4 => cpp_fspath_clone(pa)
      q4 => cpp_fspath_replace_filename(q4, fp('new.inp'))
      q5 => cpp_fspath_clone(pa)
      q5 => cpp_fspath_remove_filename(q5)
      cmp = cpp_fspath_compare(pa, fp('a'))
      ln = ' rext [' // P(q) // '] [' // P(q2) // '] [' // P(q3) // '] rfn [' // P(q4) // '] rmfn [' // P(q5) // &
          '] str [' // T(cpp_fspath_string(pa)) // '] gs [' // T(cpp_fspath_generic_string(pa)) // '] cs [' // &
          CS(cpp_fspath_c_str(pa)) // '] cmp ' // c4(int(sign(1, cmp) * merge(0, 1, cmp == 0), 4)) // NL
      call W(ln)
    end do
    a => fp('x')
    bp => cpp_fspath_new_cptr(L('y'))
    call cpp_fspath_assign_fspath(a, bp)
    call cpp_fspath_assign_str(bp, S('w'))
    c => cpp_fspath_new_0()
    call cpp_fspath_movefrom(c, a)
    call W('p assign [' // P(a) // '] [' // P(bp) // '] [' // P(c) // '] ')
    call cpp_fspath_clear(c)
    call W('[' // P(c) // '] ' // B(cpp_fspath_empty(c)) // NL)
    call chk()
  end subroutine sec_b_paths

  subroutine sec_b_fs()
    class(cpp_fspath), pointer :: dir, file, gp, hp, none
    class(cpp_errorcode), pointer :: ec, e2
    class(cpp_ofstream), pointer :: o
    class(cpp_ostream), pointer :: r
    class(cpp_filestatus), pointer :: s1, s2, s3, s3c
    character(len=24) :: sps(4)
    integer :: k, lens(4), vs(6)
    logical :: l1, l2, l3, l4, l5, l6, l7, l8
    integer(8) :: n1, n2
    character(len=:), allocatable :: x1, x2, x3, sp
    dir => fp('build/fsdir')
    ec => cpp_errorcode_new()
    none => fp('build/fsdir/none')
    l1 = cpp_fs_remove_fspath(fp('build/fsdir/x/y/f.txt'))
    l1 = cpp_fs_remove_fspath(fp('build/fsdir/x/y/g.txt'))
    l1 = cpp_fs_remove_fspath(fp('build/fsdir/x/y/h.txt'))
    l1 = cpp_fs_remove_fspath_errorcode(fp('build/fsdir/x/y'), ec)
    l1 = cpp_fs_create_directories_fspath(fp('build/fsdir/x/y'))
    l2 = cpp_fs_create_directories_fspath_errorcode(fp('build/fsdir/x/y'), ec)
    call W('fs mkdir ' // B(l1) // ' again ' // B(l2) // ' ' // emsg(ec) // NL)
    file => fp('build/fsdir/x/y/f.txt')
    o => cpp_ofstream_new_fspath_i(file, cpp_ios_out)
    r => cpp_ostream_put_cptr(o, L('0123456789'))
    call cpp_ofstream_close(o)
    l1 = cpp_fs_exists_fspath(file)
    l2 = cpp_fs_exists_fspath(dir)
    l3 = cpp_fs_exists_fspath(none)
    l4 = cpp_fs_exists_fspath_errorcode(file, ec)
    l5 = cpp_fs_is_regular_file_fspath(file)
    l6 = cpp_fs_is_regular_file_fspath(dir)
    l7 = cpp_fs_is_directory_fspath(file)
    l8 = cpp_fs_is_directory_fspath(dir)
    call W('fs exists ' // B(l1) // B(l2) // B(l3) // B(l4) // ' reg ' // B(l5) // B(l6) // ' dir ' // B(l7) // B(l8) // NL)
    n1 = cpp_fs_file_size_fspath(file)
    n2 = cpp_fs_file_size_fspath_errorcode(file, ec)
    call W('fs size ' // cu(n1) // ' ec ' // cu(n2) // ' ' // emsg(ec))
    n1 = cpp_fs_file_size_fspath_errorcode(none, ec)
    call W(' missing ' // cu(n1) // ' ' // emsg(ec))
    n1 = cpp_fs_file_size_fspath_errorcode(dir, ec)
    call W(' dir ' // cu(n1) // ' ' // emsg(ec) // NL)
    sps = [character(len=24) :: 'build/fsdir/x/y/f.txt', 'build/fsdir', 'build/fsdir/none', '/dev/null']
    lens = [21, 11, 16, 9]
    do k = 1, 4
      sp = sps(k)(1:lens(k))
      s1 => cpp_fs_status_fspath(fp(sp))
      s2 => cpp_fs_symlink_status_fspath_errorcode(fp(sp), ec)
      s3 => cpp_fs_symlink_status_fspath(fp(sp))
      s3c => cpp_filestatus_clone(s3)
      call W('fs status ' // sp // ' ' // c4(cpp_filestatus_type(s1)) // ' ' // c4(cpp_filestatus_type(s2)) // ' ' // &
          c4(cpp_filestatus_type(s3c)) // ' ' // B(cpp_fs_exists_filestatus(s1)) // &
          B(cpp_fs_is_regular_file_filestatus(s1)) // B(cpp_fs_is_directory_filestatus(s1)) // &
          B(cpp_fs_is_symlink(s1)) // NL)
    end do
    gp => fp('build/fsdir/x/y/g.txt')
    call cpp_fs_create_hard_link(file, gp, ec)
    call W('fs link ' // emsg(ec))
    call cpp_fs_create_hard_link(file, gp, ec)
    call W(' again ' // emsg(ec))
    call cpp_fs_create_hard_link(none, fp('build/fsdir/x/y/n.txt'), ec)
    x1 = emsg(ec)
    n1 = cpp_fs_file_size_fspath(gp)
    call W(' missing ' // x1 // ' size ' // cu(n1) // NL)
    hp => fp('build/fsdir/x/y/h.txt')
    call cpp_fs_rename_fspath_fspath_errorcode(gp, hp, ec)
    x1 = emsg(ec)
    l1 = cpp_fs_exists_fspath(gp)
    l2 = cpp_fs_exists_fspath(hp)
    call W('fs rename ' // x1 // ' ' // B(l1) // B(l2))
    call cpp_fs_rename_fspath_fspath_errorcode(gp, hp, ec)
    call W(' missing ' // emsg(ec))
    call cpp_fs_rename_fspath_fspath(hp, gp)
    call chk()
    l1 = cpp_fs_exists_fspath(gp)
    l2 = cpp_fs_exists_fspath(hp)
    call W(' plain ' // B(l1) // B(l2))
    call cpp_fs_rename_fspath_fspath_errorcode(gp, file, ec)
    x1 = emsg(ec)
    l1 = cpp_fs_exists_fspath(gp)
    l2 = cpp_fs_exists_fspath(file)
    call W(' over ' // x1 // ' ' // B(l1) // B(l2) // NL)
    l1 = cpp_fs_equivalent_fspath_fspath(file, fp('./build/fsdir/x/../x/y//f.txt'))
    l2 = cpp_fs_equivalent_fspath_fspath(file, dir)
    l3 = cpp_fs_equivalent_fspath_fspath_errorcode(file, none, ec)
    x1 = emsg(ec)
    l4 = cpp_fs_equivalent_fspath_fspath_errorcode(file, file, ec)
    x2 = emsg(ec)
    call W('fs equiv ' // B(l1) // B(l2) // B(l3) // ' ' // x1 // ' ' // B(l4) // ' ' // x2 // NL)
    x1 = P(cpp_fs_canonical_fspath_errorcode(none, ec))
    x1 = x1 // '] ' // emsg(ec)
    x2 = P(cpp_fs_weakly_canonical_fspath(fp('build/../build/fsdir/./x/')))
    x3 = P(cpp_fs_weakly_canonical_fspath_errorcode(fp('/a/b/../c/.'), ec))
    x3 = x3 // '] ' // emsg(ec)
    sp = P(cpp_fs_canonical_fspath_errorcode(fp('/x/../y'), ec))
    sp = sp // '] ' // emsg(ec)
    call W('fs canon [' // x1 // ' [' // x2 // '] [' // x3 // ' [' // sp // ' [' // P(cpp_fs_canonical_fspath(dir)) // ']' // NL)
    call W('fs abs [' // P(cpp_fs_absolute(fp('rel/p'))) // '] [' // P(cpp_fs_absolute(fp('/abs/p'))) // '] cwd [' // &
        P(cpp_fs_current_path()) // '] tmp [' // P(cpp_fs_temp_directory_path()) // ']' // NL)
    l1 = cpp_fs_remove_fspath(file)
    l2 = cpp_fs_remove_fspath(file)
    l3 = cpp_fs_remove_fspath(dir)
    call W('fs remove ' // B(l1) // B(l2) // B(l3))
    l1 = cpp_fs_remove_fspath_errorcode(fp('build/fsdir/x'), ec)
    call W(' ec ' // B(l1) // ' ' // emsg(ec))
    l1 = cpp_fs_remove_fspath_errorcode(fp('build/fsdir/x/y'), ec)
    call W(' ' // B(l1) // ' ' // emsg(ec))
    l1 = cpp_fs_remove_fspath_errorcode(fp('build/fsdir/x/y'), ec)
    call W(' ' // B(l1) // ' ' // emsg(ec) // NL)
    e2 => cpp_errorcode_new()
    call W('ec ' // B(e2%v /= 0) // ' ' // c4(e2%v))
    vs = [13, 17, 20, 21, 95, 5]
    do k = 1, 6
      e2%v = vs(k)
      call W(' ' // emsg(e2))
    end do
    e2%v = 0
    call W(' ' // B(e2%v /= 0) // NL)
    call chk()
  end subroutine sec_b_fs

end program streamcheck
