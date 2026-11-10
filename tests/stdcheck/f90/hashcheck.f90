! hashcheck.f90 - the cases of hashcheck.cpp through part B of the Fortran runtime (UMap, USet, HashTable, MIt and
! StdHash of f90/runtime/cpp_rt_b.f90); the output must equal hashcheck.cpp's byte for byte: bucket counts and
! iteration order after inserts, operator[], erases, reserve and copies, and std::hash of strings.
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
program hashcheck
  use cpp_runtime
  use chk_util
  implicit none
  character(len=6), parameter :: pre(0:9) = [character(len=6) :: 'J', 'C', 'N', 'OF', 'P', 'RG', 'SUB_', 'Link-', &
                                              'node.', 'W']
  character(len=11), parameter :: words(7) = [character(len=11) :: '', 'a', 'abcdefg', 'abcdefgh', 'abcdefghi', &
                                              'Junction_17', 'OUTFALL-1']
  class(cpp_umap), pointer :: m, mi, c
  class(cpp_uset), pointer :: st
  class(cpp_func_o_l), pointer :: noh
  class(cpp_func_o_o_z), pointer :: noe
  class(cpp_func_o), pointer :: nov
  class(cpp_pair), pointer :: pr
  class(cpp_str), pointer :: k
  class(*), pointer :: x
  type(cpp_slot), pointer :: sl
  type(cpp_mit) :: it, e
  integer(4) :: round, n, i, w
  integer(8) :: r, dummy
  noh => null()
  noe => null()
  nov => null()
  chk_s = 52024_8
  do round = 0, 39
    m => cpp_umap_new_funckul_funckkb_funcv(noh, noe, nov, mk_str, 2, mk_i, 0)
    mi => cpp_umap_new_funckul_funckkb_funcv(noh, noe, nov, mk_i, 0, mk_i, 0)
    st => cpp_uset_new_functul_functtb(noh, noe, mk_str, 2)
    if (round < 20) then
      n = 1 + int(umod(rnd(), 60_8))
    else
      n = 1 + int(umod(rnd(), 3000_8))
    end if
    if (mod(round, 7) == 3) call cpp_hashtable_reserve(m, int(n, 8))
    do i = 0, n - 1
      r = rnd()
      k => name(r)
      x => k
      if (umod(r, 5_8) == 0_8) then
        sl => cpp_umap_index(m, x)
        select type (v => sl%p)
        type is (integer(4))
          v = v + i
        end select
      else
        pr => cpp_umap_emplace_k_v(m, x, boxi(i))
      end if
      pr => cpp_umap_emplace_k_v(mi, boxi(int(umod(r, 20000_8)) - 10000), boxi(i))
      pr => cpp_uset_insert(st, x)
      if (umod(r, 11_8) == 0_8) then
        if (.not. cpp_hashtable_empty(m)) then
          x => name(rnd())
          dummy = cpp_hashtable_erase_k(m, x)
        end if
      end if
      if (umod(r, 13_8) == 0_8) dummy = cpp_hashtable_erase_k(mi, boxi(int(umod(rnd(), 20000_8)) - 10000))
      if (umod(r, 17_8) == 0_8) then
        if (.not. cpp_hashtable_empty(m)) it = cpp_hashtable_erase_mite(m, cpp_hashtable_begin(m))
      end if
    end do
    c => cpp_umap_clone(m)
    call put('map ' // i4str(round) // ': ' // ustr(cpp_hashtable_bucket_count(m)) // ' buckets ' // &
             ustr(cpp_hashtable_size(m)) // ':')
    it = cpp_hashtable_begin(m)
    e = cpp_hashtable_end(m)
    do while (.not. cpp_mit_op_eq(it, e))
      pr => mitpair(it)
      call put(' ' // strtext(pr%first%p) // '=' // i4str(geti(pr%second%p)))
      it = cpp_mit_op_inc(it)
    end do
    call nl()
    call put('copy:')
    it = cpp_hashtable_begin(c)
    do while (.not. cpp_mit_op_eq(it, cpp_hashtable_end(c)))
      pr => mitpair(it)
      call put(' ' // strtext(pr%first%p))
      it = cpp_mit_op_inc(it)
    end do
    call nl()
    call put('imap ' // ustr(cpp_hashtable_bucket_count(mi)) // ' ' // ustr(cpp_hashtable_size(mi)) // ':')
    it = cpp_hashtable_begin(mi)
    do while (.not. cpp_mit_op_eq(it, cpp_hashtable_end(mi)))
      pr => mitpair(it)
      call put(' ' // i4str(geti(pr%first%p)) // '=' // i4str(geti(pr%second%p)))
      it = cpp_mit_op_inc(it)
    end do
    call nl()
    call put('set ' // ustr(cpp_hashtable_bucket_count(st)) // ':')
    it = cpp_hashtable_begin(st)
    do while (.not. cpp_mit_op_eq(it, cpp_hashtable_end(st)))
      call put(' ' // strtext(cpp_mit_deref(it)))
      it = cpp_mit_op_inc(it)
    end do
    call nl()
  end do
  do w = 1, 7
    k => mkstr(trim(words(w)))
    call put('hash ' // trim(words(w)) // ' ' // ustr(cpp_stdhash_of_str(k)))
    call nl()
  end do

contains

  function name(rr) result(s)
    integer(8), intent(in) :: rr
    class(cpp_str), pointer :: s
    character(len=:), allocatable :: t
    t = trim(pre(umod(rr, 10_8))) // istr(umod(ishft(rr, -8), 5000_8))
    if (umod(ishft(rr, -20), 3_8) == 0_8) t = t // '_' // istr(umod(ishft(rr, -24), 97_8))
    s => mkstr(t)
  end function
end program hashcheck
