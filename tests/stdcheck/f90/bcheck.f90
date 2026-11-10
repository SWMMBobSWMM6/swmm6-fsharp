! bcheck.f90 - the cases of bcheck.cpp through part B of the Fortran runtime: the members sortcheck, hashcheck and
! containercheck leave out (UMapC with an engine hasher and key-equal called through the hash_of / call2 bindings,
! UMap from a Pair array, Assign and Clone of UMap / UMapC / Map / Set / Que, Map find / empty, key copies, an
! identity-hashed USet, and the Algo members over Str, engine objects, bytes, bools, floats, doubles and Ptr). The
! output must equal bcheck.cpp's byte for byte.
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
module bcheck_m
  use cpp_runtime
  use chk_util
  implicit none
  ! the engine's CiHash and CiEqual: case-insensitive, called through the hash_of and call2 bindings
  type, extends(cpp_object) :: cih_t
  contains
    procedure :: hash_of => cih_hash_of
  end type
  type, extends(cpp_object) :: cie_t
  contains
    procedure :: call2 => cie_call2
  end type
  type, extends(cpp_object) :: obj_t
    integer(4) :: v = 0
  end type
  type, extends(cpp_object) :: day_t
    real(8) :: day = 0.0_8
    integer(4) :: id = 0
  end type
  type, extends(cpp_func_o_z) :: f_g5
  contains
    procedure :: invoke => f_g5_invoke
  end type
  type, extends(cpp_func_o_z) :: f_empty
  contains
    procedure :: invoke => f_empty_invoke
  end type
  type, extends(cpp_func_o_z) :: f_lt6
  contains
    procedure :: invoke => f_lt6_invoke
  end type
  type, extends(cpp_func_o_z) :: f_alpha
  contains
    procedure :: invoke => f_alpha_invoke
  end type
  type, extends(cpp_func_o_d_z) :: f_day
  contains
    procedure :: invoke => f_day_invoke
  end type
  type, extends(cpp_func_b_b) :: f_upper
  contains
    procedure :: invoke => f_upper_invoke
  end type
  type, extends(cpp_func_b_b) :: f_plus1
  contains
    procedure :: invoke => f_plus1_invoke
  end type
  type, extends(cpp_func_b_z) :: f_dash
  contains
    procedure :: invoke => f_dash_invoke
  end type
  type, extends(cpp_func_z_z) :: f_id
  contains
    procedure :: invoke => f_id_invoke
  end type
  type, extends(cpp_func_d_z) :: f_ltm1
  contains
    procedure :: invoke => f_ltm1_invoke
  end type
  type, extends(cpp_func_d_z) :: f_gt5
  contains
    procedure :: invoke => f_gt5_invoke
  end type
