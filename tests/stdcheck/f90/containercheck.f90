! containercheck.f90 - the cases of containercheck.cpp through part B of the Fortran runtime (f90/runtime/cpp_rt_b.f90);
! the output must equal containercheck.cpp's byte for byte: unordered_map with 64-bit keys, std::hash of integers,
! unordered_set reserve / rehash / clear / erase while iterating, std::map / std::set order and bounds (string and
! pair keys), std::stack / std::queue, partial_sort with middle < last, sort and stable_sort of subranges, std::sort
! with Less<double>.Fn and Less<Pair<int, Str>>.Fn, and the Algo functions. Where the engine calls a member only
! over another element type (count, find and replace over byte, min / max_element, is_sorted, accumulate and
! upper_bound over double), the check runs that member on a copy of the int data converted to that type.
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
module containercheck_m
  use cpp_runtime
  use chk_util
  implicit none
  type :: e_t
    integer(4) :: key = 0
    integer(4) :: id = 0
  end type
  type, extends(cpp_func_o_o_z) :: lt_e
  contains
    procedure :: invoke => lt_e_invoke
  end type
  type, extends(cpp_func_i_z) :: p_gt5
  contains
    procedure :: invoke => p_gt5_invoke
  end type
  type, extends(cpp_func_d_z) :: p_gtm3
  contains
    procedure :: invoke => p_gtm3_invoke
  end type
  type, extends(cpp_func_i_z) :: p_eq6
  contains
    procedure :: invoke => p_eq6_invoke
  end type
  type, extends(cpp_func_i_z) :: p_neg
  contains
    procedure :: invoke => p_neg_invoke
  end type
  type, extends(cpp_func_i_z) :: p_div3
  contains
    procedure :: invoke => p_div3_invoke
  end type
