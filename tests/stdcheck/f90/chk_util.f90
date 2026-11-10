! chk_util.f90 - what the Fortran counterparts of tests/stdcheck's checks share: a line buffer written to stdout,
! unsigned decimal output, the xorshift generator of the C++ checks, Str from and to Fortran text, boxes, and
! array / iterator makers. Uses only the public parts of the runtime.
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
module chk_util
  use cpp_runtime
  implicit none
  character(len=:), allocatable :: chk_line
  integer :: chk_n = 0
  integer(8) :: chk_s = 0_8
contains
  subroutine put(s)
    character(len=*), intent(in) :: s
    character(len=:), allocatable :: t
    integer :: cap
    if (.not. allocated(chk_line)) then
      allocate(character(len=4096) :: chk_line)
      chk_n = 0
    end if
    cap = len(chk_line)
    if (chk_n + len(s) > cap) then
      allocate(character(len=max(2 * cap, chk_n + len(s))) :: t)
      t(1:chk_n) = chk_line(1:chk_n)
      call move_alloc(t, chk_line)
    end if
    chk_line(chk_n + 1:chk_n + len(s)) = s
    chk_n = chk_n + len(s)
  end subroutine

  ! end the line: write it with its newline
  subroutine nl()
    if (.not. allocated(chk_line)) call put('')
    write(*, '(a)') chk_line(1:chk_n)
    chk_n = 0
  end subroutine

  function istr(x) result(s)
    integer(8), intent(in) :: x
    character(len=:), allocatable :: s
    character(len=24) :: b
    write(b, '(i0)') x
    s = trim(b)
  end function

  function i4str(x) result(s)
    integer(4), intent(in) :: x
    character(len=:), allocatable :: s
    s = istr(int(x, 8))
  end function

  ! a ulong's decimal digits
  recursive function ustr(x) result(s)
    integer(8), intent(in) :: x
    character(len=:), allocatable :: s
    integer(8) :: q, r
    if (x >= 0_8) then
      s = istr(x)
    else
      q = ishft(x, -1) / 5_8
      r = x - q * 10_8
      s = istr(q) // achar(48 + int(r))
    end if
  end function

  ! x mod n for x a ulong
  function umod(x, n) result(r)
    integer(8), intent(in) :: x, n
    integer(8) :: r
    if (x >= 0_8) then
      r = mod(x, n)
    else
      r = mod(ishft(x, -1), n)
      r = mod(2_8 * r + iand(x, 1_8), n)
    end if
  end function

  ! s ^= s << 13; s ^= s >> 7; s ^= s << 17
  function rnd() result(r)
    integer(8) :: r
    chk_s = ieor(chk_s, ishft(chk_s, 13))
    chk_s = ieor(chk_s, ishft(chk_s, -7))
    chk_s = ieor(chk_s, ishft(chk_s, 17))
    r = chk_s
  end function

  ! a new Str holding the text t
  function mkstr(t) result(s)
    character(len=*), intent(in) :: t
    class(cpp_str), pointer :: s
    integer :: i
    allocate(cpp_str :: s)
    s%b => cpp_arr_b_new(int(len(t) + 1, 8))
    do i = 1, len(t)
      s%b%a(i - 1) = int(iachar(t(i:i)), 1)
    end do
    s%b%a(len(t)) = 0_1
    s%n = len(t)
  end function

  function strtext(x) result(t)
    class(*), pointer, intent(in) :: x
    character(len=:), allocatable :: t
    integer :: i
    t = ''
    select type (s => x)
    class is (cpp_str)
      t = repeat(' ', s%n)
      do i = 1, s%n
        t(i:i) = achar(iand(int(s%b%a(i - 1)), 255))
      end do
    end select
  end function

  function boxi(v) result(p)
    integer(4), intent(in) :: v
    class(*), pointer :: p
    allocate(p, source=v)
  end function

  function boxl(v) result(p)
    integer(8), intent(in) :: v
    class(*), pointer :: p
    allocate(p, source=v)
  end function

  function geti(p) result(v)
    class(*), pointer, intent(in) :: p
    integer(4) :: v
    v = -huge(0)
    select type (x => p)
    type is (integer(4))
      v = x
    end select
  end function

  function getl(p) result(v)
    class(*), pointer, intent(in) :: p
    integer(8) :: v
    v = -huge(0_8)
    select type (x => p)
    type is (integer(8))
      v = x
    end select
  end function

  function getz(p) result(v)
    class(*), pointer, intent(in) :: p
    logical :: v
    v = .false.
    select type (x => p)
    type is (logical)
      v = x
    end select
  end function

  function getmit(p) result(v)
    class(*), pointer, intent(in) :: p
    type(cpp_mit) :: v
    v = cpp_mit()
    select type (x => p)
    type is (cpp_mit)
      v = x
    end select
  end function

  ! the pair a MIt designates (a map's element)
  function mitpair(it) result(p)
    type(cpp_mit), intent(in) :: it
    class(cpp_pair), pointer :: p
    class(*), pointer :: x
    p => null()
    x => cpp_mit_deref(it)
    select type (x)
    class is (cpp_pair)
      p => x
    end select
  end function

  ! makers for the containers' type arguments
  recursive function mk_i() result(zres)
    class(*), pointer :: zres
    allocate(zres, source=0)
  end function
  recursive function mk_l() result(zres)
    class(*), pointer :: zres
    allocate(zres, source=0_8)
  end function
  recursive function mk_str() result(zres)
    class(*), pointer :: zres
    zres => mkstr('')
  end function
  recursive function mk_none() result(zres)
    class(*), pointer :: zres
    zres => null()
  end function
end module chk_util