contains
  recursive function lower(c) result(r)
    integer(1), intent(in) :: c
    integer(1) :: r
    r = c
    if (c >= 65_1 .and. c <= 90_1) r = c + 32_1
  end function

  recursive function cih_hash_of(self, k) result(zres)
    class(cih_t), intent(inout), target :: self
    class(*), pointer, intent(in) :: k
    integer(8) :: zres
    class(cpp_arr_b), pointer :: t
    integer(4) :: i
    zres = 0_8
    select type (s => k)
    class is (cpp_str)
      t => cpp_arr_b_new(int(s%n + 1, 8))
      do i = 0, s%n - 1
        t%a(i) = lower(s%b%a(i))
      end do
      zres = cpp_stdhash_hashbytes(t, 0, s%n, 3339675911_8)
    end select
  end function

  recursive function cie_call2(self, a, b) result(zres)
    class(cie_t), intent(inout), target :: self
    class(*), pointer, intent(in) :: a, b
    logical :: zres
    integer(4) :: i
    zres = .false.
    select type (x => a)
    class is (cpp_str)
      select type (y => b)
      class is (cpp_str)
        if (x%n /= y%n) return
        do i = 0, x%n - 1
          if (lower(x%b%a(i)) /= lower(y%b%a(i))) return
        end do
        zres = .true.
      end select
    end select
  end function

  recursive function mk_cih() result(zres)
    class(*), pointer :: zres
    allocate(cih_t :: zres)
  end function
  recursive function mk_cie() result(zres)
    class(*), pointer :: zres
    allocate(cie_t :: zres)
  end function

  recursive function f_g5_invoke(self, a1) result(zres)
    class(f_g5), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    logical :: zres
    character(len=:), allocatable :: t
    t = strtext(a1)
    zres = .false.
    if (len(t) == 5) zres = t(1:1) == 'g'
  end function
  recursive function f_empty_invoke(self, a1) result(zres)
    class(f_empty), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    logical :: zres
    zres = len(strtext(a1)) == 0
  end function
  recursive function f_lt6_invoke(self, a1) result(zres)
    class(f_lt6), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    logical :: zres
    zres = len(strtext(a1)) < 6
  end function
  recursive function f_alpha_invoke(self, a1) result(zres)
    class(f_alpha), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    logical :: zres
    zres = strtext(a1) == 'alpha'
  end function
  recursive function f_day_invoke(self, a1, a2) result(zres)
    class(f_day), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    real(8), value :: a2
    logical :: zres
    zres = .false.
    select type (d => a1)
    type is (day_t)
      zres = d%day < a2
    end select
  end function
  recursive function f_upper_invoke(self, a1) result(zres)
    class(f_upper), intent(inout), target :: self
    integer(1), value :: a1
    integer(1) :: zres
    zres = a1
    if (a1 >= 97_1 .and. a1 <= 122_1) zres = a1 - 32_1
  end function
  recursive function f_plus1_invoke(self, a1) result(zres)
    class(f_plus1), intent(inout), target :: self
    integer(1), value :: a1
    integer(1) :: zres
    zres = a1 + 1_1
  end function
  recursive function f_dash_invoke(self, a1) result(zres)
    class(f_dash), intent(inout), target :: self
    integer(1), value :: a1
    logical :: zres
    zres = a1 == 45_1
  end function
  recursive function f_id_invoke(self, a1) result(zres)
    class(f_id), intent(inout), target :: self
    logical, value :: a1
    logical :: zres
    zres = a1
  end function
  recursive function f_ltm1_invoke(self, a1) result(zres)
    class(f_ltm1), intent(inout), target :: self
    real(8), value :: a1
    logical :: zres
    zres = a1 < -1.0_8
  end function
  recursive function f_gt5_invoke(self, a1) result(zres)
    class(f_gt5), intent(inout), target :: self
    real(8), value :: a1
    logical :: zres
    zres = a1 > 5.0_8
  end function

  ! the Str s now holds t (the same object, a new buffer: as std::string's assignment changes the string itself)
  subroutine setstr(s, t)
    class(cpp_str), intent(inout) :: s
    character(len=*), intent(in) :: t
    integer :: i
    s%b => cpp_arr_b_new(int(len(t) + 1, 8))
    do i = 1, len(t)
      s%b%a(i - 1) = int(iachar(t(i:i)), 1)
    end do
    s%n = len(t)
  end subroutine

  subroutine dumpci(tag, m)
    character(len=*), intent(in) :: tag
    class(cpp_hashtable), intent(inout) :: m
    type(cpp_mit) :: it
    class(cpp_pair), pointer :: p
    call put(tag // ' ' // ustr(cpp_hashtable_bucket_count(m)) // ' ' // ustr(cpp_hashtable_size(m)) // ':')
    it = cpp_hashtable_begin(m)
    do while (.not. cpp_mit_op_eq(it, cpp_hashtable_end(m)))
      p => mitpair(it)
      call put(' ' // strtext(p%first%p) // '=' // i4str(geti(p%second%p)))
      it = cpp_mit_op_inc(it)
    end do
    call nl()
  end subroutine

  subroutine dumpmap(tag, m)
    character(len=*), intent(in) :: tag
    class(cpp_map), intent(inout) :: m
    type(cpp_mit) :: it
    class(cpp_pair), pointer :: p
    call put(tag)
    it = cpp_map_begin(m)
    do while (.not. cpp_mit_op_eq(it, cpp_map_end(m)))
      p => mitpair(it)
      call put(' ' // strtext(p%first%p) // '=' // i4str(geti(p%second%p)))
      it = cpp_mit_op_inc(it)
    end do
  end subroutine

  subroutine dumpset(tag, m)
    character(len=*), intent(in) :: tag
    class(cpp_set), intent(inout) :: m
    type(cpp_mit) :: it
    call put(tag)
    it = cpp_set_begin(m)
    do while (.not. cpp_mit_op_eq(it, cpp_set_end(m)))
      call put(' ' // strtext(cpp_mit_deref(it)))
      it = cpp_mit_op_inc(it)
    end do
  end subroutine

  subroutine seti(sl, v)
    type(cpp_slot), pointer, intent(in) :: sl
    integer(4), intent(in) :: v
    select type (x => sl%p)
    type is (integer(4))
      x = v
    end select
  end subroutine

  ! %.2f
  function f2(x) result(s)
    real(8), intent(in) :: x
    character(len=:), allocatable :: s
    integer(8) :: h
    character(len=2) :: fr
    h = nint(abs(x) * 100.0_8, 8)
    write(fr, '(i2.2)') mod(h, 100_8)
    s = istr(h / 100_8) // '.' // fr
    if (x < 0.0_8) s = '-' // s
  end function

  function f1(x) result(s)
    real(8), intent(in) :: x
    character(len=:), allocatable :: s
    integer(8) :: h
    h = nint(abs(x) * 10.0_8, 8)
    s = istr(h / 10_8) // '.' // istr(mod(h, 10_8))
    if (x < 0.0_8) s = '-' // s
  end function

  function ito(a, i) result(it)
    class(cpp_arr_o), pointer, intent(in) :: a
    integer(4), intent(in) :: i
    type(cpp_iter_o) :: it
    it = cpp_iter_o()
    it%a => a
    it%i = int(i, 8)
  end function
  function itd(a, i) result(it)
    class(cpp_arr_d), pointer, intent(in) :: a
    integer(4), intent(in) :: i
    type(cpp_iter_d) :: it
    it = cpp_iter_d()
    it%a => a
    it%i = int(i, 8)
  end function
  function itb(a, i) result(it)
    class(cpp_arr_b), pointer, intent(in) :: a
    integer(4), intent(in) :: i
    type(cpp_iter_b) :: it
    it = cpp_iter_b()
    it%a => a
    it%i = int(i, 8)
  end function
  function itf(a, i) result(it)
    class(cpp_arr_f), pointer, intent(in) :: a
    integer(4), intent(in) :: i
    type(cpp_iter_f) :: it
    it = cpp_iter_f()
    it%a => a
    it%i = int(i, 8)
  end function
  function itz(a, i) result(it)
    class(cpp_arr_z), pointer, intent(in) :: a
    integer(4), intent(in) :: i
    type(cpp_iter_z) :: it
    it = cpp_iter_z()
    it%a => a
    it%i = int(i, 8)
  end function
  function iti(a, i) result(it)
    class(cpp_arr_i), pointer, intent(in) :: a
    integer(4), intent(in) :: i
    type(cpp_iter_i) :: it
    it = cpp_iter_i()
    it%a => a
    it%i = int(i, 8)
  end function
  function ptd(a, o) result(p)
    class(cpp_arr_d), pointer, intent(in) :: a
    integer(4), intent(in) :: o
    type(cpp_ptr_d) :: p
    p = cpp_ptr_d()
    p%a => a
    p%o = int(o, 8)
  end function
end module bcheck_m

program bcheck
  use bcheck_m
  implicit none
  character(len=4), parameter :: pre(0:9) = [character(len=4) :: 'Node', 'NODE', 'node', 'Link', 'LINK', 'Out', 'out', &
                                              'RG', 'rg', 'X']
  character(len=7), parameter :: qs(7) = [character(len=7) :: 'node1', 'NODE17', 'Link5', 'x0', 'nothing', 'OUT12', &
                                          'rg299']
  character(len=5), parameter :: fruit(5) = [character(len=5) :: 'pear', 'apple', 'fig', 'apple', 'kiwi']
  character(len=5), parameter :: words(0:5) = [character(len=5) :: 'alpha', 'beta', '', 'gamma', 'beta', 'delta']
  real(8), parameter :: dq(7) = [-1.0_8, 0.0_8, 1.5_8, 2.0_8, 7.5_8, 18.0_8, 30.0_8]
  class(cpp_umapc), pointer :: cm, cc, ca
  class(cpp_umap), pointer :: um, ub
  class(cpp_map), pointer :: mm, mc, ma
  class(cpp_set), pointer :: st, sc, sa
  class(cpp_que), pointer :: qq, qc, qa
  class(cpp_uset), pointer :: us
  class(cpp_func_o_l), pointer :: noh
  class(cpp_func_o_o_z), pointer :: noe
  class(cpp_func_o), pointer :: nov
  class(cpp_func_o_z), pointer :: fo
  class(cpp_func_o_d_z), pointer :: fday
  class(cpp_func_b_b), pointer :: fbb
  class(cpp_func_b_z), pointer :: fbz
  class(cpp_func_z_z), pointer :: fzz
  class(cpp_func_d_z), pointer :: fdz
  class(cpp_pair), pointer :: pr
  class(cpp_str), pointer :: ks, ts, us2
  class(cpp_arr_o), pointer :: il, v, days, objs
  class(cpp_arr_d), pointer :: a, b, raw, dd
  class(cpp_arr_b), pointer :: cb
  class(cpp_arr_f), pointer :: fl
  class(cpp_arr_z), pointer :: bz, zs
  class(cpp_arr_i), pointer :: iv
  class(cpp_vec_o), pointer :: w
  class(cpp_vec_d), pointer :: vd
  class(*), pointer :: x
  type(cpp_slot), pointer :: sl
  type(cpp_mit) :: it, it2
  type(cpp_iter_o) :: io
  type(cpp_iter_d) :: id
  type(cpp_iter_z) :: iz
  type(cpp_ptr_d) :: p, p2
  integer(8) :: r, dummy
  integer(4) :: round, n, i, q, added
  noh => null()
  noe => null()
  nov => null()
  chk_s = 777_8

  ! ---- unordered_map with the engine's hasher and key-equal
  do round = 0, 5
    cm => cpp_umapc_new_funcv(nov, mk_str, 2, mk_i, 0, mk_cih, 2, mk_cie, 2)
    if (round < 3) then
      n = 1 + int(umod(rnd(), 40_8))
    else
      n = 1 + int(umod(rnd(), 900_8))
    end if
    do i = 0, n - 1
      r = rnd()
      ks => mkstr(trim(pre(umod(r, 10_8))) // istr(umod(ishft(r, -8), 300_8)))
      x => ks
      if (umod(r, 3_8) == 0_8) then
        sl => cpp_umap_index(cm, x)
        call seti(sl, geti(sl%p) + i)
      else
        pr => cpp_umap_emplace_k_v(cm, x, boxi(i))
      end if
      if (umod(r, 7_8) == 0_8) it = cpp_hashtable_erase_mite(cm, cpp_hashtable_begin(cm))
    end do
    call dumpci('ci', cm)
    cc => cpp_umapc_clone(cm)
    call dumpci('ci copy', cc)
    ca => cpp_umapc_new_funcv(nov, mk_str, 2, mk_i, 0, mk_cih, 2, mk_cie, 2)
    ks => mkstr('zz')
    x => ks
    sl => cpp_umap_index(ca, x)
    call seti(sl, 1)
    call cpp_umapc_assign(ca, cm)
    call dumpci('ci assign', ca)
    call put('ci find:')
    do q = 1, 7
      ks => mkstr(trim(qs(q)))
      x => ks
      it = cpp_hashtable_find(cm, x)
      it2 = cpp_mit_clone(it)
      if (cpp_mit_op_eq(it2, cpp_hashtable_end(cm))) then
        call put(' ' // trim(qs(q)) // ':end')
      else
        pr => mitpair(it2)
        call put(' ' // trim(qs(q)) // ':' // strtext(pr%first%p))
      end if
      call put('/' // ustr(cpp_hashtable_count(cm, x)) // '/' // i4str(merge(1, 0, cpp_hashtable_contains(cm, x))))
    end do
    call nl()
  end do

  ! ---- unordered_map from an initializer list; Assign
  il => cpp_arr_o_new(7_8)
  il%a(0)%p => cpp_pair_new_a_b(boxi(3), boxi(30), mk_i, 0, mk_i, 0)
  il%a(1)%p => cpp_pair_new_a_b(boxi(1), boxi(10), mk_i, 0, mk_i, 0)
  il%a(2)%p => cpp_pair_new_a_b(boxi(3), boxi(99), mk_i, 0, mk_i, 0)
  il%a(3)%p => cpp_pair_new_a_b(boxi(7), boxi(70), mk_i, 0, mk_i, 0)
  il%a(4)%p => cpp_pair_new_a_b(boxi(-2), boxi(5), mk_i, 0, mk_i, 0)
  il%a(5)%p => cpp_pair_new_a_b(boxi(14), boxi(140), mk_i, 0, mk_i, 0)
  il%a(6)%p => cpp_pair_new_a_b(boxi(1), boxi(11), mk_i, 0, mk_i, 0)
  um => cpp_umap_new_pairkva_funckul_funckkb(il, noh, noe, mk_i, 0, mk_i, 0)
  call put('il ' // ustr(cpp_hashtable_bucket_count(um)) // ' ' // ustr(cpp_hashtable_size(um)) // ':')
  call dumpi(um)
  ub => cpp_umap_new_funckul_funckkb_funcv(noh, noe, nov, mk_i, 0, mk_i, 0)
  sl => cpp_umap_index(ub, boxi(5))
  call seti(sl, 1)
  call cpp_umap_assign(ub, um)
  sl => cpp_umap_index(ub, boxi(8))
  call seti(sl, 80)
  call put(' | assign ' // ustr(cpp_hashtable_bucket_count(ub)) // ' ' // ustr(cpp_hashtable_size(ub)) // ':')
  call dumpi(ub)
  call nl()

  ! ---- std::map, std::set: copies, assignment, find, empty; keys that stay the container's own
  mm => cpp_map_new_funckkb_funcv(noe, nov, mk_str, 2, mk_i, 0)
  call put('map empty ' // i4str(merge(1, 0, cpp_map_empty(mm))))
  call nl()
  ks => mkstr('b')
  x => ks
  sl => cpp_map_index(mm, x)
  call seti(sl, 1)
  call setstr(ks, 'bx')
  pr => cpp_map_emplace_k_v(mm, x, boxi(2))
  ks => mkstr('a')
  x => ks
  pr => cpp_map_emplace_k_v(mm, x, boxi(3))
  ks => mkstr('b')
  x => ks
  pr => cpp_map_emplace_k_v(mm, x, boxi(4))
  mc => cpp_map_clone(mm)
  ks => mkstr('c')
  x => ks
  sl => cpp_map_index(mc, x)
  call seti(sl, 5)
  ma => cpp_map_new_funckkb_funcv(noe, nov, mk_str, 2, mk_i, 0)
  ks => mkstr('q')
  x => ks
  sl => cpp_map_index(ma, x)
  call seti(sl, 9)
  call cpp_map_assign(ma, mc)
  ks => mkstr('a')
  x => ks
  sl => cpp_map_index(ma, x)
  call seti(sl, 7)
  call dumpmap('map:', mm)
  call dumpmap(' | copy:', mc)
  call dumpmap(' | assign:', ma)
  ks => mkstr('bx')
  x => ks
  it = cpp_map_find(mm, x)
  if (cpp_mit_op_eq(it, cpp_map_end(mm))) then
    call put(' | find end')
  else
    pr => mitpair(it)
    call put(' | find ' // strtext(pr%first%p))
  end if
  ks => mkstr('zz')
  x => ks
  call put(' ' // i4str(merge(1, 0, cpp_mit_op_eq(cpp_map_find(mm, x), cpp_map_end(mm)))) // ' ' // &
           i4str(merge(1, 0, cpp_map_empty(mm))))
  call nl()
  st => cpp_set_new_functtb(noe, mk_str, 2)
  do i = 1, 5
    ks => mkstr(trim(fruit(i)))
    x => ks
    pr => cpp_set_insert(st, x)
  end do
  sc => cpp_set_clone(st)
  ks => mkstr('date')
  x => ks
  pr => cpp_set_insert(sc, x)
  sa => cpp_set_new_functtb(noe, mk_str, 2)
  ks => mkstr('zz')
  x => ks
  pr => cpp_set_insert(sa, x)
  call cpp_set_assign(sa, sc)
  call dumpset('set:', st)
  call dumpset(' | copy:', sc)
  call dumpset(' | assign:', sa)
  call nl()

  ! ---- std::queue copies
  qq => cpp_que_new(mk_i, 0)
  do i = 0, 199
    call cpp_que_push(qq, boxi(i * 3))
    if (mod(i, 3) == 2) call cpp_que_pop(qq)
  end do
  qc => cpp_que_clone(qq)
  qa => cpp_que_new(mk_i, 0)
  call cpp_que_push(qa, boxi(-1))
  call cpp_que_assign(qa, qq)
  call cpp_que_pop(qq)
  sl => cpp_que_front(qq)
  call put('queue ' // ustr(cpp_que_size(qq)) // ' ' // i4str(geti(sl%p)))
  sl => cpp_que_front(qc)
  call put(' | copy ' // ustr(cpp_que_size(qc)) // ' ' // i4str(geti(sl%p)))
  sl => cpp_que_back(qc)
  call put(' ' // i4str(geti(sl%p)))
  sl => cpp_que_front(qa)
  call put(' | assign ' // ustr(cpp_que_size(qa)) // ' ' // i4str(geti(sl%p)))
  call nl()

  ! ---- an unordered_set of objects: identity
  objs => cpp_arr_o_new(50_8)
  do i = 0, 49
    allocate(obj_t :: objs%a(i)%p)
  end do
  us => cpp_uset_new_functul_functtb(noh, noe, mk_none, 1)
  added = 0
  do i = 0, 199
    pr => cpp_uset_insert(us, objs%a(mod(i * 7, 50))%p)
    if (getz(pr%second%p)) added = added + 1
  end do
  x => null()
  call put('ptrset ' // ustr(cpp_hashtable_size(us)) // ' ' // i4str(added) // ' ' // &
           ustr(cpp_hashtable_count(us, objs%a(3)%p)) // ' ' // ustr(cpp_hashtable_count(us, x)))
  call nl()

  ! ---- fill over bytes, floats, bools, ints; any_of and accumulate over a double array's begin / end
  cb => cpp_arr_b_new(6_8)
  cb%a(0:4) = [97_1, 98_1, 99_1, 100_1, 101_1]
  call cpp_algo_fill_itert_itert_t__b(itb(cb, 1), itb(cb, 3), 122_1)
  fl => cpp_arr_f_new(4_8)
  fl%a = [1.0_4, 2.0_4, 3.0_4, 4.0_4]
  call cpp_algo_fill_itert_itert_t__f(itf(fl, 2), itf(fl, 4), 0.5_4)
  bz => cpp_arr_z_new(4_8)
  bz%a = .false.
  call cpp_algo_fill_itert_itert_t__z(itz(bz, 1), itz(bz, 2), .true.)
  iv => cpp_arr_i_new(5_8)
  iv%a = [1, 2, 3, 4, 5]
  call cpp_algo_fill_itert_itert_t__i(iti(iv, 0), iti(iv, 2), -9)
  dd => cpp_arr_d_new(5_8)
  dd%a = [0.5_8, 1.5_8, -2.0_8, 3.0_8, 4.0_8]
  call put('fills ')
  do i = 0, 4
    call put(achar(cb%a(i)))
  end do
  call put(' ' // f1(real(fl%a(2), 8)) // ' ' // f1(real(fl%a(1), 8)) // ' ')
  do i = 0, 3
    call put(i4str(merge(1, 0, bz%a(i))))
  end do
  call put(' ' // i4str(iv%a(0)) // ' ' // i4str(iv%a(2)))
  allocate(f_ltm1 :: fdz)
  call put(' any ' // i4str(merge(1, 0, cpp_algo_any_of__d(cpp_algo_begin_ta__d(dd), cpp_algo_end_ta__d(dd), fdz))))
  deallocate(fdz)
  allocate(f_gt5 :: fdz)
  call put(' ' // i4str(merge(1, 0, cpp_algo_any_of__d(cpp_algo_begin_ta__d(dd), cpp_algo_end_ta__d(dd), fdz))))
  call put(' sum ' // f1(cpp_algo_accumulate_iterd_iterd_d(cpp_algo_begin_ta__d(dd), cpp_algo_end_ta__d(dd), 0.0_8)))
  call put(' n ' // istr(cpp_iter_d_op_sub_itert_itert(cpp_algo_end_ta__d(dd), cpp_algo_begin_ta__d(dd))))
  call nl()

  ! ---- algorithms
  v => cpp_arr_o_new(6_8)
  do i = 0, 5
    v%a(i)%p => mkstr(trim(words(i)))
  end do
  ks => mkstr('beta')
  x => ks
  io = cpp_algo_find__o(ito(v, 0), ito(v, 6), x, mk_str, 2)
  call put('strs find ' // istr(io%i))
  ks => mkstr('omega')
  x => ks
  io = cpp_algo_find__o(ito(v, 0), ito(v, 6), x, mk_str, 2)
  call put(' ' // istr(io%i))
  allocate(f_g5 :: fo)
  io = cpp_algo_find_if__o(ito(v, 0), ito(v, 6), fo, mk_str, 2)
  call put(' find_if ' // istr(io%i))
  deallocate(fo)
  allocate(f_empty :: fo)
  call put(' any ' // i4str(merge(1, 0, cpp_algo_any_of__o(ito(v, 0), ito(v, 6), fo, mk_str, 2))))
  deallocate(fo)
  allocate(f_lt6 :: fo)
  call put(' all ' // i4str(merge(1, 0, cpp_algo_all_of__o(ito(v, 0), ito(v, 6), fo, mk_str, 2))))
  deallocate(fo)
  allocate(f_alpha :: fo)
  call put(' none ' // i4str(merge(1, 0, cpp_algo_none_of__o(ito(v, 3), ito(v, 6), fo, mk_str, 2))))
  deallocate(fo)
  call nl()
  w => cpp_algo_vecfrom__o(ito(v, 1), ito(v, 4), mk_str, 2)
  select type (s => v%a(1)%p)
  class is (cpp_str)
    call setstr(s, 'changed')
  end select
  call put('vecfrom ' // i4str(w%n) // ':')
  do i = 0, w%n - 1
    call put(' [' // strtext(w%a%a(i)%p) // ']')
  end do
  call nl()
  days => cpp_arr_o_new(40_8)
  do i = 0, 39
    allocate(day_t :: days%a(i)%p)
    select type (d => days%a(i)%p)
    type is (day_t)
      d%day = real(i / 3, 8) * 1.5_8
      d%id = i
    end select
  end do
  allocate(f_day :: fday)
  call put('days:')
  do q = 1, 7
    io = cpp_algo_lower_bound_itert_itert_v_functvb__od(ito(days, 0), ito(days, 40), dq(q), fday, mk_none, 2)
    call put(' ' // istr(io%i))
  end do
  call nl()
  a => cpp_arr_d_new(10_8)
  b => cpp_arr_d_new(10_8)
  do i = 0, 9
    a%a(i) = real(i, 8) * 0.25_8
    b%a(i) = -1.0_8
  end do
  id = cpp_algo_copy_itert_itert_itert__d(itd(a, 2), itd(a, 7), itd(b, 1))
  call cpp_algo_fill_itert_itert_t__d(itd(a, 8), itd(a, 10), 9.5_8)
  raw => cpp_arr_d_new(12_8)
  p = cpp_algo_fill_n_ptrt_ul_t__d(ptd(raw, 2), 3_8, 4.25_8)
  p2 = cpp_algo_copy_n_ptrt_ul_ptrt__d(ptd(a, 6), 4_8, ptd(raw, 6))
  call cpp_algo_fill_ptrt_ptrt_t__d(ptd(raw, 0), ptd(raw, 2), -7.0_8)
  vd => cpp_algo_vecfrom__d(itd(a, 0), itd(a, 4))
  call put('copy ' // istr(id%i) // ' fill_n ' // istr(p%o) // ' copy_n ' // istr(p2%o) // ':')
  do i = 0, 9
    call put(' ' // f2(b%a(i)))
  end do
  call put(' |')
  do i = 0, 9
    call put(' ' // f2(a%a(i)))
  end do
  call put(' |')
  do i = 0, 11
    call put(' ' // f2(raw%a(i)))
  end do
  call put(' | ' // i4str(vd%n) // ' ' // f2(vd%a%a(3)))
  call nl()
  ts => mkstr('Junction_17-out')
  us2 => mkstr(repeat('.', ts%n))
  allocate(f_upper :: fbb)
  iz = cpp_iter_z()
  call put('transform')
  block
    type(cpp_iter_b) :: ib
    ib = cpp_algo_transform_iteruc_iteruc_iteruc_funcucc(itb(ts%b, 0), itb(ts%b, ts%n), itb(us2%b, 0), fbb)
    deallocate(fbb)
    allocate(f_plus1 :: fbb)
    ib = cpp_algo_transform_itert_itert_iteru_functu__bb(itb(ts%b, 0), itb(ts%b, 4), itb(ts%b, 0), fbb)
  end block
  x => us2
  call put(' ' // strtext(x))
  x => ts
  call put(' ' // strtext(x))
  allocate(f_dash :: fbz)
  call put(' any ' // i4str(merge(1, 0, cpp_algo_any_of__b(itb(ts%b, 0), itb(ts%b, ts%n), fbz))))
  zs => cpp_arr_z_new(5_8)
  zs%a = [.false., .false., .true., .false., .false.]
  allocate(f_id :: fzz)
  call put(' ' // i4str(merge(1, 0, cpp_algo_any_of__z(cpp_algo_begin_ta__z(zs), cpp_algo_end_ta__z(zs), fzz))))
  iz = cpp_algo_begin_ta__z(zs)
  iz%i = iz%i + 2_8
  call put(' ' // i4str(merge(1, 0, cpp_algo_any_of__z(cpp_algo_begin_ta__z(zs), iz, fzz))))
  call nl()
  dummy = 0_8

contains

  subroutine dumpi(t)
    class(cpp_hashtable), intent(inout) :: t
    type(cpp_mit) :: jt
    class(cpp_pair), pointer :: pp
    jt = cpp_hashtable_begin(t)
    do while (.not. cpp_mit_op_eq(jt, cpp_hashtable_end(t)))
      pp => mitpair(jt)
      call put(' ' // i4str(geti(pp%first%p)) // '=' // i4str(geti(pp%second%p)))
      jt = cpp_mit_op_inc(jt)
    end do
  end subroutine
end program bcheck