contains
  recursive function lt_e_invoke(self, a1, a2) result(zres)
    class(lt_e), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1, a2
    logical :: zres
    zres = .false.
    select type (x => a1)
    type is (e_t)
      select type (y => a2)
      type is (e_t)
        zres = x%key < y%key
      end select
    end select
  end function
  recursive function p_gt5_invoke(self, a1) result(zres)
    class(p_gt5), intent(inout), target :: self
    integer(4), value :: a1
    logical :: zres
    zres = a1 > 5
  end function
  recursive function p_gtm3_invoke(self, a1) result(zres)
    class(p_gtm3), intent(inout), target :: self
    real(8), value :: a1
    logical :: zres
    zres = a1 > -3.0_8
  end function
  recursive function p_eq6_invoke(self, a1) result(zres)
    class(p_eq6), intent(inout), target :: self
    integer(4), value :: a1
    logical :: zres
    zres = a1 == 6
  end function
  recursive function p_neg_invoke(self, a1) result(zres)
    class(p_neg), intent(inout), target :: self
    integer(4), value :: a1
    logical :: zres
    zres = a1 < 0
  end function
  recursive function p_div3_invoke(self, a1) result(zres)
    class(p_div3), intent(inout), target :: self
    integer(4), value :: a1
    logical :: zres
    zres = mod(a1, 3) == 0
  end function

  function iti(a, i) result(it)
    class(cpp_arr_i), pointer, intent(in) :: a
    integer(4), intent(in) :: i
    type(cpp_iter_i) :: it
    it = cpp_iter_i()
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
  function itl(a, i) result(it)
    class(cpp_arr_l), pointer, intent(in) :: a
    integer(4), intent(in) :: i
    type(cpp_iter_l) :: it
    it = cpp_iter_l()
    it%a => a
    it%i = int(i, 8)
  end function

  subroutine ids(a, n)
    class(cpp_arr_o), pointer, intent(in) :: a
    integer(4), intent(in) :: n
    integer(4) :: i
    call put(':')
    do i = 0, n - 1
      select type (e => a%a(i)%p)
      type is (e_t)
        call put(' ' // i4str(e%id))
      end select
    end do
    call nl()
  end subroutine

  subroutine ints(tag, a, n)
    character(len=*), intent(in) :: tag
    class(cpp_arr_i), pointer, intent(in) :: a
    integer(4), intent(in) :: n
    integer(4) :: i
    call put(tag // ' ' // i4str(n) // ':')
    do i = 0, n - 1
      call put(' ' // i4str(a%a(i)))
    end do
    call nl()
  end subroutine

  ! a copy of the boxed structs
  function copy_e(v, n) result(c)
    class(cpp_arr_o), pointer, intent(in) :: v
    integer(4), intent(in) :: n
    class(cpp_arr_o), pointer :: c
    integer(4) :: i
    c => cpp_arr_o_new(int(n, 8))
    do i = 0, n - 1
      select type (e => v%a(i)%p)
      type is (e_t)
        allocate(c%a(i)%p, source=e)
      end select
    end do
  end function

  ! %.1f of a multiple of 0.5
  function f1(x) result(s)
    real(8), intent(in) :: x
    character(len=:), allocatable :: s
    integer(4) :: h
    h = nint(abs(x) * 2.0_8)
    s = i4str(h / 2) // '.' // i4str(mod(h, 2) * 5)
    if (sign(1.0_8, x) < 0.0_8) s = '-' // s
  end function

  function itext(it, e) result(s)
    type(cpp_mit), intent(in) :: it, e
    character(len=:), allocatable :: s
    class(cpp_pair), pointer :: p
    if (cpp_mit_op_eq(it, e)) then
      s = 'end'
    else
      p => mitpair(it)
      s = strtext(p%first%p)
    end if
  end function
end module containercheck_m

program containercheck
  use containercheck_m
  implicit none
  character(len=5), parameter :: pre(0:9) = [character(len=5) :: 'J', 'c', 'N', 'OF', '_P', 'rg', 'SUB', 'Link-', &
                                              'node.', 'W~']
  character(len=6), parameter :: qs(8) = [character(len=6) :: '', 'J', 'J3', 'Link-', 'W~9', 'zz', 'node.5', 'OF10']
  character(len=2), parameter :: w2(0:6) = [character(len=2) :: 'b', 'a', 'ab', '', 'ba', 'B', 'aa']
  integer(4), parameter :: sizes(10) = [0, 1, 2, 5, 16, 17, 33, 100, 257, 1000]
  class(cpp_umap), pointer :: mu, ml
  class(cpp_uset), pointer :: us, cp
  class(cpp_map), pointer :: ms, mi
  class(cpp_set), pointer :: sp, ss
  class(cpp_stk), pointer :: st
  class(cpp_que), pointer :: qu
  class(cpp_func_o_l), pointer :: noh
  class(cpp_func_o_o_z), pointer :: noe, ltf
  class(cpp_func_o), pointer :: nov
  class(cpp_func_i_z), pointer :: fi
  class(cpp_func_d_z), pointer :: fd
  class(cpp_pair), pointer :: pr
  class(cpp_str), pointer :: ks
  class(cpp_arr_o), pointer :: v, c
  class(cpp_arr_d), pointer :: d, vd, ud
  class(cpp_arr_i), pointer :: av
  class(cpp_arr_b), pointer :: vb, wb
  class(cpp_arr_l), pointer :: io
  class(cpp_vec_i), pointer :: w, rr, u
  class(*), pointer :: x
  type(cpp_slot), pointer :: sl
  type(cpp_mit) :: it, e, lb, ub
  type(cpp_iter_i) :: ie
  type(cpp_iter_d) :: idd
  type(cpp_iter_b) :: ib
  integer(8), allocatable :: ku(:), kl(:)
  integer(8) :: r, k8, l8, dummy
  integer(4) :: round, n, i, j, pat, m, q, ms6(6), xv
  real(8) :: xd
  logical :: z
  noh => null()
  noe => null()
  nov => null()
  chk_s = 4242_8

  ! ---- 64-bit keys
  do round = 0, 11
    mu => cpp_umap_new_funckul_funckkb_funcv(noh, noe, nov, mk_l, 0, mk_i, 0)
    ml => cpp_umap_new_funckul_funckkb_funcv(noh, noe, nov, mk_l, 0, mk_i, 0)
    if (round < 6) then
      n = 1 + int(umod(rnd(), 50_8))
    else
      n = 1 + int(umod(rnd(), 2000_8))
    end if
    allocate(ku(0:n - 1), kl(0:n - 1))
    do i = 0, n - 1
      r = rnd()
      if (umod(r, 3_8) == 0_8) then
        k8 = ishft(r, -40)
      else
        k8 = r
      end if
      if (umod(r, 4_8) == 0_8) then
        sl => cpp_umap_index(mu, boxl(k8))
        select type (vv => sl%p)
        type is (integer(4))
          vv = vv + i
        end select
      else
        pr => cpp_umap_emplace_k_v(mu, boxl(k8), boxi(i))
      end if
      ku(i) = k8
      if (umod(r, 5_8) == 0_8) then
        l8 = umod(r, 1000_8) - 500_8
      else
        l8 = ishft(r, -1)
        if (iand(r, 2_8) /= 0_8) l8 = -l8
      end if
      pr => cpp_umap_emplace_k_v(ml, boxl(l8), boxi(i))
      kl(i) = l8
      if (umod(r, 9_8) == 0_8) dummy = cpp_hashtable_erase_k(mu, boxl(ku(umod(ishft(r, -8), int(i + 1, 8)))))
      if (umod(r, 10_8) == 0_8) dummy = cpp_hashtable_erase_k(ml, boxl(kl(umod(ishft(r, -8), int(i + 1, 8)))))
      if (umod(r, 23_8) == 0_8) then
        if (.not. cpp_hashtable_empty(mu)) it = cpp_hashtable_erase_mite(mu, cpp_hashtable_begin(mu))
      end if
    end do
    deallocate(ku, kl)
    call put('umap ' // i4str(round) // ': ' // ustr(cpp_hashtable_bucket_count(mu)) // ' ' // &
             ustr(cpp_hashtable_size(mu)) // ':')
    it = cpp_hashtable_begin(mu)
    do while (.not. cpp_mit_op_eq(it, cpp_hashtable_end(mu)))
      pr => mitpair(it)
      call put(' ' // ustr(getl(pr%first%p)) // '=' // i4str(geti(pr%second%p)))
      it = cpp_mit_op_inc(it)
    end do
    call nl()
    call put('lmap ' // ustr(cpp_hashtable_bucket_count(ml)) // ' ' // ustr(cpp_hashtable_size(ml)) // ':')
    it = cpp_hashtable_begin(ml)
    do while (.not. cpp_mit_op_eq(it, cpp_hashtable_end(ml)))
      pr => mitpair(it)
      call put(' ' // istr(getl(pr%first%p)) // '=' // i4str(geti(pr%second%p)))
      it = cpp_mit_op_inc(it)
    end do
    call nl()
  end do
  call put('hash ' // ustr(cpp_stdhash_of_i(-1)) // ' ' // ustr(cpp_stdhash_of_l(-huge(0_8) - 1_8)) // ' ' // &
           ustr(cpp_stdhash_of_ul(-1_8)) // ' ' // ustr(cpp_stdhash_of_i(12345)) // ' ' // &
           ustr(cpp_stdhash_of_l(-9007199254740993_8)))
  call nl()

  ! ---- unordered_set: reserve, rehash, erase while iterating, clear
  do round = 0, 7
    us => cpp_uset_new_functul_functtb(noh, noe, mk_i, 0)
    call cpp_hashtable_reserve(us, umod(rnd(), 100_8))
    n = int(umod(rnd(), 400_8))
    do i = 0, n - 1
      pr => cpp_uset_insert(us, boxi(int(umod(rnd(), 1000_8)) - 500))
    end do
    call put('uset ' // i4str(round) // ': ' // ustr(cpp_hashtable_bucket_count(us)) // ' ' // &
             ustr(cpp_hashtable_size(us)) // ':')
    call dump(us)
    call nl()
    if (mod(round, 2) == 0) then
      call cpp_hashtable_rehash(us, cpp_hashtable_size(us) * 3_8 + int(round, 8))
    else
      call cpp_hashtable_reserve(us, cpp_hashtable_size(us) / 2_8)
    end if
    call put('rehash ' // ustr(cpp_hashtable_bucket_count(us)) // ':')
    call dump(us)
    call nl()
    it = cpp_hashtable_begin(us)
    do while (.not. cpp_mit_op_eq(it, cpp_hashtable_end(us)))
      if (mod(geti(cpp_mit_deref(it)), 3) /= 0) then
        it = cpp_hashtable_erase_mite(us, it)
      else
        it = cpp_mit_op_inc(it)
      end if
    end do
    call put('erased ' // ustr(cpp_hashtable_bucket_count(us)) // ' ' // ustr(cpp_hashtable_size(us)) // ':')
    call dump(us)
    call nl()
    cp => cpp_uset_clone(us)
    call cpp_hashtable_clear(us)
    do i = 0, 19
      pr => cpp_uset_insert(us, boxi(int(umod(rnd(), 50_8))))
    end do
    call put('cleared ' // ustr(cpp_hashtable_bucket_count(us)) // ' ' // ustr(cpp_hashtable_size(us)) // ':')
    call dump(us)
    call nl()
    call put('copy ' // ustr(cpp_hashtable_bucket_count(cp)) // ':')
    call dump(cp)
    call nl()
  end do

  ! ---- std::map / std::set
  ms => cpp_map_new_funckkb_funcv(noe, nov, mk_str, 2, mk_i, 0)
  do i = 0, 299
    r = rnd()
    ks => mkstr(trim(pre(umod(r, 10_8))) // istr(umod(ishft(r, -8), 60_8)))
    x => ks
    if (umod(r, 3_8) == 0_8) then
      sl => cpp_map_index(ms, x)
      select type (vv => sl%p)
      type is (integer(4))
        vv = vv + i
      end select
    else
      pr => cpp_map_emplace_k_v(ms, x, boxi(i))
    end if
    if (umod(r, 7_8) == 0_8) then
      ks => mkstr(trim(pre(umod(ishft(r, -4), 10_8))) // istr(umod(ishft(r, -16), 60_8)))
      x => ks
      dummy = cpp_map_erase_k(ms, x)
    end if
  end do
  call put('smap ' // ustr(cpp_map_size(ms)) // ':')
  it = cpp_map_begin(ms)
  do while (.not. cpp_mit_op_eq(it, cpp_map_end(ms)))
    pr => mitpair(it)
    call put(' ' // strtext(pr%first%p) // '=' // i4str(geti(pr%second%p)))
    it = cpp_mit_op_inc(it)
  end do
  call nl()
  call put('bounds:')
  do q = 1, 8
    ks => mkstr(trim(qs(q)))
    x => ks
    lb = cpp_map_lower_bound(ms, x)
    ub = cpp_map_upper_bound(ms, x)
    e = cpp_map_end(ms)
    call put(' ' // trim(qs(q)) // ':' // itext(lb, e) // '/' // itext(ub, e))
  end do
  call nl()
  mi => cpp_map_new_funckkb_funcv(noe, nov, mk_i, 0, mk_i, 0)
  do i = 0, 199
    r = rnd()
    sl => cpp_map_index(mi, boxi(int(umod(r, 300_8)) - 150))
    select type (vv => sl%p)
    type is (integer(4))
      vv = vv + i
    end select
    if (umod(r, 5_8) == 0_8) dummy = cpp_map_erase_k(mi, boxi(int(umod(ishft(r, -9), 300_8)) - 150))
  end do
  call put('imap ' // ustr(cpp_map_size(mi)) // ':')
  it = cpp_map_begin(mi)
  do while (.not. cpp_mit_op_eq(it, cpp_map_end(mi)))
    pr => mitpair(it)
    call put(' ' // i4str(geti(pr%first%p)) // '=' // i4str(geti(pr%second%p)))
    it = cpp_mit_op_inc(it)
  end do
  call nl()
  sp => cpp_set_new_functtb(noe, mk_none, 2)
  do i = 0, 199
    r = rnd()
    pr => cpp_pair_new_a_b(boxl(umod(r, 7_8) - 3_8), boxl(umod(ishft(r, -8), 1000_8) - 500_8), mk_l, 0, mk_l, 0)
    x => pr
    pr => cpp_set_insert(sp, x)
  end do
  call put('pset ' // ustr(cpp_set_size(sp)) // ':')
  it = cpp_set_begin(sp)
  do while (.not. cpp_mit_op_eq(it, cpp_set_end(sp)))
    pr => mitpair(it)
    call put(' (' // istr(getl(pr%first%p)) // ',' // istr(getl(pr%second%p)) // ')')
    it = cpp_mit_op_inc(it)
  end do
  call nl()
  ss => cpp_set_new_functtb(noe, mk_str, 2)
  do i = 0, 99
    r = rnd()
    ks => mkstr(trim(pre(umod(r, 10_8))) // istr(umod(ishft(r, -8), 20_8)))
    x => ks
    pr => cpp_set_insert(ss, x)
    if (umod(r, 4_8) == 0_8) then
      ks => mkstr(trim(pre(umod(ishft(r, -3), 10_8))) // istr(umod(ishft(r, -12), 20_8)))
      x => ks
      dummy = cpp_set_erase_t(ss, x)
    end if
  end do
  call put('sset ' // ustr(cpp_set_size(ss)) // ':')
  it = cpp_set_begin(ss)
  do while (.not. cpp_mit_op_eq(it, cpp_set_end(ss)))
    call put(' ' // strtext(cpp_mit_deref(it)))
    it = cpp_mit_op_inc(it)
  end do
  call nl()

  ! ---- std::stack / std::queue
  st => cpp_stk_new(mk_i, 0)
  qu => cpp_que_new(mk_i, 0)
  call put('stackqueue:')
  do i = 0, 499
    r = rnd()
    if (umod(r, 3_8) /= 0_8 .or. cpp_stk_empty(st)) then
      call cpp_stk_push(st, boxi(int(umod(r, 100_8))))
    else
      sl => cpp_stk_top(st)
      call put(' s' // i4str(geti(sl%p)))
      call cpp_stk_pop(st)
    end if
    if (umod(r, 4_8) /= 0_8 .or. cpp_que_empty(qu)) then
      call cpp_que_push(qu, boxi(int(umod(r, 100_8))))
    else
      sl => cpp_que_front(qu)
      call put(' q' // i4str(geti(sl%p)))
      sl => cpp_que_back(qu)
      call put('/' // i4str(geti(sl%p)))
      call cpp_que_pop(qu)
    end if
  end do
  call put(' | ' // ustr(cpp_stk_size(st)) // ' ' // ustr(cpp_que_size(qu)))
  call nl()

  ! ---- partial_sort with middle < last; sort and stable_sort of subranges
  allocate(lt_e :: ltf)
  do j = 1, 10
    n = sizes(j)
    do pat = 0, 1
      v => cpp_arr_o_new(int(n, 8))
      do i = 0, n - 1
        if (pat == 0) then
          allocate(v%a(i)%p, source=e_t(int(umod(rnd(), 4_8)), i))
        else
          allocate(v%a(i)%p, source=e_t(int(umod(rnd(), 100000_8)), i))
        end if
      end do
      ms6 = [0, 1, n / 3, n / 2, n - 1, n]
      do q = 1, 6
        m = ms6(q)
        if (m < 0 .or. m > n) cycle
        c => copy_e(v, n)
        call cpp_stdalgo_partialsort__o(c, 0_8, int(m, 8), int(n, 8), ltf, mk_none, 2)
        call put('psort ' // i4str(n) // ' ' // i4str(pat) // ' ' // i4str(m) // ' ')
        call ids(c, n)
      end do
      c => copy_e(v, n)
      call cpp_stdalgo_sort__o(c, int(n / 4, 8), int((3 * n) / 4, 8), ltf, mk_none, 2)
      call put('subsort ' // i4str(n) // ' ' // i4str(pat) // ' ')
      call ids(c, n)
      c => copy_e(v, n)
      call cpp_stdalgo_stablesort__o(c, int(n / 4, 8), int((3 * n) / 4, 8), ltf, mk_none, 2)
      call put('substable ' // i4str(n) // ' ' // i4str(pat) // ' ')
      call ids(c, n)
    end do
  end do

  ! ---- std::sort with std::less: doubles with signed zeros and ties; pair<int, string>
  d => cpp_arr_d_new(300_8)
  do i = 0, 299
    r = rnd()
    xd = real(int(umod(r, 9_8)) - 4, 8) * 0.5_8
    if (xd == 0.0_8 .and. umod(ishft(r, -8), 2_8) /= 0_8) xd = -0.0_8
    d%a(i) = xd
  end do
  call cpp_stdalgo_sort__d(d, 0_8, 300_8, cpp_less_d_d_fn)
  call put('dsort:')
  do i = 0, 299
    call put(' ' // f1(d%a(i)))
  end do
  call nl()
  v => cpp_arr_o_new(100_8)
  do i = 0, 99
    r = rnd()
    ks => mkstr(trim(w2(umod(ishft(r, -8), 7_8))))
    x => ks
    v%a(i)%p => cpp_pair_new_a_b(boxi(int(umod(r, 5_8))), x, mk_i, 0, mk_str, 2)
  end do
  call cpp_stdalgo_sort__o(v, 0_8, 100_8, cpp_less_o_pairistr_fn, mk_none, 2)
  call put('psort2:')
  do i = 0, 99
    select type (pp => v%a(i)%p)
    class is (cpp_pair)
      call put(' ' // i4str(geti(pp%first%p)) // strtext(pp%second%p))
    end select
  end do
  call nl()

  ! ---- <algorithm> / <numeric>
  do round = 0, 5
    n = int(umod(rnd(), 60_8))
    av => cpp_arr_i_new(int(n, 8))
    vd => cpp_arr_d_new(int(n, 8))
    vb => cpp_arr_b_new(int(n, 8))
    do i = 0, n - 1
      av%a(i) = int(umod(rnd(), 10_8)) - 3
      vd%a(i) = real(av%a(i), 8)
      vb%a(i) = int(av%a(i), 1)
    end do
    idd = cpp_algo_min_element_itert_itert__d(itd(vd, 0), itd(vd, n))
    call put('algo ' // i4str(round) // ': min ' // istr(idd%i))
    idd = cpp_algo_max_element_itert_itert__d(itd(vd, 0), itd(vd, n))
    call put(' max ' // istr(idd%i))
    call put(' count ' // istr(cpp_algo_count__b(itb(vb, 0), itb(vb, n), 2_1)))
    ib = cpp_algo_find__b(itb(vb, 0), itb(vb, n), 4_1)
    call put(' find ' // istr(ib%i))
    z = cpp_algo_is_sorted_itert_itert__d(itd(vd, 0), itd(vd, n))
    call put(' sorted ' // i4str(merge(1, 0, z)))
    call put(' sum ' // i4str(int(cpp_algo_accumulate_iterd_iterd_d(itd(vd, 0), itd(vd, n), 0.0_8))))
    call nl()
    allocate(p_gt5 :: fi)
    call put('pred ' // i4str(merge(1, 0, cpp_algo_any_of__i(iti(av, 0), iti(av, n), fi))))
    allocate(p_gtm3 :: fd)
    call put(' ' // i4str(merge(1, 0, cpp_algo_all_of__d(itd(vd, 0), itd(vd, n), fd))))
    deallocate(fi)
    allocate(p_eq6 :: fi)
    call put(' ' // i4str(merge(1, 0, cpp_algo_none_of__i(iti(av, 0), iti(av, n), fi))))
    deallocate(fi)
    allocate(p_neg :: fi)
    call put(' ' // istr(cpp_algo_count_if__i(iti(av, 0), iti(av, n), fi)))
    ie = cpp_algo_adjacent_find_itert_itert__i(iti(av, 0), iti(av, n))
    call put(' ' // istr(ie%i))
    call put(' ' // i4str(merge(1, 0, cpp_algo_equal__i(iti(av, 0), iti(av, n), iti(av, 0)))))
    call nl()
    deallocate(fi, fd)
    ! replace (over byte, as the engine calls it) and reverse
    wb => cpp_arr_b_new(int(n, 8))
    if (n > 0) wb%a(0:n - 1) = vb%a(0:n - 1)
    call cpp_algo_replace__b(itb(wb, 0), itb(wb, n), 1_1, 7_1)
    w => cpp_algo_vecfrom__i(iti(av, 0), iti(av, n))
    do i = 0, n - 1
      w%a%a(i) = int(wb%a(i), 4)
    end do
    call cpp_algo_reverse__i(iti(w%a, 0), iti(w%a, w%n))
    call ints('replrev', w%a, w%n)
    rr => cpp_algo_vecfrom__i(iti(av, 0), iti(av, n))
    allocate(p_div3 :: fi)
    ie = cpp_algo_remove_if__i(iti(rr%a, 0), iti(rr%a, rr%n), fi)
    deallocate(fi)
    call ints('remove_if', rr%a, int(ie%i, 4))
    u => cpp_algo_vecfrom__i(iti(av, 0), iti(av, n))
    call cpp_stdalgo_sort__i(u%a, 0_8, int(u%n, 8), cpp_less_i_i_fn)
    ud => cpp_arr_d_new(int(n, 8))
    do i = 0, n - 1
      ud%a(i) = real(u%a%a(i), 8)
    end do
    call put('bounds:')
    do q = -4, 7
      ie = cpp_algo_lower_bound_itert_itert_t__i(iti(u%a, 0), iti(u%a, u%n), q)
      idd = cpp_algo_upper_bound_itert_itert_t__d(itd(ud, 0), itd(ud, n), real(q, 8))
      call put(' ' // i4str(q) // ':' // istr(ie%i) // '/' // istr(idd%i))
    end do
    call nl()
    ie = cpp_algo_unique_itert_itert__i(iti(u%a, 0), iti(u%a, u%n))
    call ints('unique', u%a, int(ie%i, 4))
    io => cpp_arr_l_new(5_8)
    call cpp_algo_iota_iterul_iterul_ul(itl(io, 0), itl(io, 5), -3_8)
    call put('iota:')
    do i = 0, 4
      call put(' ' // ustr(io%a(i)))
    end do
    call nl()
  end do
  xv = 0

contains

  subroutine dump(t)
    class(cpp_hashtable), intent(inout) :: t
    type(cpp_mit) :: jt
    jt = cpp_hashtable_begin(t)
    do while (.not. cpp_mit_op_eq(jt, cpp_hashtable_end(t)))
      call put(' ' // i4str(geti(cpp_mit_deref(jt))))
      jt = cpp_mit_op_inc(jt)
    end do
  end subroutine
end program containercheck
