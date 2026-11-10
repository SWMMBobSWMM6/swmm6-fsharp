! sortcheck.f90 - the cases of sortcheck.cpp through part B of the Fortran runtime (f90/runtime/cpp_rt_b.f90); the
! output must equal sortcheck.cpp's byte for byte. Argument O (the default): Sort / StableSort / PartialSort over
! an array of boxed structs (cpp_stdalgo_*__o). D, I or L: the same permutations from the double, int and long
! specialisations, sorting the ids by their keys (as sortcheck.java does).
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
module sortcheck_m
  use cpp_runtime
  use chk_util
  implicit none
  type :: e_t
    integer(4) :: key = 0
    integer(4) :: id = 0
  end type
  integer(4), allocatable :: key(:)
  type, extends(cpp_func_o_o_z) :: lt_o
  contains
    procedure :: invoke => lt_o_invoke
  end type
  type, extends(cpp_func_i_i_z) :: lt_i
  contains
    procedure :: invoke => lt_i_invoke
  end type
  type, extends(cpp_func_l_l_z) :: lt_l
  contains
    procedure :: invoke => lt_l_invoke
  end type
  type, extends(cpp_func_d_d_z) :: lt_d
  contains
    procedure :: invoke => lt_d_invoke
  end type
contains
  recursive function lt_o_invoke(self, a1, a2) result(zres)
    class(lt_o), intent(inout), target :: self
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
  recursive function lt_i_invoke(self, a1, a2) result(zres)
    class(lt_i), intent(inout), target :: self
    integer(4), value :: a1, a2
    logical :: zres
    zres = key(a1) < key(a2)
  end function
  recursive function lt_l_invoke(self, a1, a2) result(zres)
    class(lt_l), intent(inout), target :: self
    integer(8), value :: a1, a2
    logical :: zres
    zres = key(a1) < key(a2)
  end function
  recursive function lt_d_invoke(self, a1, a2) result(zres)
    class(lt_d), intent(inout), target :: self
    real(8), value :: a1, a2
    logical :: zres
    zres = key(int(a1)) < key(int(a2))
  end function

  ! the ids in the order sort (which 0), stable_sort (1) or partial_sort (2) leaves them
  subroutine run(mode, n, which, ids)
    character, intent(in) :: mode
    integer(4), intent(in) :: n, which
    integer(4), intent(out) :: ids(0:)
    class(cpp_arr_o), pointer :: ao
    class(cpp_arr_i), pointer :: ai
    class(cpp_arr_l), pointer :: al
    class(cpp_arr_d), pointer :: ad
    class(cpp_func_o_o_z), pointer :: fo
    class(cpp_func_i_i_z), pointer :: fi
    class(cpp_func_l_l_z), pointer :: fl
    class(cpp_func_d_d_z), pointer :: fd
    integer(4) :: i
    integer(8) :: m
    m = int(n, 8)
    select case (mode)
    case ('O')
      ao => cpp_arr_o_new(m)
      do i = 0, n - 1
        allocate(ao%a(i)%p, source=e_t(key(i), i))
      end do
      allocate(lt_o :: fo)
      if (which == 0) then
        call cpp_stdalgo_sort__o(ao, 0_8, m, fo, mk_none, 2)
      else if (which == 1) then
        call cpp_stdalgo_stablesort__o(ao, 0_8, m, fo, mk_none, 2)
      else
        call cpp_stdalgo_partialsort__o(ao, 0_8, m, m, fo, mk_none, 2)
      end if
      do i = 0, n - 1
        select type (e => ao%a(i)%p)
        type is (e_t)
          ids(i) = e%id
        end select
      end do
    case ('I')
      ai => cpp_arr_i_new(m)
      do i = 0, n - 1
        ai%a(i) = i
      end do
      allocate(lt_i :: fi)
      if (which == 0) then
        call cpp_stdalgo_sort__i(ai, 0_8, m, fi)
      else if (which == 1) then
        call cpp_stdalgo_stablesort__i(ai, 0_8, m, fi)
      else
        call cpp_stdalgo_partialsort__i(ai, 0_8, m, m, fi)
      end if
      if (n > 0) ids(0:n - 1) = ai%a(0:n - 1)
    case ('L')
      al => cpp_arr_l_new(m)
      do i = 0, n - 1
        al%a(i) = int(i, 8)
      end do
      allocate(lt_l :: fl)
      if (which == 0) then
        call cpp_stdalgo_sort__l(al, 0_8, m, fl)
      else if (which == 1) then
        call cpp_stdalgo_stablesort__l(al, 0_8, m, fl)
      else
        call cpp_stdalgo_partialsort__l(al, 0_8, m, m, fl)
      end if
      if (n > 0) ids(0:n - 1) = int(al%a(0:n - 1), 4)
    case default
      ad => cpp_arr_d_new(m)
      do i = 0, n - 1
        ad%a(i) = real(i, 8)
      end do
      allocate(lt_d :: fd)
      if (which == 0) then
        call cpp_stdalgo_sort__d(ad, 0_8, m, fd)
      else if (which == 1) then
        call cpp_stdalgo_stablesort__d(ad, 0_8, m, fd)
      else
        call cpp_stdalgo_partialsort__d(ad, 0_8, m, m, fd)
      end if
      if (n > 0) ids(0:n - 1) = int(ad%a(0:n - 1), 4)
    end select
  end subroutine

  subroutine out(tag, n, pat, ids)
    character(len=*), intent(in) :: tag
    integer(4), intent(in) :: n, pat
    integer(4), intent(in) :: ids(0:)
    integer(4) :: i
    call put(tag // ' ' // i4str(n) // ' ' // i4str(pat) // ':')
    do i = 0, n - 1
      call put(' ' // i4str(ids(i)))
    end do
    call nl()
  end subroutine
end module sortcheck_m

program sortcheck
  use sortcheck_m
  implicit none
  character(len=8) :: arg
  character :: mode
  integer(4) :: ns(0:306), n, pat, i, j, k
  integer(4), allocatable :: ids(:)
  mode = 'O'
  if (command_argument_count() > 0) then
    call get_command_argument(1, arg)
    mode = arg(1:1)
  end if
  chk_s = 1729_8
  do i = 0, 300
    ns(i) = i
  end do
  ns(301:306) = [511, 512, 513, 1000, 4096, 10007]
  do j = 0, 306
    n = ns(j)
    do pat = 0, 6
      if (allocated(key)) deallocate(key)
      allocate(key(0:max(n - 1, 0)), ids(0:max(n - 1, 0)))
      do i = 0, n - 1
        select case (pat)
        case (0)
          k = int(umod(rnd(), 5_8))
        case (1)
          k = int(umod(rnd(), 1000000_8))
        case (2)
          k = i / 3
        case (3)
          k = (n - i) / 2
        case (4)
          k = 7
        case (5)
          if (i < n / 2) then
            k = i
          else
            k = n - i
          end if
        case default
          k = mod(i * 7919, 31)
        end select
        key(i) = k
      end do
      call run(mode, n, 0, ids)
      call out('sort', n, pat, ids)
      call run(mode, n, 1, ids)
      call out('stable', n, pat, ids)
      call run(mode, n, 2, ids)
      call out('heap', n, pat, ids)
      deallocate(ids)
    end do
  end do
end program sortcheck
