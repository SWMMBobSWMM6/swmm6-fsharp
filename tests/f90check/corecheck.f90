! corecheck.f90 - part A of the Fortran runtime (f90/runtime/cpp_rt_a.f90, module cpp_rt_a) against the C# runtime it
! is ported from: the same operations as corecheck.cs, line for line, through the procedures the engine calls (and the
! components it reads), with the element makers and copy modes the translator passes. run.sh requires identical
! output. Doubles print as their bit patterns, booleans as 0/1, strings in brackets.
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
module chk
  use cpp_rt_a
  implicit none
  type, extends(cpp_object) :: holder
    real(8) :: x = 0.0_8
    integer(4) :: k = 0
    integer(8) :: kl = 0_8
    type(cpp_slot) :: s
  end type holder
  ! the C# lambdas (o) => ref ((Holder)o).x etc.: one derived type per lambda
  type, extends(cpp_refat_d) :: at_x
  contains
    procedure :: invoke => at_x_invoke
  end type at_x
  type, extends(cpp_refat_d) :: at_x2
  contains
    procedure :: invoke => at_x2_invoke
  end type at_x2
  type, extends(cpp_refat_i) :: at_k
  contains
    procedure :: invoke => at_k_invoke
  end type at_k
  type, extends(cpp_refat_l) :: at_kl
  contains
    procedure :: invoke => at_kl_invoke
  end type at_kl
  type, extends(cpp_refat_o) :: at_s
  contains
    procedure :: invoke => at_s_invoke
  end type at_s
  ! () => new Str(), () => new double[3], () => new double[4], () => S("s")
  type, extends(cpp_func_o) :: fn_str
  contains
    procedure :: invoke => fn_str_invoke
  end type fn_str
  type, extends(cpp_func_o) :: fn_arr3
  contains
    procedure :: invoke => fn_arr3_invoke
  end type fn_arr3
  type, extends(cpp_func_o) :: fn_arr4
  contains
    procedure :: invoke => fn_arr4_invoke
  end type fn_arr4
  type, extends(cpp_func_o) :: fn_s
  contains
    procedure :: invoke => fn_s_invoke
  end type fn_s
  character(len=:), allocatable :: outbuf
  integer(8) :: rs = 12345_8
