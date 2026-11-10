! crtcheck.f90 - the Fortran side of tests/crtcheck (as gocheck/, javacheck/, pycheck/ and tscheck/ are the Go, Java,
! Python and TypeScript sides):   crtcheck libm N | printf | scanf | misc | units
! reads what the C program of the same name wrote on stdin and reports where part C of the Fortran runtime (module
! cpp_rt_c, f90/runtime/cpp_rt_c*.f90) differs from glibc. The case lines and their checks are pycheck's, one for
! one, with the targets the engine passes (RefI / RefL / RefF / RefD setters, CPtr buffers, R.Slot). Two kinds of
! case are left out and counted as such: from_chars of a long (fcl; the engine's manifest has no such member) and
! the five ctype predicates part C does not have (isupper islower ispunct isprint isxdigit, part D's or unused).
! sqrt in the libm table is gfortran's intrinsic (the engine's), not a part-C member.
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
module ckutil
  use cpp_rt_a
  use cpp_rt_c
  implicit none
  character(len=1), parameter :: TAB = achar(9)
  integer(4) :: ints(8)
  integer(8) :: longs(8)
  real(4) :: floats(8)
  real(8) :: doubles(8)
  integer :: nfail = 0, nunit = 0

  type, extends(cpp_action_i) :: set_i
    integer :: k = 0, mode = 0          ! mode 1: a char (signed)
  contains
    procedure :: invoke => set_i_invoke
  end type
  type, extends(cpp_action_l) :: set_l
    integer :: k = 0, mode = 0          ! mode 1: an unsigned int kept as its bits in an int
  contains
    procedure :: invoke => set_l_invoke
  end type
  type, extends(cpp_action_f) :: set_f
    integer :: k = 0
  contains
    procedure :: invoke => set_f_invoke
  end type
  type, extends(cpp_action_d) :: set_d
    integer :: k = 0
  contains
    procedure :: invoke => set_d_invoke
  end type
  ! an action that counts its calls (call_once, Defer)
  type, extends(cpp_action) :: counter
    integer :: n = 0
  contains
    procedure :: invoke => counter_invoke
  end type

contains

  recursive subroutine set_i_invoke(self, a1)
    class(set_i), intent(inout), target :: self
    integer(4), value :: a1
    if (self%mode == 1) then
      ints(self%k) = iand(a1 + 128, 255) - 128
    else
      ints(self%k) = a1
    end if
  end subroutine
  recursive subroutine set_l_invoke(self, a1)
    class(set_l), intent(inout), target :: self
    integer(8), value :: a1
    integer(8) :: w
    if (self%mode == 1) then
      w = iand(a1, 4294967295_8)
      if (w > 2147483647_8) w = w - 4294967296_8
      ints(self%k) = int(w, 4)
    else
      longs(self%k) = a1
    end if
  end subroutine
  recursive subroutine set_f_invoke(self, a1)
    class(set_f), intent(inout), target :: self
    real(4), value :: a1
    floats(self%k) = a1
  end subroutine
  recursive subroutine set_d_invoke(self, a1)
    class(set_d), intent(inout), target :: self
    real(8), value :: a1
    doubles(self%k) = a1
  end subroutine
  recursive subroutine counter_invoke(self)
    class(counter), intent(inout), target :: self
    self%n = self%n + 1
  end subroutine

  ! ------------------------------------------------------------------------------------------- text helpers
  !> one line of stdin without its newline; false at the end
  subroutine getline(line, ok)
    character(len=:), allocatable, intent(out) :: line
    logical, intent(out) :: ok
    character(len=8192) :: buf
    integer :: n, ios
    line = ''
    ok = .false.
    do
      read(*, '(a)', advance='no', size=n, iostat=ios) buf
      line = line // buf(1:n)
      if (is_iostat_eor(ios)) then
        ok = .true.
        return
      else if (is_iostat_end(ios)) then
        ok = len(line) > 0
        return
      else if (ios /= 0) then
        return
      end if
    end do
  end subroutine getline

  !> the k-th tab-separated field (1-based; '' past the last)
  function field(line, k) result(f)
    character(len=*), intent(in) :: line
    integer, intent(in) :: k
    character(len=:), allocatable :: f
    integer :: i, j, n
    i = 1
    do n = 1, k - 1
      j = index(line(i:), TAB)
      if (j == 0) then
        f = ''
        return
      end if
      i = i + j
    end do
    j = index(line(i:), TAB)
    if (j == 0) then
      f = line(i:)
    else
      f = line(i:i+j-2)
    end if
  end function field

  pure integer function hexd(c)
    character, intent(in) :: c
    hexd = index('0123456789abcdef', c) - 1
    if (hexd < 0) hexd = index('0123456789ABCDEF', c) - 1
  end function hexd

  !> the bytes of a hex string ('-' for none)
  function unhex(s) result(b)
    character(len=*), intent(in) :: s
    integer(1), allocatable :: b(:)
    integer :: i, n, v
    if (s == '-') then
      allocate(b(0))
      return
    end if
    n = len(s) / 2
    allocate(b(n))
    do i = 1, n
      v = hexd(s(2*i-1:2*i-1)) * 16 + hexd(s(2*i:2*i))
      if (v > 127) v = v - 256
      b(i) = int(v, 1)
    end do
  end function unhex

  !> a hex string as the bits of an integer(8)
  function hexbits(s) result(v)
    character(len=*), intent(in) :: s
    integer(8) :: v
    integer :: i
    v = 0
    do i = 1, len(s)
      v = ior(ishft(v, 4), int(hexd(s(i:i)), 8))
    end do
  end function hexbits

  !> lower-case hex of the bytes ('-' for none)
  function hexof(b) result(s)
    integer(1), intent(in) :: b(:)
    character(len=:), allocatable :: s
    character(len=16), parameter :: H = '0123456789abcdef'
    integer :: i, v
    if (size(b) == 0) then
      s = '-'
      return
    end if
    allocate(character(len=2*size(b)) :: s)
    do i = 1, size(b)
      v = iand(int(b(i)), 255)
      s(2*i-1:2*i-1) = H(v/16+1:v/16+1)
      s(2*i:2*i) = H(mod(v, 16)+1:mod(v, 16)+1)
    end do
  end function hexof

  !> lower-case hex of the low n nibbles of v
  function hexn(v, n) result(s)
    integer(8), intent(in) :: v
    integer, intent(in) :: n
    character(len=n) :: s
    character(len=16), parameter :: H = '0123456789abcdef'
    integer :: i, d
    do i = 1, n
      d = int(iand(ishft(v, -4*(n - i)), 15_8))
      s(i:i) = H(d+1:d+1)
    end do
  end function hexn

  function i0(v) result(s)
    integer(8), intent(in) :: v
    character(len=:), allocatable :: s
    character(len=24) :: t
    write(t, '(i0)') v
    s = trim(t)
  end function i0

  !> a new CPtr holding the bytes and a NUL
  function lit(b) result(p)
    integer(1), intent(in) :: b(:)
    type(cpp_cptr) :: p
    class(cpp_arr_b), pointer :: a
    allocate(a)
    allocate(a%a(0:size(b)))
    if (size(b) > 0) a%a(0:size(b)-1) = b
    a%a(size(b)) = 0
    p%b => a
    p%o = 0
  end function lit

  !> the bytes of a C string (up to its NUL)
  function cbytes(p) result(b)
    type(cpp_cptr), intent(in) :: p
    integer(1), allocatable :: b(:)
    integer(8) :: n
    n = cpp_r_strlen(p)
    allocate(b(n))
    if (n > 0) b = p%b%a(p%o:p%o+n-1)
  end function cbytes

  function cstr(p) result(s)
    type(cpp_cptr), intent(in) :: p
    character(len=:), allocatable :: s
    integer(1), allocatable :: b(:)
    b = cbytes(p)
    allocate(character(len=size(b)) :: s)
    if (size(b) > 0) s = transfer(b, s)
  end function cstr

  subroutine report(what, label, want, got, nbad)
    character(len=*), intent(in) :: what, label, want, got
    integer, intent(inout) :: nbad
    nbad = nbad + 1
    if (nbad <= 15) then
      write(*, '(a)') 'MISMATCH ' // what // ' ' // label
      write(*, '(a)') '  glibc:   ' // want
      write(*, '(a)') '  Fortran: ' // got
    end if
  end subroutine report

  subroutine check(name, ok)
    character(len=*), intent(in) :: name
    logical, intent(in) :: ok
    nunit = nunit + 1
    if (.not. ok) then
      nfail = nfail + 1
      write(*, '(a)') 'FAIL ' // name
    end if
  end subroutine check

  ! ------------------------------------------------------------------------------------------- printf
  ! hex(format) types args size ret hex(output)
  subroutine printf_cases()
    character(len=:), allocatable :: line, types, vals, want, got, label
    type(cpp_slot) :: av(3)
    type(cpp_cptr) :: fmt, buf, sv
    logical :: ok
    integer :: n, nbad, k, i, c, sz
    integer(4) :: r, iv
    integer(8) :: lv
    real(8) :: dv
    n = 0
    nbad = 0
    buf = cpp_r_buf_i(1024)
    do
      call getline(line, ok)
      if (.not. ok) exit
      n = n + 1
      fmt = lit(unhex(field(line, 1)))
      types = field(line, 2)
      vals = field(line, 3) // ','
      do k = 1, 3
        av(k)%p => null()
      end do
      i = 1
      do k = 1, len(types)
        c = index(vals(i:), ',')
        select case (types(k:k))
        case ('d')
          dv = transfer(hexbits(vals(i:i+c-2)), 1.0_8)
          allocate(av(k)%p, source=dv)
        case ('i')
          lv = hexbits(vals(i:i+c-2))
          if (lv > 2147483647_8) lv = lv - 4294967296_8
          iv = int(lv, 4)
          allocate(av(k)%p, source=iv)
        case ('l')
          lv = hexbits(vals(i:i+c-2))
          allocate(av(k)%p, source=lv)
        case ('s')
          sv = lit(unhex(vals(i:i+c-2)))
          allocate(av(k)%p, source=sv)
        case ('S')
          sv = cpp_cptr()
          allocate(av(k)%p, source=sv)
        end select
        i = i + c
      end do
      label = field(line, 4)
      read(label, *) sz
      buf%b%a = 85
      buf%b%a(0) = 0
      r = cpp_r_snprintf_cptr_ul_cptr_oa(buf, int(sz, 8), fmt, av(1)%p, av(2)%p, av(3)%p)
      if (sz > 0) then
        got = i0(int(r, 8)) // TAB // hexof(cbytes(buf))
      else
        got = i0(int(r, 8)) // TAB // '-'
      end if
      want = field(line, 5) // TAB // field(line, 6)
      if (want /= got) then
        label = cstr(fmt)
        call report('printf', label, want, got, nbad)
      end if
      do k = 1, 3
        if (associated(av(k)%p)) deallocate(av(k)%p)
      end do
    end do
    write(*, '(a,i0,a,i0)') 'printf cases: ', n, ', mismatches: ', nbad
  end subroutine printf_cases

  ! ------------------------------------------------------------------------------------------- scanf
  ! hex(format) hex(input) types ret values
  subroutine scanf_cases()
    character(len=:), allocatable :: line, types, want, got, label
    type(cpp_slot) :: tg(8)
    type(cpp_cptr) :: fmt, inp, bufs(8)
    class(set_i), pointer :: si
    class(set_l), pointer :: sl
    class(set_f), pointer :: sf
    class(set_d), pointer :: sd
    class(cpp_action_i), pointer :: ai
    class(cpp_action_l), pointer :: al
    class(cpp_action_f), pointer :: af
    class(cpp_action_d), pointer :: ad
    logical :: ok
    integer :: n, nbad, k
    integer(4) :: r
    n = 0
    nbad = 0
    do
      call getline(line, ok)
      if (.not. ok) exit
      n = n + 1
      fmt = lit(unhex(field(line, 1)))
      inp = lit(unhex(field(line, 2)))
      types = field(line, 3)
      ints = 1431655765
      longs = 6148914691236517205_8
      floats = transfer(1431655765, 1.0_4)
      doubles = transfer(6148914691236517205_8, 1.0_8)
      do k = 1, 8
        tg(k)%p => null()
      end do
      do k = 1, len(types)
        select case (types(k:k))
        case ('i', 'n', 'k')
          allocate(si)
          si%k = k
          if (types(k:k) == 'k') then
            si%mode = 1
            ints(k) = 85
          end if
          ai => si
          tg(k)%p => cpp_r_refi(ai)
        case ('u', 'l')
          allocate(sl)
          sl%k = k
          if (types(k:k) == 'u') sl%mode = 1
          al => sl
          tg(k)%p => cpp_r_refl(al)
        case ('f')
          allocate(sf)
          sf%k = k
          af => sf
          tg(k)%p => cpp_r_reff(af)
        case ('d')
          allocate(sd)
          sd%k = k
          ad => sd
          tg(k)%p => cpp_r_refd(ad)
        case ('s', 'c')
          bufs(k) = cpp_r_buf_i(64)
          bufs(k)%b%a = 85
          allocate(tg(k)%p, source=bufs(k))
        end select
      end do
      r = cpp_r_sscanf(inp, fmt, tg(1)%p, tg(2)%p, tg(3)%p, tg(4)%p, tg(5)%p, tg(6)%p, tg(7)%p, tg(8)%p)
      got = i0(int(r, 8)) // TAB
      do k = 1, len(types)
        select case (types(k:k))
        case ('i', 'n', 'u', 'k')
          got = got // i0(int(ints(k), 8))
        case ('l')
          got = got // i0(longs(k))
        case ('f')
          got = got // hexn(int(transfer(floats(k), 0_4), 8), 8)
        case ('d')
          got = got // hexn(transfer(doubles(k), 0_8), 16)
        case default
          got = got // hexof(bufs(k)%b%a(0:11))
        end select
        got = got // ' '
      end do
      want = field(line, 4) // TAB // field(line, 5)
      if (want /= got) then
        label = cstr(fmt) // ' <- "' // cstr(inp) // '"'
        call report('scanf', label, want, got, nbad)
      end if
    end do
    write(*, '(a,i0,a,i0)') 'scanf cases: ', n, ', mismatches: ', nbad
  end subroutine scanf_cases

  ! ------------------------------------------------------------------------------------------- misc
  ! kind hex(input) result
  subroutine misc_cases()
    character(len=:), allocatable :: line, kind, want, got, raw
    integer(1), allocatable :: rb(:)
    type(cpp_cptr) :: s, e, e2, ct
    type(cpp_fromcharsresult) :: fr
    class(cpp_arr_l), pointer :: tarr
    logical :: ok
    integer :: n, nbad, nskip, c
    integer(4) :: iv
    integer(8) :: lv, t
    real(8) :: dv
    n = 0
    nbad = 0
    nskip = 0
    do
      call getline(line, ok)
      if (.not. ok) exit
      kind = field(line, 1)
      rb = unhex(field(line, 2))
      s = lit(rb)
      e = s
      e%o = s%o + size(rb)
      want = field(line, 3)
      select case (kind)
      case ('fci')
        iv = 1431655765
        fr = cpp_std_from_chars_cptr_cptr_ir(s, e, iv)
        got = i0(int(fr%ec, 8)) // ' ' // i0(int(fr%ptr%o - s%o, 8)) // ' ' // i0(int(iv, 8))
      case ('fcu')
        iv = 1431655765
        fr = cpp_std_from_chars_cptr_cptr_ur(s, e, iv)
        got = i0(int(fr%ec, 8)) // ' ' // i0(int(fr%ptr%o - s%o, 8)) // ' ' // i0(iand(int(iv, 8), 4294967295_8))
      case ('fcl')
        nskip = nskip + 1
        cycle
      case ('fcd')
        dv = 0.25_8
        fr = cpp_std_from_chars_cptr_cptr_dr(s, e, dv)
        got = i0(int(fr%ec, 8)) // ' ' // i0(int(fr%ptr%o - s%o, 8)) // ' ' // hexn(transfer(dv, 0_8), 16)
      case ('strtol')
        e2 = s
        lv = cpp_r_strtol(s, e2, 10)
        got = i0(lv) // ' ' // i0(int(e2%o - s%o, 8))
      case ('atoi')
        got = i0(int(cpp_r_atoi(s), 8))
      case ('atol')
        got = i0(cpp_r_atol(s))
      case ('strtod')
        e2 = s
        dv = cpp_r_strtod_cptr_cptrr(s, e2)
        got = hexn(transfer(dv, 0_8), 16) // ' ' // i0(int(e2%o - s%o, 8))
      case ('ctime')
        raw = cstr(s)
        read(raw, *) t
        allocate(tarr)
        allocate(tarr%a(0:0))
        tarr%a(0) = t
        ct = cpp_r_ctime_ptrl(cpp_ptr_l_new_ta_l(tarr, 0_8))
        got = hexof(cbytes(ct))
      case ('nprocs')
        got = i0(int(cpp_sys_hardware_concurrency(), 8))
      case ('ctype')
        raw = cstr(s)
        read(raw, *) c
        ! only isdigit isalpha isalnum isspace (the first four flags), toupper and tolower are part C's
        want = want(1:4) // want(10:)
        got = i0(int(cpp_r_isdigit(c), 8)) // i0(int(cpp_r_isalpha(c), 8)) // i0(int(cpp_r_isalnum(c), 8)) // &
              i0(int(cpp_r_isspace(c), 8)) // ' ' // i0(int(cpp_r_toupper(c), 8)) // ' ' // i0(int(cpp_r_tolower(c), 8))
      case default
        nskip = nskip + 1
        cycle
      end select
      n = n + 1
      if (want /= got) call report('misc', kind // ' "' // cstr(s) // '"', want, got, nbad)
    end do
    write(*, '(a,i0,a,i0,a,i0,a)') 'misc cases: ', n, ', mismatches: ', nbad, ' (', nskip, &
         ' fcl cases skipped: no from_chars of a long in the manifest)'
  end subroutine misc_cases

  ! ------------------------------------------------------------------------------------------- libm
  !> the 27 functions of libm.c over N random inputs and the 24 x 24 special values each, bit for bit
  subroutine libm_cases(nn)
    integer, intent(in) :: nn
    character(len=10), parameter :: FN(27) = [character(len=10) :: 'atan2', 'cbrt', 'cosh', 'sinh', 'tanh', 'log2', &
         'expm1', 'log1p', 'hypot', 'fmod', 'remainder', 'round', 'nextafter', 'exp', 'log', 'pow', 'sin', 'cos', &
         'tan', 'asin', 'acos', 'atan', 'log10', 'floor', 'ceil', 'trunc', 'sqrt']
    integer, parameter :: NSPEC = 24
    integer :: u, per, f, k, bad, nan_sign, sbad, total, ios
    real(8), allocatable :: rec(:, :)
    real(8) :: x, y, g, w
    integer(8) :: gb, wb, maxu, du
    logical :: nannum
    character(len=:), allocatable :: first
    character(len=40) :: t1, t2, t3, t4
    open(newunit=u, file='/dev/stdin', access='stream', form='unformatted', status='old', iostat=ios)
    if (ios /= 0) stop 'crtcheck: cannot read /dev/stdin'
    per = nn + NSPEC * NSPEC
    allocate(rec(3, per))
    write(*, '(a10,1x,a8,1x,a10,1x,a9,1x,a20,1x,a10,2x,a)') 'function', 'cases', 'mismatch', 'NaN-sign', 'max ulp', &
         'special', 'first mismatch (x, y: glibc vs Fortran)'
    total = 0
    do f = 1, 27
      read(u, iostat=ios) rec
      if (ios /= 0) stop 'crtcheck: short input'
      bad = 0
      nan_sign = 0
      sbad = 0
      maxu = 0
      nannum = .false.
      first = ''
      do k = 1, per
        x = rec(1, k)
        y = rec(2, k)
        select case (f)
        case (1)
          g = cpp_math_atan2_dd(x, y)
        case (2)
          g = cpp_math_cbrt_d(x)
        case (3)
          g = cpp_math_cosh_d(x)
        case (4)
          g = cpp_math_sinh_d(x)
        case (5)
          g = cpp_math_tanh_d(x)
        case (6)
          g = cpp_math_log2_d(x)
        case (7)
          g = cpp_libm_expm1(x)
        case (8)
          g = cpp_libm_log1p(x)
        case (9)
          g = cpp_libm_hypot(x, y)
        case (10)
          g = cpp_libm_fmod(x, y)
        case (11)
          g = cpp_libm_remainder(x, y)
        case (12)
          g = cpp_libm_round(x)
        case (13)
          g = cpp_libm_nextafter(x, y)
        case (14)
          g = cpp_math_exp_d(x)
        case (15)
          g = cpp_math_log_d(x)
        case (16)
          g = cpp_math_pow_dd(x, y)
        case (17)
          g = cpp_math_sin_d(x)
        case (18)
          g = cpp_math_cos_d(x)
        case (19)
          g = cpp_math_tan_d(x)
        case (20)
          g = cpp_math_asin_d(x)
        case (21)
          g = cpp_math_acos_d(x)
        case (22)
          g = cpp_math_atan_d(x)
        case (23)
          g = cpp_math_log10_d(x)
        case (24)
          g = cpp_math_floor_d(x)
        case (25)
          g = cpp_math_ceiling_d(x)
        case (26)
          g = cpp_math_truncate_d(x)
        case (27)
          g = sqrt(x)
        end select
        gb = transfer(g, 0_8)
        wb = transfer(rec(3, k), 0_8)
        if (gb == wb) cycle
        bad = bad + 1
        if (k > nn) sbad = sbad + 1
        w = rec(3, k)
        if (w /= w .and. g /= g) then
          nan_sign = nan_sign + 1
        else if (w == w .and. g == g) then
          du = abs(ord(gb) - ord(wb))
          maxu = max(maxu, du)
        else
          nannum = .true.
        end if
        if (len(first) == 0) then
          write(t1, '(es24.16)') x
          write(t2, '(es24.16)') y
          t3 = hexn(wb, 16)
          t4 = hexn(gb, 16)
          first = trim(adjustl(t1)) // ', ' // trim(adjustl(t2)) // ': ' // trim(t3) // ' vs ' // trim(t4)
        end if
      end do
      total = total + bad
      if (nannum) then
        write(*, '(a10,1x,i8,1x,i10,1x,i9,1x,a20,1x,i10,2x,a)') FN(f), per, bad, nan_sign, 'NaN/num', sbad, first
      else
        write(*, '(a10,1x,i8,1x,i10,1x,i9,1x,i20,1x,i10,2x,a)') FN(f), per, bad, nan_sign, maxu, sbad, first
      end if
    end do
    write(*, '(a,i0)') 'total mismatches: ', total
    close(u)
  contains
    pure integer(8) function ord(b)
      integer(8), intent(in) :: b
      if (b < 0) then
        ord = -huge(b) - 1 - b
      else
        ord = b
      end if
    end function ord
  end subroutine libm_cases

end module ckutil