contains
  recursive function at_x_invoke(self, a1) result(zres)
    class(at_x), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    real(8), pointer :: zres
    zres => null()
    select type (a1)
    class is (holder)
      zres => a1%x
    end select
  end function at_x_invoke
  recursive function at_x2_invoke(self, a1) result(zres)
    class(at_x2), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    real(8), pointer :: zres
    zres => null()
    select type (a1)
    class is (holder)
      zres => a1%x
    end select
  end function at_x2_invoke
  recursive function at_k_invoke(self, a1) result(zres)
    class(at_k), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    integer(4), pointer :: zres
    zres => null()
    select type (a1)
    class is (holder)
      zres => a1%k
    end select
  end function at_k_invoke
  recursive function at_kl_invoke(self, a1) result(zres)
    class(at_kl), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    integer(8), pointer :: zres
    zres => null()
    select type (a1)
    class is (holder)
      zres => a1%kl
    end select
  end function at_kl_invoke
  recursive function at_s_invoke(self, a1) result(zres)
    class(at_s), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    type(cpp_slot), pointer :: zres
    zres => null()
    select type (a1)
    class is (holder)
      zres => a1%s
    end select
  end function at_s_invoke
  recursive function fn_str_invoke(self) result(zres)
    class(fn_str), intent(inout), target :: self
    class(*), pointer :: zres
    zres => cpp_str_new_0()
  end function fn_str_invoke
  recursive function fn_arr3_invoke(self) result(zres)
    class(fn_arr3), intent(inout), target :: self
    class(*), pointer :: zres
    zres => cpp_arr_d_new(3_8)
  end function fn_arr3_invoke
  recursive function fn_arr4_invoke(self) result(zres)
    class(fn_arr4), intent(inout), target :: self
    class(*), pointer :: zres
    zres => cpp_arr_d_new(4_8)
  end function fn_arr4_invoke
  recursive function fn_s_invoke(self) result(zres)
    class(fn_s), intent(inout), target :: self
    class(*), pointer :: zres
    zres => S('s')
  end function fn_s_invoke

  ! makers (Elem<T>.Make) for the element types of the checks
  recursive function mk_cptr() result(zres)
    class(*), pointer :: zres
    allocate(zres, source=cpp_cptr())
  end function mk_cptr
  recursive function mk_vecd() result(zres)
    class(*), pointer :: zres
    zres => cpp_vec_d_new_0()
  end function mk_vecd
  recursive function mk_pair_id() result(zres)
    class(*), pointer :: zres
    class(*), pointer :: a, b
    allocate(a, source=0)
    allocate(b, source=0.0_8)
    zres => cpp_pair_new_a_b(a, b, cpp_a_mk_i, 0, cpp_a_mk_d, 0)
  end function mk_pair_id

  ! ---- output
  subroutine emit(tag, v)
    character(len=*), intent(in) :: tag, v
    write(*, '(a)') tag // '=' // v
    flush(6)
  end subroutine emit
  function d(x) result(s)
    real(8), intent(in) :: x
    character(len=:), allocatable :: s
    s = hx(transfer(x, 0_8))
  end function d
  function df(x) result(s)
    real(4), intent(in) :: x
    character(len=:), allocatable :: s
    s = d(real(x, 8))
  end function df
  function hx(v) result(s)
    integer(8), intent(in) :: v
    character(len=:), allocatable :: s
    character(len=16), parameter :: digs = '0123456789abcdef'
    integer :: k, nib
    logical :: started
    s = ''
    started = .false.
    do k = 15, 0, -1
      nib = int(iand(ishft(v, -4 * k), 15_8))
      if (nib /= 0) started = .true.
      if (started) s = s // digs(nib + 1:nib + 1)
    end do
    if (.not. started) s = '0'
  end function hx
  function b(x) result(s)
    logical, intent(in) :: x
    character(len=1) :: s
    s = merge('1', '0', x)
  end function b
  function n8(v) result(s)
    integer(8), intent(in) :: v
    character(len=:), allocatable :: s
    s = cpp_a_dec8(v)
  end function n8
  function n4(v) result(s)
    integer(4), intent(in) :: v
    character(len=:), allocatable :: s
    s = cpp_a_dec8(int(v, 8))
  end function n4
  function u8(v) result(s)
    integer(8), intent(in) :: v
    character(len=:), allocatable :: s
    s = cpp_a_udec8(v)
  end function u8
  function ss(x) result(s)
    class(cpp_str), pointer, intent(in) :: x
    character(len=:), allocatable :: s
    s = '[' // cpp_str_tostring(x) // ']'
  end function ss
  function cs(p) result(s)
    type(cpp_cptr), intent(in) :: p
    character(len=:), allocatable :: s
    integer :: i, m
    m = cpp_a_strlen(p)
    allocate(character(len=m) :: s)
    do i = 1, m
      s(i:i) = achar(iand(int(p%b%a(p%o + i - 1), 4), 255))
    end do
  end function cs
  function S(x) result(r)
    character(len=*), intent(in) :: x
    class(cpp_str), pointer :: r
    r => cpp_str_new_cptr(lit(x))
  end function S
  ! R.Lit: a new NUL-terminated buffer (part C's cpp_r_lit, which this check does not link)
  function lit(x) result(p)
    character(len=*), intent(in) :: x
    type(cpp_cptr) :: p
    integer :: i
    p%b => cpp_arr_b_new(int(len(x) + 1, 8))
    do i = 1, len(x)
      p%b%a(i - 1) = transfer(x(i:i), 0_1)
    end do
    p%o = 0
  end function lit
  function hexb(a, n) result(s)
    class(cpp_arr_b), pointer, intent(in) :: a
    integer(4), intent(in) :: n
    character(len=:), allocatable :: s
    character(len=16), parameter :: digs = '0123456789abcdef'
    integer :: i, v
    s = ''
    do i = 0, n - 1
      v = iand(int(a%a(i), 4), 255)
      s = s // digs(v / 16 + 1:v / 16 + 1) // digs(mod(v, 16) + 1:mod(v, 16) + 1)
    end do
  end function hexb
  function head(n, cap) result(s)
    integer(8), intent(in) :: n, cap
    character(len=:), allocatable :: s
    s = u8(n) // '/' // u8(cap) // ':'
  end function head
  function vd(v) result(s)
    class(cpp_vec_d), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    integer :: i
    s = head(cpp_vec_d_size(v), cpp_vec_d_capacity(v))
    do i = 0, v%n - 1
      s = s // ' ' // d(v%a%a(i))
    end do
  end function vd
  function vi(v) result(s)
    class(cpp_vec_i), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    integer :: i
    s = head(cpp_vec_i_size(v), cpp_vec_i_capacity(v))
    do i = 0, v%n - 1
      s = s // ' ' // n4(v%a%a(i))
    end do
  end function vi
  function vb(v) result(s)
    class(cpp_vec_b), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    integer :: i
    s = head(cpp_vec_b_size(v), cpp_vec_b_capacity(v))
    do i = 0, v%n - 1
      s = s // ' ' // n4(int(v%a%a(i), 4))
    end do
  end function vb
  function vz(v) result(s)
    class(cpp_vec_z), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    integer :: i
    s = head(cpp_vec_z_size(v), int(size(v%a%a), 8))
    do i = 0, v%n - 1
      s = s // b(v%a%a(i))
    end do
  end function vz
  function vl(v) result(s)
    class(cpp_vec_l), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    integer :: i
    s = head(cpp_vec_l_size(v), int(size(v%a%a), 8))
    do i = 0, v%n - 1
      s = s // ' ' // n8(v%a%a(i))
    end do
  end function vl
  function vsh(v) result(s)
    class(cpp_vec_s), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    integer :: i
    s = head(cpp_vec_s_size(v), int(size(v%a%a), 8))
    do i = 0, v%n - 1
      s = s // ' ' // n4(int(v%a%a(i), 4))
    end do
  end function vsh
  function vf(v) result(s)
    class(cpp_vec_f), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    integer :: i
    s = head(cpp_vec_f_size(v), int(size(v%a%a), 8))
    do i = 0, v%n - 1
      s = s // ' ' // df(v%a%a(i))
    end do
  end function vf
  function vs(v) result(s)
    class(cpp_vec_o), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    integer :: i
    s = head(cpp_vec_o_size(v), cpp_vec_o_capacity(v))
    do i = 0, v%n - 1
      if (.not. associated(v%a%a(i)%p)) then
        s = s // ' null'
      else
        s = s // ' ' // ss(str_of(v%a%a(i)%p))
      end if
    end do
  end function vs
  ! the engine's h_uas_* / h_ref_* casts
  function str_of(x) result(r)
    class(*), pointer, intent(in) :: x
    class(cpp_str), pointer :: r
    r => null()
    select type (x)
    class is (cpp_str)
      r => x
    end select
  end function str_of
  function arrd_of(x) result(r)
    class(*), pointer, intent(in) :: x
    class(cpp_arr_d), pointer :: r
    r => null()
    if (.not. associated(x)) return
    select type (x)
    type is (cpp_arr_d)
      r => x
    end select
  end function arrd_of
  function arro_of(x) result(r)
    class(*), pointer, intent(in) :: x
    class(cpp_arr_o), pointer :: r
    r => null()
    if (.not. associated(x)) return
    select type (x)
    type is (cpp_arr_o)
      r => x
    end select
  end function arro_of
  function vecd_of(x) result(r)
    class(*), pointer, intent(in) :: x
    class(cpp_vec_d), pointer :: r
    r => null()
    select type (x)
    class is (cpp_vec_d)
      r => x
    end select
  end function vecd_of
  function pair_of(x) result(r)
    class(*), pointer, intent(in) :: x
    class(cpp_pair), pointer :: r
    r => null()
    select type (x)
    class is (cpp_pair)
      r => x
    end select
  end function pair_of
  function refd(x) result(r)
    class(*), pointer, intent(in) :: x
    real(8), pointer :: r
    r => null()
    select type (x)
    type is (real(8))
      r => x
    end select
  end function refd
  function refi(x) result(r)
    class(*), pointer, intent(in) :: x
    integer(4), pointer :: r
    r => null()
    select type (x)
    type is (integer(4))
      r => x
    end select
  end function refi
  function refc(x) result(r)
    class(*), pointer, intent(in) :: x
    type(cpp_cptr), pointer :: r
    r => null()
    select type (x)
    type is (cpp_cptr)
      r => x
    end select
  end function refc
  function boxd(v) result(r)
    real(8), intent(in) :: v
    class(*), pointer :: r
    allocate(r, source=v)
  end function boxd
  function boxi(v) result(r)
    integer(4), intent(in) :: v
    class(*), pointer :: r
    allocate(r, source=v)
  end function boxi
  function boxc(v) result(r)
    type(cpp_cptr), intent(in) :: v
    class(*), pointer :: r
    allocate(r, source=v)
  end function boxc
  function arrd(xs) result(r)
    real(8), intent(in) :: xs(:)
    class(cpp_arr_d), pointer :: r
    r => cpp_arr_d_new(int(size(xs), 8))
    r%a(:) = xs
  end function arrd
  function arri(xs) result(r)
    integer(4), intent(in) :: xs(:)
    class(cpp_arr_i), pointer :: r
    r => cpp_arr_i_new(int(size(xs), 8))
    r%a(:) = xs
  end function arri
  ! a variant's value as C# prints the boxed object
  function vval(v) result(s)
    class(cpp_variant), pointer, intent(in) :: v
    character(len=:), allocatable :: s
    class(*), pointer :: x
    x => v%v
    s = '?'
    select type (x)
    type is (logical)
      s = merge('True ', 'False', x)
      s = trim(s)
    type is (integer(4))
      s = n4(x)
    type is (real(8))
      s = d(x)
    class is (cpp_str)
      s = cpp_str_tostring(x)
    end select
  end function vval
  function ibi(it) result(i)
    type(cpp_iter_b), intent(in) :: it
    integer(8) :: i
    i = it%i
  end function ibi
  function izi(it) result(i)
    type(cpp_iter_z), intent(in) :: it
    integer(8) :: i
    i = it%i
  end function izi
  function ili(it) result(i)
    type(cpp_iter_l), intent(in) :: it
    integer(8) :: i
    i = it%i
  end function ili
  function ifi(it) result(i)
    type(cpp_iter_f), intent(in) :: it
    integer(8) :: i
    i = it%i
  end function ifi
  function iii(it) result(i)
    type(cpp_iter_i), intent(in) :: it
    integer(8) :: i
    i = it%i
  end function iii
  function co(p) result(o)
    type(cpp_cptr), intent(in) :: p
    integer(4) :: o
    o = p%o
  end function co
  function ad_len(x) result(n)
    class(*), pointer, intent(in) :: x
    integer(4) :: n
    class(cpp_arr_d), pointer :: a
    a => arrd_of(x)
    n = size(a%a)
  end function ad_len
  function ad_at(x, i) result(v)
    class(*), pointer, intent(in) :: x
    integer, intent(in) :: i
    real(8) :: v
    class(cpp_arr_d), pointer :: a
    a => arrd_of(x)
    v = a%a(i)
  end function ad_at
  function ai_len(x) result(n)
    class(*), pointer, intent(in) :: x
    integer(4) :: n
    n = -1
    select type (x)
    type is (cpp_arr_i)
      n = size(x%a)
    end select
  end function ai_len
  function ai_at(x, i) result(v)
    class(cpp_arr_i), pointer, intent(in) :: x
    integer, intent(in) :: i
    integer(4) :: v
    v = x%a(i)
  end function ai_at
  function al_at(x, i) result(v)
    class(cpp_arr_l), pointer, intent(in) :: x
    integer, intent(in) :: i
    integer(8) :: v
    v = x%a(i)
  end function al_at
  function ao_slot(x, i) result(r)
    class(*), pointer, intent(in) :: x
    integer, intent(in) :: i
    class(*), pointer :: r
    class(cpp_arr_o), pointer :: a
    a => arro_of(x)
    r => a%a(i)%p
  end function ao_slot
  function pair_second(x) result(r)
    class(*), pointer, intent(in) :: x
    class(*), pointer :: r
    class(cpp_pair), pointer :: p
    p => pair_of(x)
    r => p%second%p
  end function pair_second
  function opair(v) result(r)
    class(cpp_pair), pointer, intent(in) :: v
    class(*), pointer :: r
    r => v
  end function opair
  function SO(x) result(r)
    character(len=*), intent(in) :: x
    class(*), pointer :: r
    r => S(x)
  end function SO
  function ovd(v) result(r)
    class(cpp_vec_d), pointer, intent(in) :: v
    class(*), pointer :: r
    r => v
  end function ovd
  function oad(v) result(r)
    class(cpp_arr_d), pointer, intent(in) :: v
    class(*), pointer :: r
    r => v
  end function oad
  function oao(v) result(r)
    class(cpp_arr_o), pointer, intent(in) :: v
    class(*), pointer :: r
    r => v
  end function oao
end module chk

program corecheck
  use chk
  implicit none
  outbuf = ''
  call vecd()
  call veci()
  call vecsmall()
  call veco()
  call iter()
  call str()
  call tostr()
  call cptr()
  call ptr()
  call misc()
  call extra()
  call nums()
  call fuzz()
contains

  subroutine vecd()
    class(cpp_vec_d), pointer :: v, w, c, e, eq3, m, ma, e1, e2, g, t1, t2
    type(cpp_iter_d) :: it, r
    type(cpp_ptr_d) :: dp, gp
    character(len=:), allocatable :: caps
    class(cpp_arr_d), pointer :: raw
    integer :: i
    v => cpp_vec_d_new_0()
    call emit('empty0', u8(cpp_vec_d_size(v)) // ' ' // u8(cpp_vec_d_capacity(v)) // ' ' // b(cpp_vec_d_empty(v)))
    caps = ''
    do i = 0, 19
      call cpp_vec_d_push_back(v, i + 0.5_8)
      caps = caps // u8(cpp_vec_d_capacity(v)) // ' '
    end do
    call emit('grow', caps)
    call emit('frontBack', d(cpp_vec_d_front(v)) // ' ' // d(cpp_vec_d_back(v)))
    call cpp_vec_d_resize_ul(v, 5_8); call emit('resize5', vd(v))
    call cpp_vec_d_resize_ul(v, 9_8); call emit('resize9', vd(v))
    call cpp_vec_d_resize_ul_t(v, 12_8, 7.5_8); call emit('resize12', vd(v))
    call cpp_vec_d_resize_ul_t(v, 12_8, 1.0_8); call emit('resizeSame', vd(v))
    w => cpp_vec_d_new_ta(arrd([10.0_8, 20.0_8, 30.0_8]))
    call emit('w', vd(w))
    it = cpp_vec_d_insert_itert_t(v, cpp_iter_d_op_add(cpp_vec_d_begin(v), 2_8), 99.0_8)
    call emit('insT', vd(v) // ' @' // n8(it%i))
    it = cpp_vec_d_insert_itert_ta(v, cpp_iter_d_op_add(cpp_vec_d_begin(v), 1_8), arrd([1.0_8, 2.0_8, 3.0_8]))
    call emit('insTA', vd(v) // ' @' // n8(it%i))
    raw => arrd([5.0_8, 6.0_8, 7.0_8, 8.0_8])
    it = cpp_vec_d_insert_itert_ptrt_ptrt(v, cpp_iter_d_op_add(cpp_vec_d_begin(v), 4_8), cpp_ptr_d_new_ta_l(raw, 1_8), &
      cpp_ptr_d_new_ta_l(raw, 3_8))
    call emit('insP', vd(v) // ' @' // n8(it%i))
    it = cpp_vec_d_insert_itert_ta(v, cpp_vec_d_end(v), arrd([77.0_8]))
    call emit('insEnd', vd(v) // ' @' // n8(it%i))
    r = cpp_vec_d_erase_itert(v, cpp_iter_d_op_add(cpp_vec_d_begin(v), 3_8)); call emit('erase1', vd(v) // ' @' // n8(r%i))
    r = cpp_vec_d_erase_itert(v, cpp_iter_d_op_add(cpp_vec_d_end(v), -1_8)); call emit('eraseLast', vd(v) // ' @' // n8(r%i))
    call cpp_vec_d_reserve(v, 100_8); call emit('reserve', vd(v))
    call cpp_vec_d_reserve(v, 10_8); call emit('reserveLess', vd(v))
    call cpp_vec_d_shrink_to_fit(v); call emit('shrink', vd(v))
    call cpp_vec_d_assign_ul_t(v, 3_8, 2.0_8); call emit('assignN', vd(v))
    call cpp_vec_d_assign_ul_t(v, 30_8, 4.0_8); call emit('assignBig', u8(cpp_vec_d_size(v)) // '/' // u8(cpp_vec_d_capacity(v)))
    call cpp_vec_d_assign_ul_t(v, 2_8, -1.0_8); call emit('assignSmall', vd(v))
    call cpp_vec_d_swap(v, w); call emit('swapV', vd(v)); call emit('swapW', vd(w))
    c => cpp_vec_d_clone(v); call emit('clone', vd(c))
    e => cpp_vec_d_new_0(); call cpp_vec_d_reserve(e, 10_8); call cpp_vec_d_assign_vect(e, v); call emit('assign', vd(e))
    call cpp_vec_d_push_back(e, 1.0_8); call cpp_vec_d_assign_vect(e, v); call emit('assignAgain', vd(e))
    eq3 => cpp_vec_d_new_ul(3_8); call cpp_vec_d_assign_vect(eq3, v); call emit('assignSameCap', vd(eq3))
    call cpp_vec_d_assign_vect(e, e); call emit('assignSelf', vd(e))
    m => cpp_vec_d_new_0(); call cpp_vec_d_movefrom_vect(m, c); call emit('moveTo', vd(m)); call emit('moveFrom', vd(c))
    call cpp_vec_d_movefrom_vect(m, m); call emit('moveSelf', vd(m))
    ma => cpp_vec_d_new_0(); call cpp_vec_d_movefrom_ta(ma, arrd([4.0_8, 5.0_8])); call emit('moveArr', vd(ma))
    dp = cpp_vec_d_data(v); call emit('data', d(cpp_ptr_d_item_i(dp, 1)) // ' ' // n8(cpp_ptr_d_o(dp)))
    call cpp_vec_d_clear(v); call emit('clear', vd(v) // ' ' // b(cpp_vec_d_empty(v)))
    call cpp_vec_d_resize_ul(v, 2_8); call emit('afterClear', vd(v))
    t1 => cpp_vec_d_new_ta(arrd([transfer(-2251799813685248_8, 1.0_8), 0.0_8]))
    t2 => cpp_vec_d_new_ta(arrd([transfer(-2251799813685248_8, 1.0_8), -0.0_8]))
    call emit('eqNaN', b(cpp_vec_d_eq(t1, t2)))
    t1 => cpp_vec_d_new_ul(2_8); t2 => cpp_vec_d_new_ul(3_8)
    call emit('eqLen', b(cpp_vec_d_eq(t1, t2)))
    t1 => cpp_vec_d_new_ta(arrd([1.0_8, 2.0_8])); t2 => cpp_vec_d_new_ta(arrd([1.0_8, 3.0_8]))
    call emit('eqVal', b(cpp_vec_d_eq(t1, t2)))
    t1 => cpp_vec_d_new_ul_t(4_8, 1.25_8)
    caps = vd(t1)
    t1 => cpp_vec_d_new_ul(0_8)
    caps = caps // ' ' // vd(t1)
    t1 => cpp_vec_d_new_ul(3_8)
    call emit('ctorN', caps // ' ' // vd(t1))
    e1 => cpp_vec_d_new_0(); e2 => cpp_vec_d_new_0()
    t1 => cpp_vec_d_new_ul(0_8)
    call emit('emptyShared', b(cpp_ptr_d_op_eq(cpp_vec_d_data(e1), cpp_vec_d_data(e2))) // &
      b(cpp_ptr_d_op_eq(cpp_vec_d_data(t1), cpp_vec_d_data(e1))))
    g => cpp_vec_d_new_0(); call cpp_vec_d_resize_ul(g, 3_8); gp = cpp_vec_d_data(g); call cpp_vec_d_push_back(g, 1.0_8)
    call emit('growNewArr', b(cpp_ptr_d_op_eq(gp, cpp_vec_d_data(g))) // ' ' // u8(cpp_vec_d_capacity(g)))
  end subroutine vecd

  function cpp_ptr_d_o(p) result(o)
    type(cpp_ptr_d), intent(in) :: p
    integer(8) :: o
    o = p%o
  end function cpp_ptr_d_o

  subroutine veci()
    class(cpp_vec_i), pointer :: w, u, z, c, a, m, t
    type(cpp_iter_i) :: it, ib, ie
    type(cpp_ptr_i) :: dp
    integer(4), pointer :: q
    character(len=:), allocatable :: x
    w => vnew_i([1, 2, 3])
    call emit('vi', vi(w)); call cpp_vec_i_push_back(w, 4); call emit('vi4', vi(w))
    q => cpp_vec_i_at(w, 7_8)
    call catch_oor('at')
    q => cpp_vec_i_at(w, -1_8)
    call catch_oor('at')
    q => cpp_vec_i_at(w, 1_8); q = 20
    call emit('atSet', vi(w) // ' ' // n4(cpp_vec_i_back(w)))
    u => vnew_i([7, 8])
    it = cpp_vec_i_insert_itert_itert_itert(w, cpp_iter_i_op_add(cpp_vec_i_begin(w), 1_8), cpp_vec_i_begin(u), cpp_vec_i_end(u))
    call emit('insII', vi(w) // ' @' // n8(it%i))
    it = cpp_vec_i_insert_itert_itert_itert(w, cpp_iter_i_op_add(cpp_vec_i_begin(w), 2_8), cpp_vec_i_begin(w), &
      cpp_iter_i_op_add(cpp_vec_i_begin(w), 3_8))
    call emit('insSelf', vi(w) // ' @' // n8(it%i))
    it = cpp_vec_i_insert_itert_t(w, cpp_vec_i_begin(w), 5); call emit('insT', vi(w) // ' @' // n8(it%i))
    it = cpp_vec_i_erase_itert_itert(w, cpp_iter_i_op_add(cpp_vec_i_begin(w), 1_8), cpp_iter_i_op_add(cpp_vec_i_begin(w), 4_8))
    call emit('eraseRange', vi(w) // ' @' // n8(it%i))
    it = cpp_vec_i_erase_itert_itert(w, cpp_iter_i_op_add(cpp_vec_i_begin(w), 2_8), cpp_iter_i_op_add(cpp_vec_i_begin(w), 2_8))
    call emit('eraseNone', vi(w) // ' @' // n8(it%i))
    it = cpp_vec_i_erase_itert(w, cpp_vec_i_begin(w)); call emit('erase1', vi(w) // ' @' // n8(it%i))
    call cpp_vec_i_resize_ul_t(w, 9_8, -3); call emit('resizeT', vi(w))
    call cpp_vec_i_resize_ul_t(w, 4_8, 9); call emit('resizeDown', vi(w))
    call cpp_vec_i_resize_ul(w, 6_8); call emit('resizeUp', vi(w))
    z => cpp_vec_i_new_ul(5_8); t => cpp_vec_i_new_ul_t(3_8, 6)
    call emit('ctorN', vi(z) // ' ' // vi(t))
    call cpp_vec_i_assign_ul_t(z, 2_8, 1); call emit('assignN', vi(z))
    call cpp_vec_i_swap(z, w); call emit('swap', vi(z) // ' | ' // vi(w))
    call cpp_vec_i_shrink_to_fit(w); call emit('shrink', vi(w))
    call cpp_vec_i_reserve(w, 3_8); call cpp_vec_i_reserve(w, 40_8); call emit('reserve', vi(w))
    c => cpp_vec_i_clone(z); call cpp_vec_i_push_back(c, 1); call emit('clone', vi(z) // ' | ' // vi(c))
    a => cpp_vec_i_new_0(); call cpp_vec_i_assign_vect(a, z); call emit('assign', vi(a))
    m => cpp_vec_i_new_0(); call cpp_vec_i_movefrom_vect(m, a); call emit('move', vi(m) // ' | ' // vi(a))
    call emit('eq', b(cpp_vec_i_eq(m, z)) // b(cpp_vec_i_eq(m, c)))
    dp = cpp_vec_i_data(m)
    x = n4(cpp_ptr_i_item_i(dp, 2)) // ' ' // n8(dp_o_i(dp)) // ' ' // n4(m%a%a(0)) // ' ' // n4(cpp_vec_i_back(m))
    call emit('data', x)
    call cpp_vec_i_clear(m); call emit('clear', vi(m) // ' ' // b(cpp_vec_i_empty(m)))
    ib = cpp_vec_i_begin(m); ie = cpp_vec_i_end(m); call emit('iterEmpty', n8(cpp_iter_i_op_sub_itert_itert(ie, ib)))
  end subroutine veci

  function dp_o_i(p) result(o)
    type(cpp_ptr_i), intent(in) :: p
    integer(8) :: o
    o = p%o
  end function dp_o_i

  function vnew_i(xs) result(v)
    integer(4), intent(in) :: xs(:)
    class(cpp_vec_i), pointer :: v
    integer :: i
    v => cpp_vec_i_new_0()
    ! Vec<int>(int[]): built element by element with the same growth as the C#'s exact-size array
    call cpp_vec_i_reserve(v, int(size(xs), 8))
    do i = 1, size(xs)
      call cpp_vec_i_push_back(v, xs(i))
    end do
  end function vnew_i

  subroutine catch_oor(tag)
    character(len=*), intent(in) :: tag
    class(cpp_object), pointer :: e
    e => cpp_exc
    cpp_exc => null()
    select type (e)
    class is (cpp_outofrange)
      call emit(tag, cs(cpp_cppexception_what(e)))
    class default
      call emit(tag, 'NO EXCEPTION')
    end select
  end subroutine catch_oor

  subroutine vecsmall()
    class(cpp_vec_b), pointer :: bv, bc, ba, bm, t1, t2
    class(cpp_vec_z), pointer :: zv, zc, za, zm
    class(cpp_vec_l), pointer :: lv, lc, la, lm, l2
    class(cpp_vec_s), pointer :: sv, sc, sa
    class(cpp_vec_f), pointer :: fv, fc, fa
    type(cpp_iter_b) :: ib
    type(cpp_ptr_b) :: bd
    type(cpp_ptr_f) :: fdp
    integer :: i
    character(len=:), allocatable :: x
    bv => cpp_vec_b_new_0()
    do i = 0, 4
      call cpp_vec_b_push_back(bv, int(ibits(i * 60, 0, 8) - merge(256, 0, i * 60 > 127), 1))
    end do
    call emit('vb', vb(bv))
    call cpp_vec_b_resize_ul(bv, 7_8); call cpp_vec_b_resize_ul_t(bv, 8_8, -1_1); call emit('vbResize', vb(bv))
    bc => cpp_vec_b_clone(bv); ib = cpp_vec_b_insert_itert_t(bc, cpp_iter_b_op_add(cpp_vec_b_begin(bc), 1_8), 9_1)
    call emit('vbClone', vb(bv) // ' | ' // vb(bc))
    ib = cpp_vec_b_erase_itert(bc, cpp_iter_b_op_add(cpp_vec_b_begin(bc), 3_8)); call emit('vbErase', vb(bc))
    ba => cpp_vec_b_new_0()
    call cpp_vec_b_assign_itert_itert(ba, cpp_iter_b_op_add(cpp_vec_b_begin(bv), 1_8), cpp_iter_b_op_add(cpp_vec_b_begin(bv), 4_8))
    call emit('vbAssignII', vb(ba))
    call cpp_vec_b_assign_ul_t(ba, 3_8, 5_1); call emit('vbAssignN', vb(ba))
    bm => cpp_vec_b_new_0(); call cpp_vec_b_movefrom_vect(bm, ba); call emit('vbMove', vb(bm) // ' | ' // vb(ba))
    call cpp_vec_b_assign_vect(bm, bv); call emit('vbAssign', vb(bm))
    call cpp_vec_b_reserve(bm, 30_8); call cpp_vec_b_shrink_to_fit(bm); call emit('vbShrink', vb(bm))
    bd = cpp_vec_b_data(bm); call emit('vbData', n4(int(cpp_ptr_b_item_i(bd, 2), 4)) // ' ' // b(cpp_vec_b_empty(bm)))
    t1 => cpp_vec_b_new_ul(2_8); t2 => cpp_vec_b_new_ul_t(2_8, -7_1)
    call emit('vbEq', b(cpp_vec_b_eq(bm, bv)) // b(cpp_vec_b_eq(bm, bc)) // ' ' // vb(t1) // ' ' // vb(t2))
    call cpp_vec_b_clear(bm); call emit('vbClear', vb(bm) // ' ' // n8(ibi(cpp_vec_b_end(bv)) - ibi(cpp_vec_b_begin(bv))))
    zv => cpp_vec_z_new_ul_t(3_8, .true.); call cpp_vec_z_resize_ul(zv, 5_8); call emit('vz', vz(zv))
    call cpp_vec_z_push_back(zv, .true.); call cpp_vec_z_resize_ul_t(zv, 8_8, .true.); call cpp_vec_z_resize_ul_t(zv, 7_8, .false.)
    call emit('vzPush', vz(zv))
    zc => cpp_vec_z_clone(zv); call cpp_vec_z_assign_ul_t(zc, 2_8, .true.); call emit('vzClone', vz(zv) // ' | ' // vz(zc))
    za => cpp_vec_z_new_0(); call cpp_vec_z_assign_vect(za, zv); call cpp_vec_z_reserve(za, 20_8); call cpp_vec_z_shrink_to_fit(za)
    call emit('vzAssign', vz(za))
    zm => cpp_vec_z_new_0(); call cpp_vec_z_movefrom_vect(zm, za); call cpp_vec_z_clear(zm)
    call emit('vzMove', vz(zm) // ' | ' // vz(za) // ' ' // n8(izi(cpp_vec_z_end(zv)) - izi(cpp_vec_z_begin(zv))))
    lv => cpp_vec_l_new_0(); call cpp_vec_l_push_back(lv, -huge(1_8) - 1_8); call cpp_vec_l_push_back(lv, -1_8)
    call cpp_vec_l_resize_ul_t(lv, 4_8, 7_8); call emit('vl', vl(lv))
    lc => cpp_vec_l_clone(lv); call cpp_vec_l_assign_ul_t(lc, 1_8, 3_8); call cpp_vec_l_reserve(lv, 9_8); call cpp_vec_l_shrink_to_fit(lv)
    call emit('vlClone', vl(lv) // ' | ' // vl(lc))
    la => cpp_vec_l_new_0(); call cpp_vec_l_assign_vect(la, lv); call cpp_vec_l_resize_ul(la, 2_8); call cpp_vec_l_resize_ul(la, 3_8)
    call emit('vlAssign', vl(la))
    lm => cpp_vec_l_new_0(); call cpp_vec_l_movefrom_vect(lm, la); call cpp_vec_l_clear(lm)
    l2 => cpp_vec_l_new_ul(2_8)
    call emit('vlMove', vl(lm) // ' | ' // vl(la) // ' ' // n8(ili(cpp_vec_l_end(lv)) - ili(cpp_vec_l_begin(lv))) // ' ' // vl(l2))
    sv => cpp_vec_s_new_0(); call cpp_vec_s_resize_ul(sv, 3_8); call cpp_vec_s_resize_ul_t(sv, 5_8, -2_2); call cpp_vec_s_reserve(sv, 8_8)
    call emit('vs', vsh(sv))
    sc => cpp_vec_s_clone(sv); call cpp_vec_s_assign_ul_t(sc, 2_8, 300_2); call cpp_vec_s_shrink_to_fit(sv)
    call emit('vsClone', vsh(sv) // ' | ' // vsh(sc))
    sa => cpp_vec_s_new_0(); call cpp_vec_s_assign_vect(sa, sc); call cpp_vec_s_resize_ul(sa, 1_8)
    call emit('vsAssign', vsh(sa) // ' ' // u8(cpp_vec_s_size(sa)))
    fv => cpp_vec_f_new_0(); call cpp_vec_f_push_back(fv, 0.1_4); call cpp_vec_f_push_back(fv, -2.5_4); call cpp_vec_f_resize_ul(fv, 4_8)
    call emit('vf', vf(fv))
    fc => cpp_vec_f_clone(fv); call cpp_vec_f_assign_ul_t(fc, 2_8, 1.5_4); call emit('vfClone', vf(fv) // ' | ' // vf(fc))
    fa => cpp_vec_f_new_0(); call cpp_vec_f_assign_vect(fa, fv); call cpp_vec_f_clear(fa); call cpp_vec_f_push_back(fa, 3.0_4)
    fdp = cpp_vec_f_data(fv)
    x = vf(fa) // ' ' // n8(ifi(cpp_vec_f_end(fv)) - ifi(cpp_vec_f_begin(fv))) // ' ' // u8(cpp_vec_f_size(fv)) // ' ' // &
      df(cpp_ptr_f_item_i(fdp, 1))
    call emit('vfAssign', x)
  end subroutine vecsmall

  subroutine veco()
    class(cpp_vec_o), pointer :: sv, sc, sa, sm, st, mvt, nul, nul2, va, vc, none, none2, none3, deep, dc, cv, cc, pv, pc, &
      vv, vvc, tmp
    class(fn_str), pointer :: fstr
    class(fn_arr3), pointer :: f3
    class(fn_arr4), pointer :: f4
    class(cpp_func_o), pointer :: fo
    type(cpp_iter_o) :: it
    type(cpp_reviter_o) :: ri
    type(cpp_ptr_o) :: sd
    type(cpp_slot), pointer :: sl
    class(cpp_str), pointer :: x, y
    class(cpp_arr_o), pointer :: ao, ad
    class(cpp_arr_d), pointer :: a3
    class(cpp_pair), pointer :: pp
    class(cpp_vec_d), pointer :: v0
    type(cpp_cptr) :: c0
    class(cpp_arr_b), pointer :: cb
    character(len=:), allocatable :: line
    real(8), pointer :: rp
    allocate(fstr)
    fo => fstr
    sv => cpp_vec_o_new_ul_funct(3_8, fo, cpp_a_mk_str, 2)
    x => cpp_str_append_cptr(str_of(sv%a%a(1)%p), lit('one'))
    call cpp_vec_o_push_back(sv, SO('two'))
    sc => cpp_vec_o_clone(sv)
    x => cpp_str_append_cptr(str_of(sc%a%a(1)%p), lit('!'))
    call emit('strVec', vs(sv)); call emit('strClone', vs(sc))
    call emit('strEq', b(cpp_vec_o_eq(sv, cpp_vec_o_clone(sv))) // b(cpp_vec_o_eq(sv, sc)))
    call cpp_vec_o_resize_ul(sv, 6_8); call emit('strResize', vs(sv))
    it = cpp_vec_o_insert_itert_itert_itert(sv, cpp_iter_o_op_add(cpp_vec_o_begin(sv), 1_8), cpp_vec_o_begin(sc), &
      cpp_iter_o_op_add(cpp_vec_o_begin(sc), 2_8))
    x => cpp_str_append_cptr(str_of(sc%a%a(1)%p), lit('?'))
    call emit('strIns', vs(sv) // ' @' // n8(it%i))
    it = cpp_vec_o_erase_itert_itert(sv, cpp_vec_o_begin(sv), cpp_iter_o_op_add(cpp_vec_o_begin(sv), 2_8))
    call emit('strErase', vs(sv) // ' @' // n8(it%i))
    it = cpp_vec_o_erase_itert(sv, cpp_iter_o_op_add(cpp_vec_o_begin(sv), 1_8)); call emit('strErase1', vs(sv) // ' @' // n8(it%i))
    it = cpp_vec_o_insert_itert_t(sv, cpp_iter_o_op_add(cpp_vec_o_begin(sv), 2_8), SO('ins'))
    call emit('strInsT', vs(sv) // ' @' // n8(it%i))
    sl => cpp_vec_o_at(sv, 99_8)
    call catch_oor('strAt')
    sl => cpp_vec_o_at(sv, 0_8)
    x => cpp_str_append_cptr(str_of(sl%p), lit('@'))
    sl => cpp_vec_o_at(sv, 0_8)
    line = ss(str_of(sl%p))
    sl => cpp_vec_o_front(sv)
    line = line // ' ' // ss(str_of(sl%p))
    sl => cpp_vec_o_back(sv)
    call emit('strAtOk', line // ' ' // ss(str_of(sl%p)))
    sl => cpp_vec_o_emplace_back(sv, SO('emp'))
    x => cpp_str_append_cptr(str_of(sl%p), lit('+'))
    call emit('strEmplace', vs(sv))
    call cpp_vec_o_pop_back(sv); call emit('strPop', vs(sv) // ' ' // b(cpp_vec_o_empty(sv)))
    sa => cpp_vec_o_new_0(cpp_a_mk_str, 2); call cpp_vec_o_assign_vect(sa, sv)
    x => cpp_str_append_cptr(str_of(sv%a%a(0)%p), lit('#')); call emit('strAssign', vs(sa))
    call cpp_vec_o_assign_ul_t(sa, 2_8, SO('rep')); x => cpp_str_append_cptr(str_of(sa%a%a(0)%p), lit('1'))
    call emit('strAssignN', vs(sa))
    call cpp_vec_o_assign_itert_itert(sa, cpp_iter_o_op_add(cpp_vec_o_begin(sv), 1_8), cpp_vec_o_end(sv))
    x => cpp_str_append_cptr(str_of(sv%a%a(1)%p), lit('&')); call emit('strAssignII', vs(sa))
    call cpp_vec_o_resize_ul_t(sa, 5_8, SO('pad')); x => cpp_str_append_cptr(str_of(sa%a%a(3)%p), lit('3'))
    call emit('strResizeT', vs(sa))
    call cpp_vec_o_resize_ul(sa, 2_8); call emit('strShrink', vs(sa))
    sm => cpp_vec_o_new_0(cpp_a_mk_str, 2); call cpp_vec_o_movefrom_vect(sm, sa); call emit('strMove', vs(sm) // ' | ' // vs(sa))
    call cpp_vec_o_swap(sm, sa); call emit('strSwap', vs(sm) // ' | ' // vs(sa))
    call cpp_vec_o_reserve(sa, 11_8); call emit('strReserve', vs(sa)); call cpp_vec_o_shrink_to_fit(sa); call emit('strFit', vs(sa))
    line = ''
    ri = cpp_vec_o_rbegin(sv)
    do while (.not. cpp_reviter_o_op_eq(ri, cpp_vec_o_rend(sv)))
      sl => cpp_reviter_o_deref(ri)
      line = line // ss(str_of(sl%p))
      ri = cpp_reviter_o_op_inc(ri)
    end do
    call emit('strRev', line)
    sd = cpp_vec_o_data(sv); sl => cpp_ptr_o_item_i(sd, 1)
    call emit('strData', ss(str_of(sl%p)) // ' ' // b(cpp_ptr_o_isnull(sd)))
    ao => cpp_arr_o_new(2_8); ao%a(0)%p => S('x'); ao%a(1)%p => S('y')
    st => cpp_vec_o_new_ta(ao, cpp_a_mk_str, 2); call emit('strTA', vs(st))
    ao => cpp_arr_o_new(3_8); ao%a(0)%p => S('p'); ao%a(1)%p => S('q'); ao%a(2)%p => S('r')
    mvt => cpp_vec_o_new_0(cpp_a_mk_str, 2); call cpp_vec_o_movefrom_ta(mvt, ao); call emit('strMoveTA', vs(mvt))
    nul => cpp_vec_o_new_ul(2_8, cpp_a_mk_str, 2); call emit('strMake', vs(nul))
    nul2 => cpp_vec_o_new_ul_t(2_8, SO('v'), cpp_a_mk_str, 2); x => cpp_str_append_cptr(str_of(nul2%a%a(0)%p), lit('0'))
    call emit('strCtorT', vs(nul2))
    call cpp_vec_o_clear(sv); call emit('strClear', vs(sv) // ' ' // b(cpp_vec_o_empty(sv)))
    ! Vec<double[]>: mode 3
    allocate(f3)
    fo => f3
    va => cpp_vec_o_new_ul_funct(2_8, fo, cpp_a_mk_null, 3)
    va%a%a(0)%p => arrd([1.0_8, 2.0_8, 3.0_8])
    vc => cpp_vec_o_clone(va)
    a3 => arrd_of(vc%a%a(0)%p); a3%a(1) = 42.0_8
    call emit('arrVec', d(ad_at(va%a%a(0)%p, 1)) // ' ' // d(ad_at(vc%a%a(0)%p, 1)) // ' ' // &
      n4(ad_len(va%a%a(1)%p)))
    none => cpp_vec_o_new_0(cpp_a_mk_null, 3)
    tmp => cpp_vec_o_new_funct(fo, cpp_a_mk_null, 3)
    call cpp_vec_o_assign_vect(none, tmp)
    call cpp_vec_o_resize_ul(none, 2_8)
    call emit('mkAdopt', n4(ad_len(none%a%a(1)%p)))
    allocate(f4)
    fo => f4
    none2 => cpp_vec_o_new_0(cpp_a_mk_null, 3)
    tmp => cpp_vec_o_new_funct(fo, cpp_a_mk_null, 3)
    call cpp_vec_o_movefrom_vect(none2, tmp)
    call cpp_vec_o_resize_ul(none2, 1_8)
    call emit('mkMove', n4(ad_len(none2%a%a(0)%p)))
    none3 => cpp_vec_o_new_0(cpp_a_mk_null, 3); call cpp_vec_o_resize_ul(none3, 2_8)
    call emit('mkNull', b(.not. associated(none3%a%a(0)%p)) // ' ' // u8(cpp_vec_o_size(none3)))
    deep => cpp_vec_o_new_0(cpp_a_mk_null, 3)
    ad => cpp_arr_o_new(2_8); ad%a(0)%p => arrd([1.0_8, 2.0_8])
    call cpp_vec_o_push_back(deep, oao(ad))
    dc => cpp_vec_o_clone(deep)
    a3 => arrd_of(ao_slot(dc%a%a(0)%p, 0)); a3%a(1) = 7.0_8
    call emit('vecDeep', d(ad_at(ao_slot(deep%a%a(0)%p, 0), 1)) // ' ' // d(ad_at(ao_slot(dc%a%a(0)%p, 0), 1)) // &
      ' ' // b(.not. associated(ao_slot(dc%a%a(0)%p, 1))))
    ! Vec<CPtr>: mode 0
    cb => cpp_arr_b_new(4_8); cb%a = [65_1, 66_1, 67_1, 0_1]
    cv => cpp_vec_o_new_0(mk_cptr, 0)
    c0%b => cb; c0%o = 0; call cpp_vec_o_push_back(cv, boxc(c0))
    c0%o = 2; call cpp_vec_o_push_back(cv, boxc(c0))
    call cpp_vec_o_resize_ul(cv, 3_8)
    cc => cpp_vec_o_clone(cv)
    tmp => cpp_vec_o_new_ul(3_8, mk_cptr, 0)
    call emit('cptrVec', cs(refc(cc%a%a(1)%p)) // ' ' // b(cpp_cptr_isnull(refc(cc%a%a(2)%p))) // ' ' // &
      b(cpp_vec_o_eq(cv, cc)) // b(cpp_vec_o_eq(cv, tmp)))
    ! Vec<Pair<int, double>>: mode 2, Equals by identity
    pv => cpp_vec_o_new_0(mk_pair_id, 2)
    pp => cpp_pair_new_a_b(boxi(1), boxd(2.5_8), cpp_a_mk_i, 0, cpp_a_mk_d, 0)
    call cpp_vec_o_push_back(pv, opair(pp))
    pc => cpp_vec_o_clone(pv)
    pp => pair_of(pc%a%a(0)%p); rp => refd(pp%second%p); rp = 9.0_8
    call emit('pairVec', d(refd(pair_second(pv%a%a(0)%p))) // ' ' // d(refd(pair_second(pc%a%a(0)%p))) // ' ' // &
      b(cpp_vec_o_eq(pv, pv)) // b(cpp_vec_o_eq(pv, pc)))
    ! Vec<Vec<double>>: mode 2
    vv => cpp_vec_o_new_ul(2_8, mk_vecd, 2)
    call cpp_vec_d_push_back(vecd_of(vv%a%a(0)%p), 1.0_8)
    vvc => cpp_vec_o_clone(vv)
    call cpp_vec_d_push_back(vecd_of(vvc%a%a(0)%p), 2.0_8)
    v0 => vecd_of(vv%a%a(0)%p)
    line = vd(v0) // ' | '
    v0 => vecd_of(vvc%a%a(0)%p)
    line = line // vd(v0) // ' '
    v0 => vecd_of(vvc%a%a(1)%p)
    call emit('vecVec', line // vd(v0))
    y => null()
  end subroutine veco

  subroutine iter()
    class(cpp_vec_d), pointer :: w
    class(cpp_vec_i), pointer :: wi
    class(cpp_vec_b), pointer :: bs
    class(cpp_vec_o), pointer :: so
    type(cpp_iter_d) :: it, inc, bi, t
    type(cpp_iter_i) :: ii
    type(cpp_iter_b) :: bb
    type(cpp_iter_o) :: oi
    type(cpp_reviter_o) :: rb, rb1, rb2, rb3
    type(cpp_ptr_d) :: fromit
    type(cpp_cspan_o) :: sp, sp0, sp2
    type(cpp_slot), pointer :: sl
    class(cpp_arr_o), pointer :: ao
    character(len=:), allocatable :: line
    w => cpp_vec_d_new_ta(arrd([1.0_8, 2.0_8, 3.0_8]))
    it = cpp_vec_d_begin(w)
    call cpp_vec_d_push_back(w, 4.0_8)
    w%a%a(0) = 100.0_8
    line = d(cpp_iter_d_deref(it)) // ' ' // d(cpp_iter_d_deref(cpp_iter_d_op_add(it, 0_8)))
    it = cpp_iter_d_op_inc(it)
    call emit('staleIter', line // ' ' // d(cpp_iter_d_deref(it)))
    inc = cpp_vec_d_begin(w); inc = cpp_iter_d_op_inc(inc)
    call emit('iterSub', n8(cpp_iter_d_op_sub_itert_itert(cpp_vec_d_end(w), cpp_vec_d_begin(w))) // ' ' // &
      d(cpp_iter_d_deref(cpp_iter_d_op_add(cpp_vec_d_end(w), -2_8))))
    call emit('iterCmp', b(cpp_iter_d_op_eq(inc, cpp_iter_d_op_add(cpp_vec_d_begin(w), 1_8))) // &
      b(cpp_iter_d_op_eq(cpp_vec_d_begin(w), cpp_vec_d_end(w))) // &
      b(cpp_iter_d_op_eq(cpp_vec_d_end(w), cpp_iter_d_op_add(cpp_vec_d_begin(w), 4_8))))
    bi = cpp_iter_d_new_ta_l(arrd([7.0_8, 8.0_8, 9.0_8]), 1_8)
    line = d(cpp_iter_d_deref(bi)) // ' ' // d(cpp_iter_d_deref(cpp_iter_d_op_add(bi, 1_8)))
    bi = cpp_iter_d_op_inc(bi)
    t = cpp_iter_d_clone(bi)
    call emit('bareIter', line // ' ' // d(cpp_iter_d_deref(bi)) // ' ' // d(cpp_iter_d_deref(t)))
    fromit = cpp_ptr_d_from(cpp_iter_d_op_add(cpp_vec_d_begin(w), 2_8))
    call emit('ptrFromIter', d(cpp_ptr_d_deref(fromit)) // ' ' // n8(cpp_ptr_d_o(fromit)))
    wi => vnew_i([4, 5, 6, 7])
    ii = cpp_iter_i_op_sub_itert_l(cpp_vec_i_end(wi), 2_8)
    call emit('iterI', n4(ii%a%a(ii%i)) // ' ' // n8(cpp_iter_i_op_sub_itert_itert(cpp_vec_i_end(wi), cpp_vec_i_begin(wi))) // ' ' // &
      n8(cpp_iter_i_op_sub_itert_itert(cpp_iter_i_op_add(cpp_vec_i_begin(wi), 3_8), cpp_vec_i_begin(wi))) // ' ' // &
      n8(iii(cpp_iter_i_clone(ii))))
    bs => cpp_vec_b_new_0(); call cpp_vec_b_push_back(bs, 3_1); call cpp_vec_b_push_back(bs, 4_1)
    bb = cpp_vec_b_begin(bs)
    call emit('iterB', n4(int(bb%a%a(ibi(cpp_iter_b_op_add(bb, 1_8))), 4)) // ' ' // &
      b(cpp_iter_b_op_eq(cpp_iter_b_op_add(bb, 2_8), cpp_vec_b_end(bs))) // b(cpp_iter_b_op_eq(bb, cpp_iter_b_clone(bb))))
    ao => cpp_arr_o_new(3_8); ao%a(0)%p => S('a'); ao%a(1)%p => S('b'); ao%a(2)%p => S('c')
    so => cpp_vec_o_new_ta(ao, cpp_a_mk_str, 2)
    oi = cpp_vec_o_begin(so); oi = cpp_iter_o_op_inc(oi)
    sl => cpp_iter_o_deref(oi)
    line = ss(str_of(sl%p))
    sl => cpp_iter_o_deref(cpp_iter_o_op_add(oi, 1_8))
    line = line // ' ' // ss(str_of(sl%p)) // ' ' // b(cpp_iter_o_op_eq(cpp_iter_o_op_add(oi, 2_8), cpp_vec_o_end(so))) // &
      b(cpp_iter_o_op_eq(cpp_iter_o_clone(oi), oi))
    sl => cpp_iter_o_deref(cpp_iter_o_clone(oi))
    call emit('iterO', line // ' ' // ss(str_of(sl%p)))
    ! C#'s var rb1 = ++rb steps rb as well
    rb = cpp_vec_o_rbegin(so); rb = cpp_reviter_o_op_inc(rb); rb1 = rb; rb1 = cpp_reviter_o_op_inc(rb1); rb2 = rb1
    rb2 = cpp_reviter_o_op_inc(rb2); rb3 = rb2
    sl => cpp_reviter_o_deref(cpp_vec_o_rbegin(so))
    line = ss(str_of(sl%p))
    sl => cpp_reviter_o_deref(rb)
    call emit('revIter', line // ' ' // ss(str_of(sl%p)) // ' ' // b(cpp_reviter_o_op_eq(cpp_vec_o_rbegin(so), cpp_vec_o_rend(so))) // &
      b(cpp_reviter_o_op_eq(rb3, cpp_vec_o_rend(so))))
    ao => cpp_arr_o_new(1_8); ao%a(0)%p => S('a')
    sp%a => ao; sp%o = 0; sp%n = 1
    sp2%a => ao; sp2%o = 1; sp2%n = 0
    call emit('span', b(cpp_cspan_o_empty(sp0)) // b(cpp_cspan_o_empty(sp)) // b(cpp_cspan_o_empty(sp2)))
  end subroutine iter

  subroutine str()
    class(cpp_str), pointer :: s_, t, g, h, v, a, a2, r, f, e0, x, src, lat
    class(cpp_arr_b), pointer :: hb
    class(cpp_arr_l), pointer :: ia
    class(holder), pointer :: hd
    class(at_kl), pointer :: akl
    class(cpp_refat_l), pointer :: ak
    class(*), pointer :: ob
    type(cpp_cptr) :: c1, c2
    type(cpp_ptr_l) :: idx, fi, nullp
    type(cpp_ptr_b) :: p1, p2
    type(cpp_iter_b) :: bi
    integer(1), pointer :: bp
    integer(8), pointer :: q
    real(8) :: dv
    character(len=:), allocatable :: lens, line
    integer :: i
    integer(8), parameter :: NPOS = -1_8
    s_ => S('Hello, World')
    e0 => cpp_str_new_0()
    call emit('size', u8(cpp_str_size(s_)) // ' ' // b(cpp_str_empty(s_)) // ' ' // b(cpp_str_empty(e0)))
    call emit('find', u8(cpp_str_find_cptr_ul(s_, lit('o'), 0_8)) // ' ' // u8(cpp_str_find_i_ul(s_, ichar('o'), 5_8)) // ' ' // &
      u8(cpp_str_find_i_ul(s_, ichar('z'), 0_8)) // ' ' // u8(cpp_str_find_cptr_ul(s_, lit('World'), 0_8)) // ' ' // &
      u8(cpp_str_find_cptr_ul(s_, lit('World'), 8_8)) // ' ' // u8(cpp_str_find_cptr_ul(s_, lit('d'), 11_8)) // ' ' // &
      u8(cpp_str_find_i_ul(s_, ichar('d'), 12_8)))
    call emit('findEmpty', u8(cpp_str_find_cptr_ul(s_, lit(''), 12_8)) // ' ' // u8(cpp_str_find_cptr_ul(s_, lit(''), 13_8)) // ' ' // &
      u8(cpp_str_find_cptr_ul(s_, lit(''), 0_8)))
    call emit('rfind', u8(cpp_str_rfind_i_ul(s_, ichar('o'), NPOS)) // ' ' // u8(cpp_str_rfind_cptr_ul(s_, lit('o'), 5_8)) // ' ' // &
      u8(cpp_str_rfind_cptr_ul(s_, lit('l'), NPOS)) // ' ' // u8(cpp_str_rfind_cptr_ul(s_, lit('Hello, World!'), NPOS)) // ' ' // &
      u8(cpp_str_rfind_i_ul(e0, ichar('a'), NPOS)) // ' ' // u8(cpp_str_rfind_cptr_ul(s_, lit('H'), 0_8)) // ' ' // &
      u8(cpp_str_rfind_i_ul(s_, ichar('l'), 2_8)) // ' ' // u8(cpp_str_rfind_cptr_ul(s_, lit(''), 3_8)) // ' ' // &
      u8(cpp_str_rfind_cptr_ul(s_, lit(''), NPOS)))
    call emit('firstOf', u8(cpp_str_find_first_of_cptr_ul(s_, lit(', '), 0_8)) // ' ' // &
      u8(cpp_str_find_first_not_of_cptr_ul(s_, lit('Hel'), 0_8)) // ' ' // u8(cpp_str_find_first_not_of_i_ul(s_, ichar('H'), 0_8)) // &
      ' ' // u8(cpp_str_find_first_of_cptr_ul(s_, lit('xyz'), 0_8)) // ' ' // u8(cpp_str_find_first_of_cptr_ul(s_, lit('lo'), 4_8)) // &
      ' ' // u8(cpp_str_find_first_not_of_cptr_ul(s_, lit('Helo, Wrd'), 0_8)) // ' ' // &
      u8(cpp_str_find_first_not_of_i_ul(s_, ichar('d'), 11_8)))
    call emit('lastOf', u8(cpp_str_find_last_not_of_cptr_ul(s_, lit('dl'), NPOS)) // ' ' // u8(cpp_str_find_last_of_i_ul(s_, ichar('o'), 6_8)) &
      // ' ' // u8(cpp_str_find_last_of_i_ul(s_, ichar('W'), NPOS)) // ' ' // &
      u8(cpp_str_find_last_not_of_cptr_ul(s_, lit('Helo, Wrd'), NPOS)) // ' ' // u8(cpp_str_find_last_not_of_cptr_ul(s_, lit('x'), 3_8)) &
      // ' ' // u8(cpp_str_find_last_not_of_cptr_ul(e0, lit('x'), NPOS)) // ' ' // u8(cpp_str_find_last_of_i_ul(e0, ichar('x'), NPOS)))
    call emit('substr', ss(cpp_str_substr(s_, 7_8, NPOS)) // ss(cpp_str_substr(s_, 0_8, 5_8)) // ss(cpp_str_substr(s_, 12_8, 3_8)) // &
      ss(cpp_str_substr(s_, 3_8, 0_8)))
    x => cpp_str_substr(s_, 13_8, 1_8); call catch_oor('substrErr')
    x => cpp_str_substr(s_, NPOS, 1_8); call catch_oor('substrErr2')
    t => cpp_str_clone(s_)
    x => cpp_str_erase(t, 5_8, 2_8); call emit('erase', ss(t))
    x => cpp_str_erase(t, 3_8, NPOS); call emit('eraseAll', ss(t))
    x => cpp_str_erase(t, 0_8, 0_8); line = ss(t)
    call emit('eraseNone', line // ' ' // ss(cpp_str_erase(t, 1_8, 1_8)))
    x => cpp_str_new_cptr_ul(lit(achar(255)), 1_8)
    call emit('compare', n4(cpp_str_compare_ul_ul_str(S('abc'), 0_8, 3_8, S('abd'))) // ' ' // &
      n4(cpp_str_compare_ul_ul_str(S('ab'), 0_8, 9_8, S('abc'))) // ' ' // n4(cpp_str_compare_ul_ul_str(S('abc'), 0_8, 3_8, S('ab'))) // &
      ' ' // n4(cpp_str_compare_ul_ul_str(x, 0_8, 1_8, S('a'))) // ' ' // n4(cpp_str_compare_ul_ul_str(S('abc'), 1_8, 2_8, S('bc'))) // &
      ' ' // n4(cpp_str_compare_ul_ul_str(s_, 7_8, 5_8, S('World'))) // ' ' // n4(cpp_str_compare_ul_ul_str(S('xa'), 1_8, 1_8, S('b'))))
    i = cpp_str_compare_ul_ul_str(S('ab'), 3_8, 1_8, S('x')); call catch_oor('compareErr')
    call emit('starts', b(cpp_str_starts_with_cptr(s_, lit('Hell'))) // b(cpp_str_starts_with_str(s_, S('ello'))) // &
      b(cpp_str_starts_with_cptr(s_, lit(''))) // b(cpp_str_starts_with_str(s_, S('Hello, World!'))) // &
      b(cpp_str_starts_with_str(s_, S('Hello, World'))))
    call emit('cat', ss(cpp_str_cat_str_cptr(s_, lit('!'))) // ss(cpp_str_cat_cptr_str(lit('<'), s_)) // ss(cpp_str_cat_str_str(s_, s_)) &
      // ss(cpp_str_cat_str_i(s_, ichar('?'))) // ss(cpp_str_cat_str_str(cpp_str_new_0(), cpp_str_new_0())))
    g => cpp_str_new_0(); lens = ''
    do i = 0, 39
      call cpp_str_push_back(g, ichar('a') + mod(i, 26))
      lens = lens // n4(size(g%b%a)) // ' '
    end do
    call emit('grow', lens // ss(g))
    c1 = cpp_str_c_str(g)
    call emit('cstr', b(associated(c1%b, g%b)) // ' ' // n4(int(g%b%a(g%n), 4)) // ' ' // cs(cpp_cptr_op_add_cptr_i(c1, 38)))
    c2 = cpp_str_data(g); call emit('data', b(cpp_cptr_op_eq(c2, c1)) // ' ' // n4(c2%o))
    h => cpp_str_new_0(); call cpp_str_movefrom(h, g); call emit('move', ss(h) // ss(g) // ' ' // n4(size(g%b%a)))
    call cpp_str_movefrom(h, h); call emit('moveSelf', ss(h))
    call cpp_str_assign_cptr(h, lit('assigned')); call emit('assignC', ss(h) // ' ' // n4(size(h%b%a)))
    call cpp_str_assign_str(h, S('xy')); call emit('assignS', ss(h) // ' ' // n4(size(h%b%a)))
    call cpp_str_assign_str(h, h); call emit('assignSelf', ss(h))
    hb => h%b; call cpp_str_assign_str(h, S('0123456789'))
    call emit('assignGrow', ss(h) // ' ' // n4(size(h%b%a)) // ' ' // b(associated(hb, h%b)))
    x => null()
    call emit('eq', b(cpp_str_eqlit(h, lit('0123456789'))) // b(cpp_str_eqlit(h, lit('012345678'))) // b(cpp_str_eqlit(h, lit('0123456788'))) &
      // b(cpp_str_equals_str(h, S('0123456789'))) // b(cpp_str_equals_str(h, x)) // b(cpp_str_equals_str(h, S('0123456780'))) // &
      b(cpp_str_eqlit(cpp_str_new_0(), lit(''))))
    v => S('  view  '); call cpp_str_remove_prefix(v, 2_8); call emit('view', ss(v) // ' ' // n4(int(v%b%a(v%n), 4)))
    a => cpp_str_new_0()
    hb => cpp_arr_b_new(4_8); hb%a = [ichar('q', 1), ichar('r', 1), ichar('s', 1), ichar('t', 1)]
    p1 = cpp_ptr_b_new_ta_l(hb, 1_8)
    p2 = cpp_ptr_b_new_ta_l(cpp_arr_b_new(4_8), 3_8)
    x => cpp_str_assign_ptrc_ptrc(a, p1, p2); call emit('assignPtr', ss(a) // ' ' // n4(size(a%b%a)))
    a2 => cpp_str_new_0()
    x => cpp_str_assign_cptr_ul(a2, cpp_cptr_op_add_cptr_i(lit('abcdef'), 1), 3_8)
    call emit('assignCl', ss(x) // ' ' // n4(size(a2%b%a)))
    x => cpp_str_append_str(a2, S('0123456')); x => cpp_str_append_cptr(x, lit('ABCDEF'))
    x => cpp_str_append_cptr_ul(x, lit('xyz'), 2_8); x => cpp_str_append_i(x, ichar('!'))
    call emit('append', ss(a2) // ' ' // n4(size(a2%b%a)))
    x => cpp_str_append_str(a2, a2); call emit('appendSelf', ss(a2))
    x => cpp_str_append_cptr(a2, cpp_cptr_op_add_cptr_i(cpp_str_c_str(a2), 3)); call emit('appendSelfC', ss(a2))
    line = ss(cpp_str_new_ul_i(5_8, ichar('-'))) // ss(cpp_str_new_cptr_ul(lit('abcdef'), 3_8)) // ss(cpp_str_new_str(S('abcdef'))) // &
      ss(cpp_str_from(lit('conv'))) // ss(cpp_str_new_ul_i(0_8, ichar('x')))
    x => cpp_str_new_ul_i(5_8, ichar('-')); line = line // ' ' // n4(size(x%b%a))
    x => cpp_str_new_cptr(lit('abc')); line = line // ' ' // n4(size(x%b%a))
    x => cpp_str_new_0(); call emit('ctors', line // ' ' // n4(size(x%b%a)))
    bi = cpp_str_begin(s_)
    call emit('strIter', achar(int(bi%a%a(bi%i), 4)) // ' ' // n8(ibi(cpp_str_end(s_)) - bi%i) // ' ' // &
      b(cpp_iter_b_op_eq(cpp_iter_b_op_add(bi, 12_8), cpp_str_end(s_))) // b(cpp_iter_b_op_eq(cpp_iter_b_clone(bi), bi)))
    r => S('abc'); call cpp_str_resize(r, 6_8, ichar('.')); call emit('resize', ss(r) // ' ' // n4(size(r%b%a)))
    call cpp_str_resize(r, 2_8, ichar('z')); call emit('resizeDown', ss(r))
    call cpp_str_resize(r, 4_8, 0); call emit('resizeNul', hexb(r%b, r%n))
    call cpp_str_reserve(r, 50_8); call emit('reserve', ss(r) // ' ' // n4(size(r%b%a)))
    call cpp_str_reserve(r, 3_8); call emit('reserve2', n4(size(r%b%a)))
    call cpp_str_clear(r); call emit('clear', ss(r) // ' ' // u8(cpp_str_size(r)) // ' ' // n4(size(r%b%a)))
    f => S('front'); bp => cpp_str_front(f); bp = ichar('F', 1); bp => cpp_str_back(f); bp = ichar('T', 1)
    call emit('frontBack', ss(f) // ' ' // achar(int(cpp_str_front(f), 4)) // achar(int(cpp_str_back(f), 4)))
    call cpp_str_pop_back(f); call cpp_str_pop_back(f); call emit('pop', ss(f))
    x => cpp_str_new_0()
    call emit('toStringFn', '<' // cpp_str_tostring(S('plain text')) // '>' // '<' // cpp_str_tostring(x) // '>')
    ia => cpp_arr_l_new(1_8)
    idx = cpp_ptr_l_new_ta_l(ia, 0_8)
    ! (with part C's stub, strtod converts nothing and these throw: the exception is dropped, as are the lines)
    dv = cpp_str_stod(S('  12.5e1xyz'), idx); cpp_exc => null(); line = d(dv) // ' ' // u8(ia%a(0))
    dv = cpp_str_stod(S('-0.25'), nullp); cpp_exc => null(); line = line // ' ' // d(dv)
    dv = cpp_str_stod(S('1e400'), idx); cpp_exc => null(); line = line // ' ' // d(dv) // ' ' // u8(ia%a(0))
    dv = cpp_str_stod(S('0x1p-2'), idx); cpp_exc => null(); line = line // ' ' // d(dv) // ' ' // u8(ia%a(0))
    call emit('stod', line)
    dv = cpp_str_stod(S('abc'), nullp)
    ob => cpp_exc; cpp_exc => null()
    select type (ob)
    class is (cpp_invalidargument)
      call emit('stodErr', cs(cpp_cppexception_what(ob)))
    class default
      call emit('stodErr', 'NO EXCEPTION')
    end select
    allocate(hd); allocate(akl); ak => akl; ob => hd
    fi = cpp_ptr_l_new_o_refatt(ob, ak)
    dv = cpp_str_stod(S('3.5 '), fi); cpp_exc => null(); call emit('stodField', u8(hd%kl))
    src => null(); lat => null(); q => null()
  end subroutine str

  subroutine tostr()
    real(8) :: ds(32)
    character(len=:), allocatable :: line
    integer :: i
    call emit('toStringI', ss(cpp_str_to_string_i(-42)) // ss(cpp_str_to_string_i(0)) // ss(cpp_str_to_string_i(-huge(1) - 1)) // &
      ss(cpp_str_to_string_i(huge(1))))
    call emit('toStringL', ss(cpp_str_to_string_l(-huge(1_8) - 1_8)) // ss(cpp_str_to_string_l(huge(1_8))) // ss(cpp_str_to_string_l(-1_8)))
    call emit('toStringU', ss(cpp_str_to_string_u(-1)) // ss(cpp_str_to_string_u(0)) // ss(cpp_str_to_string_u(-huge(1) - 1)))
    call emit('toStringUL', ss(cpp_str_to_string_ul(-1_8)) // ss(cpp_str_to_string_ul(0_8)) // ss(cpp_str_to_string_ul(-huge(1_8) - 1_8)) // &
      ss(cpp_str_to_string_ul(-8446744073709551616_8)))
    ds = [3.14159_8, -0.0_8, 0.0_8, 1e20_8, 2.5e-7_8, 5e-7_8, 1.5e-6_8, 2.5e-6_8, 0.0000015_8, 1e300_8, -1e300_8, &
      transfer(1_8, 1.0_8), transfer(3_8, 1.0_8), 123.456_8, 999999.9999995_8, 0.1_8, 0.5_8, 1.5_8, 2.5_8, -2.5e-7_8, -1e-7_8, &
      transfer(-2251799813685248_8, 1.0_8), transfer(-2251799813685248_8, 1.0_8), transfer(9218868437227405312_8, 1.0_8), &
      transfer(-4503599627370496_8, 1.0_8), huge(1.0_8), 4503599627370497.5_8, 0.000001_8, 0.0000005000000000000001_8, &
      1e15_8 + 0.3_8, 2.0000005_8, 2.0000015_8]
    line = ''
    do i = 1, size(ds)
      line = line // ss(cpp_str_to_string_d(ds(i)))
    end do
    call emit('toStringD', line)
  end subroutine tostr

  subroutine cptr()
    type(cpp_cptr) :: p, q, q2, z1, z2, fromc, fromuc
    class(cpp_arr_b), pointer :: buf, pb
    type(cpp_ptr_b) :: dflt
    buf => cpp_arr_b_new(8_8)
    p%b => buf; p%o = 0
    q = cpp_cptr_op_add_cptr_i(p, 3)
    q2 = cpp_cptr_op_add_cptr_l(p, 5_8)
    z2%o = 5
    call emit('cptrArith', n8(cpp_cptr_diff(q, p)) // ' ' // n8(cpp_cptr_diff(p, q2)) // ' ' // b(cpp_cptr_op_lt(p, q)) // &
      b(cpp_cptr_op_lt(q, p)) // b(cpp_cptr_op_eq(q, cpp_cptr_op_add_cptr_l(p, 3_8))) // &
      b(cpp_cptr_op_ne(p, cptr_new(cpp_arr_b_new(8_8), 0))) // b(cpp_cptr_op_eq(z1, z2)) // b(cpp_cptr_isnull(z1)) // &
      b(cpp_cptr_isnull(p)) // b(cpp_cptr_op_ne(q, q2)) // b(cpp_cptr_op_eq(cpp_cptr_clone(q), q)) // ' ' // &
      n4(co(cpp_cptr_op_add_cptr_i(q, -2))) // ' ' // n4(co(cpp_cptr_op_add_cptr_l(q, 4_8))))
    pb => cpp_arr_b_new(4_8); pb%a = [97_1, 98_1, 99_1, 0_1]
    fromc = cpp_cptr_from_ptrc(cpp_ptr_b_new_ta_l(pb, 1_8))
    pb => cpp_arr_b_new(4_8); pb%a = [97_1, 98_1, 99_1, 0_1]
    fromuc = cpp_cptr_from_ptruc(cpp_ptr_b_new_ta_l(pb, 2_8))
    call emit('fromPtr', cs(fromc) // ' ' // cs(fromuc) // ' ' // n4(fromuc%o) // ' ' // b(cpp_cptr_isnull(cpp_cptr_from_ptruc(dflt))))
  end subroutine cptr

  function cptr_new(bb, o) result(p)
    class(cpp_arr_b), pointer, intent(in) :: bb
    integer(4), intent(in) :: o
    type(cpp_cptr) :: p
    p%b => bb
    p%o = o
  end function cptr_new

  subroutine ptr()
    class(cpp_arr_d), pointer :: arr
    type(cpp_ptr_d) :: p, pi, q, f1, f2, f3, f4, f5, f6, dflt
    type(cpp_ptr_i) :: ip, fk, fk2, dflti
    type(cpp_ptr_l) :: fl, lp
    type(cpp_ptr_o) :: ps, dflto
    type(cpp_ptr_b) :: pbb
    type(cpp_ptr_f) :: pf
    type(cpp_ptr_s) :: psh
    type(cpp_ptr_z) :: pz
    class(holder), pointer :: h, h2
    class(at_x), pointer :: ax
    class(at_x2), pointer :: ax2
    class(at_k), pointer :: akk
    class(at_kl), pointer :: akl
    class(cpp_refat_d), pointer :: at, at2
    class(cpp_refat_i), pointer :: ati
    class(cpp_refat_l), pointer :: atl
    class(*), pointer :: ob, nob
    class(cpp_arr_i), pointer :: ia
    class(cpp_arr_l), pointer :: la
    class(cpp_arr_o), pointer :: sa
    class(cpp_arr_b), pointer :: ba
    class(cpp_arr_f), pointer :: fa
    class(cpp_arr_s), pointer :: sha
    class(cpp_arr_z), pointer :: za
    real(8), pointer :: rp
    integer(4), pointer :: ipp
    integer(8), pointer :: lpp
    type(cpp_slot), pointer :: sl
    character(len=:), allocatable :: line
    arr => arrd([1.0_8, 2.0_8, 3.0_8, 4.0_8, 5.0_8])
    p = cpp_ptr_d_new_ta_l(arr, 1_8)
    call emit('ptrIdx', d(cpp_ptr_d_item_i(p, 1)) // ' ' // d(cpp_ptr_d_item_l(p, 2_8)) // ' ' // d(cpp_ptr_d_item_ul(p, 3_8)) // ' ' // &
      d(cpp_ptr_d_deref(p)) // ' ' // d(cpp_ptr_d_deref(cpp_ptr_d_op_add_ptrt_l(p, 3_8))) // ' ' // &
      d(cpp_ptr_d_deref(cpp_ptr_d_op_sub_ptrt_l(p, 1_8))))
    rp => cpp_ptr_d_item_i(p, 0); rp = 20.0_8
    pi = cpp_ptr_d_op_add_ptrt_l(p, 1_8); rp => cpp_ptr_d_deref(pi); rp = 30.0_8
    call emit('ptrSet', d(arr%a(1)) // ' ' // d(arr%a(2)))
    q = cpp_ptr_d_op_add_ptrt_l(p, 2_8)
    call emit('ptrCmp', b(cpp_ptr_d_op_eq(q, cpp_ptr_d_new_ta_l(arr, 3_8))) // b(cpp_ptr_d_op_eq(q, cpp_ptr_d_new_ta_l(cpp_arr_d_new(5_8), 3_8))) &
      // b(cpp_ptr_d_op_eq(dflt, cpp_ptr_d_new_ta_l(null(), 7_8))) // b(cpp_ptr_d_isnull(dflt)) // b(cpp_ptr_d_op_ne(p, q)) // &
      b(cpp_ptr_d_isnull(p)) // b(cpp_ptr_d_op_eq(cpp_ptr_d_op_sub_ptrt_l(q, 2_8), p)))
    allocate(h); h%x = 1.5_8
    allocate(ax); at => ax
    allocate(ax2); at2 => ax2
    ob => h
    f1 = cpp_ptr_d_new_o_refatt(ob, at); f2 = cpp_ptr_d_new_o_refatt(ob, at); f3 = cpp_ptr_d_new_o_refatt(ob, at2)
    allocate(h2); nob => h2
    f4 = cpp_ptr_d_new_o_refatt(nob, at)
    nob => null()
    f5 = cpp_ptr_d_new_o_refatt(nob, at)
    rp => cpp_ptr_d_deref(f1); rp = 2.5_8
    call emit('fieldPtr', d(h%x) // ' ' // d(cpp_ptr_d_deref(f2)) // ' ' // d(cpp_ptr_d_item_i(cpp_ptr_d_op_add_ptrt_l(f1, 3_8), 9)) // ' ' &
      // d(cpp_ptr_d_item_l(f1, 2_8)) // ' ' // d(cpp_ptr_d_item_ul(f1, 5_8)) // ' ' // b(cpp_ptr_d_isnull(f1)))
    f6 = cpp_ptr_d_new_o_refatt(nob, at)
    call emit('fieldEq', b(cpp_ptr_d_op_eq(f1, f2)) // b(cpp_ptr_d_op_eq(f1, f3)) // b(cpp_ptr_d_op_eq(f1, f4)) // &
      b(cpp_ptr_d_op_eq(f1, cpp_ptr_d_op_add_ptrt_l(f2, 1_8))) // b(cpp_ptr_d_op_eq(f1, p)) // b(cpp_ptr_d_op_eq(p, f1)) // &
      b(cpp_ptr_d_op_eq(f5, f6)) // b(cpp_ptr_d_op_ne(f1, f2)) // b(cpp_ptr_d_op_ne(cpp_ptr_d_op_sub_ptrt_l(f1, 1_8), f2)))
    ia => arri([10, 11, 12])
    ip = cpp_ptr_i_new_ta_l(ia, 0_8)
    h%k = 5
    allocate(akk); ati => akk
    fk = cpp_ptr_i_new_o_refatt(ob, ati); ipp => cpp_ptr_i_item_i(fk, 0); ipp = ipp + 1
    fk2 = cpp_ptr_i_new_o_refatt(ob, ati)
    line = n4(cpp_ptr_i_deref(ip)) // ' ' // n4(cpp_ptr_i_item_i(ip, 2)) // ' ' // n4(cpp_ptr_i_item_l(ip, 1_8)) // ' ' // &
      n4(cpp_ptr_i_item_ul(ip, 2_8)) // ' ' // n4(cpp_ptr_i_deref(cpp_ptr_i_op_add_ptrt_l(ip, 1_8))) // ' ' // &
      n4(cpp_ptr_i_deref(cpp_ptr_i_op_sub_ptrt_l(cpp_ptr_i_op_add_ptrt_l(ip, 2_8), 1_8))) // ' ' // &
      b(cpp_ptr_i_op_ne(ip, cpp_ptr_i_op_add_ptrt_l(ip, 1_8))) // b(cpp_ptr_i_op_ne(ip, cpp_ptr_i_new_ta_l(ia, 0_8))) // &
      b(cpp_ptr_i_isnull(dflti)) // b(cpp_ptr_i_isnull(fk)) // ' ' // n4(h%k) // ' ' // n4(cpp_ptr_i_deref(fk)) // ' ' // &
      b(cpp_ptr_i_op_ne(fk, fk2))
    call emit('ptrI', line)
    allocate(akl); atl => akl
    fl = cpp_ptr_l_new_o_refatt(ob, atl); lpp => cpp_ptr_l_item_i(fl, 0); lpp = -1_8
    la => cpp_arr_l_new(2_8); la%a = [1_8, 2_8]
    lp = cpp_ptr_l_new_ta_l(la, 1_8)
    call emit('ptrL', u8(h%kl) // ' ' // u8(cpp_ptr_l_item_i(lp, 0)) // ' ' // u8(cpp_ptr_l_item_i(fl, 3)))
    sa => cpp_arr_o_new(3_8); sa%a(0)%p => S('a'); sa%a(1)%p => S('b'); sa%a(2)%p => S('c')
    ps = cpp_ptr_o_new_ta_l(sa, 0_8)
    sl => cpp_ptr_o_deref(cpp_ptr_o_op_add_ptrt_l(ps, 1_8)); line = ss(str_of(sl%p))
    sl => cpp_ptr_o_item_i(ps, 2); line = line // ss(str_of(sl%p))
    sl => cpp_ptr_o_item_l(ps, 1_8); line = line // ss(str_of(sl%p))
    sl => cpp_ptr_o_item_ul(ps, 2_8); line = line // ss(str_of(sl%p))
    call emit('ptrO', line // ' ' // b(cpp_ptr_o_isnull(dflto)) // b(cpp_ptr_o_isnull(ps)) // &
      b(cpp_ptr_o_op_ne(ps, cpp_ptr_o_op_add_ptrt_l(ps, 1_8))) // b(cpp_ptr_o_op_ne(ps, ps)))
    ba => cpp_arr_b_new(3_8); ba%a = [1_1, 2_1, -6_1]
    pbb = cpp_ptr_b_new_ta_l(ba, 1_8)
    call emit('ptrB', n4(iand(int(cpp_ptr_b_item_i(pbb, 0), 4), 255)) // ' ' // n4(iand(int(cpp_ptr_b_item_i(pbb, 1), 4), 255)) // ' ' // &
      n4(iand(int(cpp_ptr_b_item_i(cpp_ptr_b_op_add_ptrt_l(pbb, 1_8), 0), 4), 255)) // ' ' // &
      n4(iand(int(cpp_ptr_b_item_i(cpp_ptr_b_op_sub_ptrt_l(pbb, 1_8), 0), 4), 255)))
    allocate(fa); allocate(fa%a(0:2)); fa%a = [0.5_4, 1.5_4, 2.5_4]
    pf = cpp_ptr_f_new_ta_l(fa, 0_8)
    call emit('ptrF', df(cpp_ptr_f_item_i(pf, 1)) // ' ' // df(cpp_ptr_f_item_i(cpp_ptr_f_op_add_ptrt_l(pf, 2_8), 0)) // ' ' // &
      df(cpp_ptr_f_item_ul(pf, 2_8)))
    sha => cpp_arr_s_new(2_8); sha%a = [-7_2, 8_2]
    za => cpp_arr_z_new(2_8); za%a = [.false., .true.]
    psh = cpp_ptr_s_new_ta_l(sha, 0_8); pz = cpp_ptr_z_new_ta_l(za, 0_8)
    call emit('ptrSZ', n4(int(cpp_ptr_s_item_i(psh, 0), 4)) // ' ' // n4(int(cpp_ptr_s_item_i(psh, 1), 4)) // ' ' // &
      b(cpp_ptr_z_item_i(pz, 1)) // b(cpp_ptr_z_item_ul(pz, 0_8)) // b(cpp_ptr_z_item_ul(pz, 1_8)))
  end subroutine ptr

  subroutine misc()
    real(8) :: nan
    class(cpp_pair), pointer :: p1, pc, pp, pa, vsp, vsc
    class(cpp_opt_d), pointer :: o, o2, oc, oa, om, t
    class(cpp_opt_o), pointer :: os, osc, osa, ocp, ocpc, tt
    class(cpp_variant), pointer :: va, vc, vc2, vm, vaa
    class(cpp_str), pointer :: x, vstr
    class(cpp_arr_d), pointer :: da, db, fd
    class(cpp_arr_l), pointer :: fl
    class(cpp_arr_i), pointer :: ints, ints2
    class(cpp_arr_o), pointer :: sa, sb, sd, grid, gc, vcp, vcc, vca, m2
    class(cpp_arr_z), pointer :: az
    class(cpp_arr_i), pointer :: m4
    class(fn_s), pointer :: fs
    class(cpp_func_o), pointer :: fo
    class(cpp_object), pointer :: other
    class(cpp_cppexception), pointer :: ce, cd
    class(cpp_vec_d), pointer :: v0
    type(cpp_slot), pointer :: sl
    type(cpp_cptr), pointer :: cp
    type(cpp_cptr) :: cc
    integer(4), pointer :: ip
    real(8), pointer :: rp
    character(len=:), allocatable :: line
    integer :: i
    nan = transfer(-2251799813685248_8, 1.0_8)
    call emit('min', d(cpp_m_min_d_d(0.0_8, -0.0_8)) // ' ' // d(cpp_m_min_d_d(-0.0_8, 0.0_8)) // ' ' // d(cpp_m_max_d_d(-0.0_8, 0.0_8)) // &
      ' ' // d(cpp_m_max_d_d(0.0_8, -0.0_8)) // ' ' // d(cpp_m_min_d_d(nan, 1.0_8)) // ' ' // d(cpp_m_min_d_d(1.0_8, nan)) // ' ' // &
      d(cpp_m_max_d_d(nan, 1.0_8)) // ' ' // d(cpp_m_max_d_d(1.0_8, nan)))
    call emit('clamp', d(cpp_m_clamp_d_d_d(nan, 0.0_8, 1.0_8)) // ' ' // d(cpp_m_clamp_d_d_d(-0.0_8, 0.0_8, 1.0_8)) // ' ' // &
      d(cpp_m_clamp_d_d_d(5.0_8, 0.0_8, 1.0_8)) // ' ' // d(cpp_m_clamp_d_d_d(-5.0_8, 0.0_8, 1.0_8)) // ' ' // &
      n4(cpp_m_clamp_i_i_i(-5, 0, 10)) // ' ' // n4(cpp_m_clamp_i_i_i(15, 0, 10)) // ' ' // n4(cpp_m_clamp_i_i_i(5, 0, 10)))
    call emit('ints', n4(cpp_m_max_i_i(-3, 2)) // ' ' // n4(cpp_m_min_i_i(-3, 2)) // ' ' // n8(cpp_m_max_l_l(-3_8, 2_8)) // ' ' // &
      n8(cpp_m_max_l_l(-huge(1_8) - 1_8, huge(1_8))))
    call emit('unsigned', u8(cpp_m_max_ul_ul(-1_8, 1_8)) // ' ' // u8(cpp_m_min_ul_ul(-1_8, 1_8)) // ' ' // &
      u8(iand(int(cpp_m_max_u_u(-1, 1), 8), 4294967295_8)) // ' ' // u8(iand(int(cpp_m_max_u_u(1, -huge(1) - 1), 8), 4294967295_8)) // &
      ' ' // n4(iand(int(cpp_m_min_uc_uc(-56_1, 100_1), 4), 255)) // ' ' // n4(iand(int(cpp_m_min_uc_uc(100_1, -56_1), 4), 255)) // &
      ' ' // u8(cpp_m_min_ul_ul(-huge(1_8) - 1_8, 5_8)))
    call emit('listMinMax', d(cpp_m_max_da(arrd([1.0_8, nan, 3.0_8]))) // ' ' // d(cpp_m_min_da(arrd([nan, 1.0_8, -2.0_8]))) // ' ' // &
      d(cpp_m_max_da(arrd([-0.0_8, 0.0_8]))) // ' ' // n4(cpp_m_max_ia(arri([4, 9, 2]))) // ' ' // &
      d(cpp_m_min_da(arrd([2.0_8, -0.0_8, 0.0_8]))))
    ! Pair
    p1 => cpp_pair_new_a_b(boxi(1), SO('b'), cpp_a_mk_i, 0, cpp_a_mk_str, 2)
    pc => cpp_pair_clone(p1); x => cpp_str_append_cptr(str_of(pc%second%p), lit('!')); ip => refi(pc%first%p); ip = 5
    call emit('pairClone', n4(refi(p1%first%p)) // ss(str_of(p1%second%p)) // ' ' // n4(refi(pc%first%p)) // ss(str_of(pc%second%p)))
    pp => cpp_pair_new_a_b(SO('x'), SO('y'), cpp_a_mk_str, 2, cpp_a_mk_str, 2)
    pa => cpp_pair_new_a_b(SO(''), SO(''), cpp_a_mk_str, 2, cpp_a_mk_str, 2)
    other => pp
    call pa%assign(other)
    x => cpp_str_append_cptr(str_of(pp%first%p), lit('+'))
    call emit('pairAssign', ss(str_of(pa%first%p)) // ss(str_of(pa%second%p)) // ss(str_of(pp%first%p)))
    pp => cpp_pair_new_0(cpp_a_mk_str, 2, cpp_a_mk_str, 2); x => cpp_str_append_cptr(str_of(pp%first%p), lit('m'))
    pa => cpp_pair_clone(pp); x => cpp_str_append_cptr(str_of(pa%second%p), lit('c'))
    line = ss(str_of(pp%first%p)) // ss(str_of(pp%second%p)) // ss(str_of(pa%first%p)) // ss(str_of(pa%second%p))
    pp => cpp_pair_new_0(cpp_a_mk_i, 0, cpp_a_mk_d, 0)
    line = line // ' ' // n4(refi(pp%first%p)) // ' ' // d(refd(pp%second%p))
    pp => cpp_pair_new_0(cpp_a_mk_null, 3, cpp_a_mk_str, 2)
    call emit('pairMake', line // ' ' // b(.not. associated(pp%first%p)) // ss(str_of(pp%second%p)))
    vsp => cpp_pair_new_a_b(ovd(cpp_vec_d_new_ta(arrd([1.0_8]))), oad(arrd([2.0_8])), mk_vecd, 2, cpp_a_mk_null, 3)
    vsc => cpp_pair_clone(vsp)
    call cpp_vec_d_push_back(vecd_of(vsc%first%p), 3.0_8)
    da => arrd_of(vsc%second%p); da%a(0) = 4.0_8
    v0 => vecd_of(vsp%first%p); line = vd(v0) // ' ' // d(ad_at(vsp%second%p, 0)) // ' | '
    v0 => vecd_of(vsc%first%p)
    call emit('pairDeep', line // vd(v0) // ' ' // d(ad_at(vsc%second%p, 0)))
    ! Opt<double>
    o => cpp_opt_d_new_0()
    call emit('opt', d(cpp_opt_d_value_or(o, 9.0_8)) // ' ' // b(o%has))
    o2 => cpp_opt_d_new_t(3.5_8); oc => cpp_opt_d_clone(o2)
    line = d(cpp_opt_d_value_or(oc, 0.0_8)) // ' ' // b(oc%has)
    t => cpp_opt_d_clone(o); line = line // ' ' // d(cpp_opt_d_value_or(t, -1.0_8))
    t => cpp_opt_d_clone(o)
    call emit('opt2', line // ' ' // b(t%has))
    oa => cpp_opt_d_new_t(1.0_8); call cpp_opt_d_assign(oa, o); call emit('optAssign', b(oa%has) // ' ' // d(oa%val))
    call cpp_opt_d_assign(oa, o2); call emit('optAssign2', b(oa%has) // ' ' // d(oa%val))
    om => cpp_opt_d_new_0(); call cpp_opt_d_movefrom(om, o2); call emit('optMove', b(om%has) // ' ' // d(om%val))
    ! Opt<Str>
    os => cpp_opt_o_new_0(cpp_a_mk_str, 2); call emit('optStr0', b(os%has) // b(.not. associated(os%val%p)))
    call cpp_opt_o_set(os, SO('x')); osc => cpp_opt_o_clone(os); x => cpp_str_append_cptr(str_of(osc%val%p), lit('y'))
    osa => cpp_opt_o_new_0(cpp_a_mk_str, 2); call cpp_opt_o_assign(osa, os); x => cpp_str_append_cptr(str_of(os%val%p), lit('z'))
    call emit('optStr', ss(str_of(os%val%p)) // ss(str_of(osc%val%p)) // ss(str_of(osa%val%p)) // b(osa%has))
    sl => cpp_opt_o_emplace(os, SO('e')); x => cpp_str_append_cptr(str_of(sl%p), lit('!'))
    call emit('optEmplace', ss(str_of(os%val%p)) // b(os%has))
    call cpp_opt_o_reset(os)
    tt => cpp_opt_o_clone(os); line = b(os%has) // b(.not. associated(os%val%p)) // b(tt%has)
    tt => cpp_opt_o_clone(os)
    call emit('optReset', line // b(.not. associated(tt%val%p)))
    call cpp_opt_o_assign(osa, os); call emit('optAssignEmpty', b(osa%has) // b(.not. associated(osa%val%p)))
    ocp => cpp_opt_o_new_0(mk_cptr, 0); call emit('optValue0', b(cpp_cptr_isnull(refc(ocp%val%p))) // b(ocp%has))
    call cpp_opt_o_set(ocp, boxc(cpp_cptr_op_add_cptr_i(lit('abc'), 1))); ocpc => cpp_opt_o_clone(ocp); call cpp_opt_o_reset(ocp)
    call emit('optValue', cs(refc(ocpc%val%p)) // ' ' // b(cpp_cptr_isnull(refc(ocp%val%p))))
    ! Variant
    va => cpp_variant_new_0(); call emit('variant0', n4(va%idx) // ' ' // vval(va))
    call cpp_variant_set_i(va, 42); vc => cpp_variant_clone(va); call cpp_variant_set_d(va, 2.5_8)
    call emit('variant', n4(va%idx) // ' ' // vval(va) // ' ' // n4(vc%idx) // ' ' // vval(vc))
    vstr => S('str'); call cpp_variant_set_str(va, vstr); x => cpp_str_append_cptr(vstr, lit('!')); vc2 => cpp_variant_clone(va)
    x => cpp_str_append_cptr(str_of(va%v), lit('?'))
    call emit('variantStr', n4(va%idx) // ' ' // ss(str_of(va%v)) // ' ' // ss(str_of(vc2%v)))
    vm => cpp_variant_new_0(); call cpp_variant_movefrom(vm, va)
    call emit('variantMove', n4(vm%idx) // ' ' // b(associated(vm%v, va%v)) // ' ' // ss(str_of(vm%v)))
    vaa => cpp_variant_new_0(); call cpp_variant_set_b(vaa, .true.); call cpp_variant_assign(vc, vaa)
    call emit('variantAssign', n4(vc%idx) // ' ' // vval(vc))
    ! Arr
    da => cpp_arr_make__d(3); da%a(1) = nan
    db => cpp_arr_clone__d(da)
    m2 => cpp_arr_make2__i(2, 3)
    line = b(cpp_arr_eq__d(da, db)) // ' ' // b(.not. associated(db, da)) // ' ' // b(cpp_arr_eq__d(da, cpp_arr_d_new(3_8))) // &
      b(cpp_arr_eq__d(da, cpp_arr_d_new(2_8))) // ' ' // n4(ai_len(m2%a(1)%p))
    m2 => cpp_arr_make2__d(3, 1)
    az => cpp_arr_make__z(2)
    m4 => cpp_arr_make__i(4)
    call emit('arrD', line // ' ' // n4(size(m2%a)) // ' ' // n4(size(m4%a)) // ' ' // n8(al_at(cpp_arr_make__l(2), 1)) // &
      ' ' // b(az%a(0)))
    allocate(fs); fo => fs
    sa => cpp_arr_makewith__o(2, fo, cpp_a_mk_str, 2)
    sb => cpp_arr_clone__o(sa, cpp_a_mk_str, 2); x => cpp_str_append_cptr(str_of(sb%a(0)%p), lit('!'))
    sd => cpp_arr_make__o(2, cpp_a_mk_str, 2); call cpp_arr_assign__o(sd, sa, cpp_a_mk_str, 2)
    x => cpp_str_append_cptr(str_of(sa%a(1)%p), lit('?'))
    m2 => cpp_arr_make2__o(2, 2, cpp_a_mk_str, 2)
    call emit('arrStr', ss(str_of(sa%a(0)%p)) // ss(str_of(sb%a(0)%p)) // ss(str_of(sd%a(1)%p)) // ss(str_of(sa%a(1)%p)) // ' ' // &
      ss(str_of(ao_slot(m2%a(1)%p, 1))))
    grid => cpp_arr_o_new(2_8); grid%a(0)%p => arrd([1.0_8, 2.0_8]); grid%a(1)%p => arrd([3.0_8, 4.0_8])
    gc => cpp_arr_clone__o(grid, cpp_a_mk_null, 3); da => arrd_of(gc%a(0)%p); da%a(0) = 9.0_8
    call emit('arrShallow', d(ad_at(grid%a(0)%p, 0)))
    vcp => cpp_arr_o_new(2_8); vcp%a(0)%p => boxc(lit('ab')); vcp%a(1)%p => boxc(lit('cd'))
    vcc => cpp_arr_clone__o(vcp, mk_cptr, 0); cp => refc(vcc%a(0)%p); cp = cpp_cptr_op_add_cptr_i(cp, 1)
    call emit('arrValues', cs(refc(vcp%a(0)%p)) // cs(refc(vcc%a(0)%p)))
    vca => cpp_arr_o_new_mk(2_8, mk_cptr); call cpp_arr_assign__o(vca, vcp, mk_cptr, 0)
    call emit('arrAssignValues', cs(refc(vca%a(1)%p)))
    da => null()
    call emit('arrNull', b(.not. associated(cpp_arr_clone__d(da))))
    ints => arri([1, 2, 3]); ints2 => cpp_arr_i_new(3_8); call cpp_arr_assign__i(ints2, ints); ints%a(0) = 7
    call emit('arrAssignI', n4(ints2%a(0)) // ' ' // n4(ai_at(cpp_arr_clone__i(ints), 0)))
    fd => cpp_arr_d_new(3_8); call cpp_array_fill_d(fd, -0.5_8); fl => cpp_arr_l_new(2_8); call cpp_array_fill_l(fl, -huge(1_8) - 1_8)
    call emit('fill', d(fd%a(2)) // ' ' // n8(fl%a(1)))
    ! exceptions
    line = exc_line(cpp_runtimeerror_new_str(S('runtime')))
    line = line // exc_line(cpp_invalidargument_new_str(S('inv')))
    call emit('exc', line // exc_line(cpp_outofrange_new_str(S('oor'))))
    vstr => S('msg'); ce => cpp_runtimeerror_new_str(vstr); x => cpp_str_append_cptr(vstr, lit('!'))
    call emit('excCopy', cs(cpp_cppexception_what(ce)))
    allocate(cd); call cpp_cppexception_ctor_0(cd)
    call emit('excDefault', cs(cpp_cppexception_what(cd)))
    rp => null(); cc = cpp_cptr(); i = 0
  end subroutine misc

  function exc_line(e) result(s)
    class(cpp_cppexception), pointer, intent(in) :: e
    character(len=:), allocatable :: s
    logical :: lg, rt, oo
    lg = .false.; rt = .false.; oo = .false.
    select type (e)
    class is (cpp_logicerror)
      lg = .true.
    end select
    select type (e)
    class is (cpp_runtimeerror)
      rt = .true.
    end select
    select type (e)
    class is (cpp_outofrange)
      oo = .true.
    end select
    s = cs(cpp_cppexception_what(e)) // '|' // b(lg) // b(rt) // b(oo) // ' '
  end function exc_line

  function arri_of(x) result(r)
    class(*), pointer, intent(in) :: x
    class(cpp_arr_i), pointer :: r
    r => null()
    select type (x)
    type is (cpp_arr_i)
      r => x
    end select
  end function arri_of

  subroutine extra()
    class(cpp_arr_b), pointer :: ab, ab2, abc
    class(cpp_arr_d), pointer :: ad, ad2
    class(cpp_arr_l), pointer :: al, al2, alc
    class(cpp_arr_s), pointer :: ash, ash2, ashc
    class(cpp_arr_z), pointer :: az, az2, azc
    class(cpp_arr_f), pointer :: af
    class(holder), pointer :: hs
    class(at_s), pointer :: as1, as2
    class(cpp_refat_o), pointer :: r1, r2
    class(*), pointer :: ob
    type(cpp_ptr_o) :: fs, fs2
    type(cpp_slot), pointer :: sl
    class(cpp_str), pointer :: x
    character(len=:), allocatable :: line, before
    ab => cpp_arr_b_new(2_8); ab%a = [1_1, -56_1]; ab2 => cpp_arr_b_new(3_8); call cpp_arr_assign__b(ab2, ab)
    abc => cpp_arr_clone__b(ab); abc%a(0) = 9_1
    ad => arrd([1.5_8, -0.0_8]); ad2 => cpp_arr_d_new(2_8); call cpp_arr_assign__d(ad2, ad)
    al => cpp_arr_l_new(2_8); al%a = [-huge(1_8) - 1_8, 3_8]; al2 => cpp_arr_l_new(2_8); call cpp_arr_assign__l(al2, al)
    alc => cpp_arr_clone__l(al)
    ash => cpp_arr_s_new(2_8); ash%a = [-5_2, 7_2]; ash2 => cpp_arr_s_new(2_8); call cpp_arr_assign__s(ash2, ash)
    ashc => cpp_arr_clone__s(ash)
    az => cpp_arr_z_new(2_8); az%a = [.true., .false.]; az2 => cpp_arr_z_new(2_8); call cpp_arr_assign__z(az2, az)
    azc => cpp_arr_clone__z(az)
    af => cpp_arr_f_new(3_8)
    line = n4(iand(int(ab2%a(0), 4), 255)) // ' ' // n4(iand(int(ab2%a(1), 4), 255)) // ' ' // n4(iand(int(ab2%a(2), 4), 255)) // &
      ' ' // n4(int(abc%a(0), 4)) // n4(int(ab%a(0), 4)) // ' ' // d(ad2%a(0)) // ' ' // d(ad2%a(1)) // ' ' // n8(al2%a(0)) // ' ' // &
      n8(alc%a(1)) // ' ' // n4(int(ash2%a(0), 4)) // ' ' // n4(int(ashc%a(1), 4)) // ' ' // b(az2%a(0)) // b(az2%a(1)) // &
      b(azc%a(0)) // ' ' // n4(size(af%a)) // ' ' // df(af%a(2))
    call emit('arrKinds', line)
    allocate(hs); hs%s%p => SO('field')
    allocate(as1); r1 => as1; allocate(as2); r2 => as2
    ob => hs
    fs = cpp_ptr_o_new_o_refatt(ob, r1)
    sl => cpp_ptr_o_deref(fs); x => cpp_str_append_cptr(str_of(sl%p), lit('!'))
    before = ss(str_of(hs%s%p))
    sl => cpp_ptr_o_item_i(fs, 3); sl%p => SO('new')
    fs2 = cpp_ptr_o_new_o_refatt(ob, r2)
    sl => cpp_ptr_o_item_i(fs, 0)
    call emit('ptrOField', before // ss(str_of(hs%s%p)) // ' ' // b(cpp_ptr_o_isnull(fs)) // b(cpp_ptr_o_op_ne(fs, fs2)) // ' ' // &
      ss(str_of(sl%p)))
  end subroutine extra

  subroutine nums()
    integer(8) :: us(11), xs(9), ys(7)
    integer(4) :: ux(5), uy(5)
    real(8) :: ds(9)
    character(len=:), allocatable :: line
    integer :: i, j
    us = [0_8, 1_8, 9007199254740993_8, 9223372036854775807_8, -huge(1_8) - 1_8, -9223372036854774784_8, -9223372036854774783_8, &
      -1_8, -1024_8, -2048_8, -6101065172474983726_8]
    line = ''
    do i = 1, size(us)
      line = line // d(cpp_u2d(us(i))) // ' '
    end do
    call emit('u2d', line)
    xs = [0_8, 1_8, 7_8, 10_8, 9223372036854775807_8, -huge(1_8) - 1_8, -1_8, -2_8, -6101065172474983726_8]
    ys = [1_8, 3_8, 10_8, 9223372036854775807_8, -huge(1_8) - 1_8, -1_8, 4294967296_8]
    line = ''
    do i = 1, size(xs)
      do j = 1, size(ys)
        line = line // u8(cpp_udiv8(xs(i), ys(j))) // ',' // u8(cpp_urem8(xs(i), ys(j))) // ' '
      end do
    end do
    call emit('udiv', line)
    ux = [0, 5, -1, -huge(1) - 1, 123456789]
    uy = [1, 2, 7, -huge(1) - 1, -1]
    line = ''
    do i = 1, size(ux)
      do j = 1, size(uy)
        line = line // u8(iand(int(cpp_urem4(ux(i), uy(j)), 8), 4294967295_8)) // ' '
      end do
    end do
    call emit('urem4', line)
    ds = [0.0_8, -0.0_8, 1.0_8, transfer(-2251799813685248_8, 1.0_8), transfer(-2251799813685248_8, 1.0_8), &
      transfer(9218868437227405312_8, 1.0_8), transfer(-4503599627370496_8, 1.0_8), huge(1.0_8), transfer(1_8, 1.0_8)]
    line = ''
    do i = 1, size(ds)
      line = line // b(cpp_double_isnan(ds(i))) // b(cpp_double_isinfinity(ds(i))) // b(cpp_double_isfinite(ds(i))) // ' '
    end do
    call emit('dtests', line)
    call emit('nanBits', d(transfer(-2251799813685248_8, 1.0_8)) // ' ' // d(transfer(-2251799813685248_8, 1.0_8)))
  end subroutine nums
  ! ---- the differential fuzz of corecheck.cs (the same generator, operations and hash)
  function rnd() result(r)
    integer(8) :: r
    rs = rs * 6364136223846793005_8 + 1442695040888963407_8
    r = ishft(rs, -33)
  end function rnd
  function rn(n) result(r)
    integer(4), intent(in) :: n
    integer(4) :: r
    r = int(mod(rnd(), int(n, 8)), 4)
  end function rn
  function hh(h, v) result(r)
    integer(8), intent(in) :: h, v
    integer(8) :: r
    r = ieor(h, v) * 1099511628211_8
  end function hh
  function pick() result(t)
    character(len=:), allocatable :: t
    select case (rn(5))
    case (0)
      t = 'a'
    case (1)
      t = 'bc'
    case (2)
      t = ''
    case (3)
      t = 'cde'
    case default
      t = 'eab'
    end select
  end function pick

  subroutine fuzz()
    class(cpp_vec_i), pointer :: v, w, m
    class(cpp_str), pointer :: s1, s2, t, x
    class(cpp_vec_o), pointer :: vs1, vs2, c
    class(fn_str), pointer :: fstr
    class(cpp_func_o), pointer :: fo
    type(cpp_iter_i) :: it
    type(cpp_iter_o) :: io
    type(cpp_cptr) :: set
    integer(8) :: h, r, cc, lp, pp
    integer(4) :: step, op, xx, p, q, a, b2, i, j, ch, k
    character(len=:), allocatable :: line
    h = -3750763034362895579_8
    line = ''
    v => cpp_vec_i_new_0(); w => cpp_vec_i_new_0()
    do step = 0, 3999
      op = rn(15)
      select case (op)
      case (0, 1)
        xx = rn(1000) - 500; call cpp_vec_i_push_back(v, xx)
      case (2)
        p = rn(v%n + 1); xx = rn(100); it = cpp_vec_i_insert_itert_t(v, cpp_iter_i_op_add(cpp_vec_i_begin(v), int(p, 8)), xx)
      case (3)
        if (v%n > 0) then
          p = rn(v%n); it = cpp_vec_i_erase_itert(v, cpp_iter_i_op_add(cpp_vec_i_begin(v), int(p, 8)))
        end if
      case (4)
        p = rn(v%n + 1); q = p + rn(v%n - p + 1)
        it = cpp_vec_i_erase_itert_itert(v, cpp_iter_i_op_add(cpp_vec_i_begin(v), int(p, 8)), &
          cpp_iter_i_op_add(cpp_vec_i_begin(v), int(q, 8)))
      case (5)
        p = rn(v%n + 1); a = rn(w%n + 1); b2 = a + rn(w%n - a + 1)
        it = cpp_vec_i_insert_itert_itert_itert(v, cpp_iter_i_op_add(cpp_vec_i_begin(v), int(p, 8)), &
          cpp_iter_i_op_add(cpp_vec_i_begin(w), int(a, 8)), cpp_iter_i_op_add(cpp_vec_i_begin(w), int(b2, 8)))
      case (6)
        call cpp_vec_i_resize_ul(v, int(rn(30), 8))
      case (7)
        cc = rn(30); xx = rn(9); call cpp_vec_i_resize_ul_t(v, cc, xx)
      case (8)
        cc = rn(12); xx = rn(50); call cpp_vec_i_assign_ul_t(v, cc, xx)
      case (9)
        call cpp_vec_i_reserve(v, int(rn(60), 8))
      case (10)
        call cpp_vec_i_shrink_to_fit(v)
      case (11)
        call cpp_vec_i_swap(v, w)
      case (12)
        call cpp_vec_i_assign_vect(w, v)
      case (13)
        w => cpp_vec_i_clone(v)
      case (14)
        if (rn(5) == 0) then
          call cpp_vec_i_clear(v)
        else
          m => cpp_vec_i_new_0(); call cpp_vec_i_movefrom_vect(m, v); call cpp_vec_i_movefrom_vect(w, m)
        end if
      end select
      h = hh(h, cpp_vec_i_size(v)); h = hh(h, cpp_vec_i_capacity(v)); h = hh(h, cpp_vec_i_size(w)); h = hh(h, cpp_vec_i_capacity(w))
      do i = 0, v%n - 1
        h = hh(h, int(v%a%a(i), 8))
      end do
      if (mod(step, 1000) == 999) line = line // hx(h) // ' '
    end do
    call emit('fuzzVec', line // b(cpp_vec_i_eq(v, w)))
    s1 => cpp_str_new_0(); s2 => S('abc')
    h = -3750763034362895579_8
    line = ''
    do step = 0, 3999
      op = rn(16)
      r = 0
      select case (op)
      case (0, 1)
        call cpp_str_push_back(s1, ichar('a') + rn(5))
      case (2)
        x => cpp_str_append_cptr(s1, lit(pick()))
      case (3)
        if (s1%n > 0) then
          p = rn(s1%n + 1); lp = rn(4); x => cpp_str_erase(s1, int(p, 8), lp)
        end if
      case (4)
        cc = rn(25); ch = ichar('a') + rn(5); call cpp_str_resize(s1, cc, ch)
      case (5)
        if (s1%n > 0) call cpp_str_remove_prefix(s1, int(rn(s1%n + 1), 8))
      case (6)
        call cpp_str_assign_cptr(s1, lit(pick()))
      case (7)
        ch = ichar('a') + rn(5); pp = rn(s1%n + 2); r = cpp_str_find_i_ul(s1, ch, pp)
      case (8)
        ch = ichar('a') + rn(5)
        if (rn(3) == 0) then
          pp = -1_8
        else
          pp = rn(s1%n + 2)
        end if
        r = cpp_str_rfind_i_ul(s1, ch, pp)
      case (9)
        set = lit(pick()); pp = rn(s1%n + 2); r = cpp_str_find_first_of_cptr_ul(s1, set, pp)
      case (10)
        set = lit(pick())
        if (rn(3) == 0) then
          pp = -1_8
        else
          pp = rn(s1%n + 2)
        end if
        r = cpp_str_find_last_not_of_cptr_ul(s1, set, pp)
      case (11)
        set = lit(pick()); pp = rn(s1%n + 2); r = cpp_str_find_cptr_ul(s1, set, pp)
      case (12)
        set = lit(pick())
        if (rn(3) == 0) then
          pp = -1_8
        else
          pp = rn(s1%n + 2)
        end if
        r = cpp_str_rfind_cptr_ul(s1, set, pp)
      case (13)
        p = rn(s1%n + 1); lp = rn(6); r = cpp_str_compare_ul_ul_str(s1, int(p, 8), lp, s2)
      case (14)
        r = merge(1_8, 0_8, cpp_str_starts_with_str(s1, s2)); pp = rn(s1%n + 1); lp = rn(5); s2 => cpp_str_substr(s1, pp, lp)
      case (15)
        t => cpp_str_clone(s1); x => cpp_str_append_str(t, s2); call cpp_str_movefrom(s1, t)
      end select
      h = hh(h, r); h = hh(h, int(s1%n, 8)); h = hh(h, int(size(s1%b%a), 8))
      do i = 0, s1%n - 1
        h = hh(h, int(iand(int(s1%b%a(i), 4), 255), 8))
      end do
      if (mod(step, 1000) == 999) line = line // hx(h) // ' '
    end do
    call emit('fuzzStr', line // ss(s1))
    allocate(fstr); fo => fstr
    vs1 => cpp_vec_o_new_funct(fo, cpp_a_mk_str, 2); vs2 => cpp_vec_o_new_0(cpp_a_mk_str, 2)
    h = -3750763034362895579_8
    line = ''
    do step = 0, 2999
      op = rn(9)
      select case (op)
      case (0)
        call cpp_vec_o_push_back(vs1, SO(pick()))
      case (1)
        p = rn(vs1%n + 1)
        io = cpp_vec_o_insert_itert_t(vs1, cpp_iter_o_op_add(cpp_vec_o_begin(vs1), int(p, 8)), SO(pick()))
      case (2)
        if (vs1%n > 0) then
          p = rn(vs1%n); io = cpp_vec_o_erase_itert(vs1, cpp_iter_o_op_add(cpp_vec_o_begin(vs1), int(p, 8)))
        end if
      case (3)
        call cpp_vec_o_resize_ul(vs1, int(rn(12), 8))
      case (4)
        if (vs1%n > 0) then
          k = rn(vs1%n); x => cpp_str_append_cptr(str_of(vs1%a%a(k)%p), lit(pick()))
        end if
      case (5)
        call cpp_vec_o_assign_vect(vs2, vs1)
      case (6)
        p = rn(vs1%n + 1); a = rn(vs2%n + 1); b2 = a + rn(vs2%n - a + 1)
        io = cpp_vec_o_insert_itert_itert_itert(vs1, cpp_iter_o_op_add(cpp_vec_o_begin(vs1), int(p, 8)), &
          cpp_iter_o_op_add(cpp_vec_o_begin(vs2), int(a, 8)), cpp_iter_o_op_add(cpp_vec_o_begin(vs2), int(b2, 8)))
      case (7)
        c => cpp_vec_o_clone(vs1); call cpp_vec_o_movefrom_vect(vs2, c)
      case (8)
        p = rn(vs1%n + 1); q = p + rn(vs1%n - p + 1)
        io = cpp_vec_o_erase_itert_itert(vs1, cpp_iter_o_op_add(cpp_vec_o_begin(vs1), int(p, 8)), &
          cpp_iter_o_op_add(cpp_vec_o_begin(vs1), int(q, 8)))
      end select
      h = hh(h, cpp_vec_o_size(vs1)); h = hh(h, cpp_vec_o_capacity(vs1)); h = hh(h, cpp_vec_o_size(vs2))
      do i = 0, vs1%n - 1
        x => str_of(vs1%a%a(i)%p)
        h = hh(h, int(x%n, 8))
        do j = 0, x%n - 1
          h = hh(h, int(iand(int(x%b%a(j), 4), 255), 8))
        end do
      end do
      if (mod(step, 1000) == 999) line = line // hx(h) // ' '
    end do
    call emit('fuzzVecStr', line // b(cpp_vec_o_eq(vs1, vs2)))
  end subroutine fuzz
end program corecheck
