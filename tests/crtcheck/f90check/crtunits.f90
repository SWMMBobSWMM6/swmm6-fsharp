! crtunits.f90 - the unit checks of tests/crtcheck/f90check (pycheck's units, for part C's members) and the program
! crtcheck (crtcheck libm N | printf | scanf | misc | units).
! Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
module ckunits
  use ckutil
  implicit none

  ! an object whose destructor counts
  type, extends(cpp_object) :: dcount
    integer :: n = 0
  contains
    procedure :: dispose => dcount_dispose
  end type
  ! a bool& member: the lambda giving the place
  type, extends(cpp_refget_z) :: zref
    logical, pointer :: p => null()
  contains
    procedure :: invoke => zref_invoke
  end type
  ! a Ptr<long> to a field: the lambda giving the place
  type, extends(cpp_refat_l) :: lref
    integer(8), pointer :: p => null()
  contains
    procedure :: invoke => lref_invoke
  end type
  integer, save :: ndisposed = 0

contains

  recursive subroutine dcount_dispose(self)
    class(dcount), intent(inout), target :: self
    self%n = self%n + 1
    ndisposed = ndisposed + 1
  end subroutine
  recursive function zref_invoke(self) result(zres)
    class(zref), intent(inout), target :: self
    logical, pointer :: zres
    zres => self%p
  end function
  recursive function lref_invoke(self, a1) result(zres)
    class(lref), intent(inout), target :: self
    class(*), pointer, intent(in) :: a1
    integer(8), pointer :: zres
    zres => self%p
  end function

  function bits(x) result(b)
    real(8), intent(in) :: x
    integer(8) :: b
    b = transfer(x, 0_8)
  end function
  function d(b) result(x)
    integer(8), intent(in) :: b
    real(8) :: x
    x = transfer(b, 0.0_8)
  end function
  function arr_b(v) result(a)
    integer, intent(in) :: v(:)
    class(cpp_arr_b), pointer :: a
    integer :: i
    allocate(a)
    allocate(a%a(0:size(v)-1))
    do i = 1, size(v)
      a%a(i - 1) = int(iand(v(i) + 128, 255) - 128, 1)
    end do
  end function
  function arr_i(v) result(a)
    integer(8), intent(in) :: v(:)
    class(cpp_arr_i), pointer :: a
    integer :: i
    allocate(a)
    allocate(a%a(0:size(v)-1))
    do i = 1, size(v)
      a%a(i - 1) = int(merge(v(i) - 4294967296_8, v(i), v(i) > 2147483647_8), 4)
    end do
  end function
  function arr_d(v) result(a)
    real(8), intent(in) :: v(:)
    class(cpp_arr_d), pointer :: a
    allocate(a)
    allocate(a%a(0:size(v)-1))
    a%a = v
  end function
  function arr_l(v) result(a)
    integer(8), intent(in) :: v(:)
    class(cpp_arr_l), pointer :: a
    allocate(a)
    allocate(a%a(0:size(v)-1))
    a%a = v
  end function
  function cbuf(v) result(p)
    integer, intent(in) :: v(:)
    type(cpp_cptr) :: p
    p%b => arr_b(v)
    p%o = 0
  end function
  function mkstr(s) result(p)
    character(len=*), intent(in) :: s
    class(cpp_str), pointer :: p
    type(cpp_cptr) :: c
    allocate(p)
    c = cpp_r_lit(s)
    p%b => c%b
    p%n = len(s)
  end function

  subroutine units()
    type(cpp_cptr) :: s, e, p, q, line, lb, sb, name, an, stamp, view, nul
    integer(8) :: v, pos, k, t
    integer(1) :: flag
    integer(4) :: r, i4
    real(4) :: f4
    real(8) :: x, dv
    class(set_f), pointer :: sf
    class(cpp_action_f), pointer :: af
    class(cpp_cfile), pointer :: fp, nofile
    class(cpp_arr_l), pointer :: la
    class(cpp_arr_i), pointer :: ia
    class(cpp_arr_d), pointer :: da, da2
    class(cpp_arr_o), pointer :: oa
    class(cpp_arr_z), pointer :: za
    class(cpp_arr_s), pointer :: sa
    class(cpp_arr_f), pointer :: fa
    type(cpp_ptr_d) :: pd
    type(cpp_ptr_l) :: pl
    procedure(cpp_maker), pointer :: mk
    class(cpp_tupn), pointer :: ta, tb, tc, td, te
    class(*), pointer :: x1, x2, ob
    class(cpp_atomic_i), pointer :: ai7
    real(8), pointer :: dp
    type(cpp_ptr_d) :: pd2
    class(cpp_atomic_l), pointer :: al
    class(cpp_atomic_d), pointer :: ad
    class(cpp_atomic_z), pointer :: az
    class(cpp_object), pointer :: oc
    class(cpp_onceflag), pointer :: once
    class(counter), pointer :: cnt
    class(cpp_action), pointer :: act
    class(cpp_defer), pointer :: df
    class(cpp_pair), pointer :: pr
    class(cpp_errorcode), pointer :: ec
    class(cpp_sysexception), pointer :: se
    class(dcount), pointer :: d1, d2
    class(cpp_box_z), pointer :: bz
    class(zref), pointer :: zr
    class(cpp_refget_z), pointer :: rg
    class(lref), pointer :: lr
    class(cpp_refat_l), pointer :: ral
    logical, pointer :: lp
    logical, target :: zt
    integer(8), target :: lt
    type(cpp_timepoint) :: t0
    character(len=:), allocatable :: dir, txt, sline
    character(len=4096) :: home
    integer :: hl, st
    integer(1), allocatable :: fb(:)

    nul = cpp_cptr()
    ! ---- sscanf into pointer parameters: the stash and R.Back
    v = -9
    flag = int(ichar('q'), 1)
    r = cpp_r_back_i_t1r_t2r__lb(cpp_r_sscanf(cpp_r_lit('123 Y'), cpp_r_lit('%ld %c'), cpp_r_slot, cpp_r_slot), v, flag)
    call check('slot %ld %c', r == 2 .and. v == 123 .and. flag == ichar('Y'))
    v = -9
    flag = int(ichar('q'), 1)
    r = cpp_r_back_i_t1r_t2r__lb(cpp_r_sscanf(cpp_r_lit('77'), cpp_r_lit('%ld %c'), cpp_r_slot, cpp_r_slot), v, flag)
    call check('slot partial', r == 1 .and. v == 77 .and. flag == ichar('q'))
    v = -9
    r = cpp_r_back_i_t1r_t2r__lb(cpp_r_sscanf(cpp_r_lit('-12 z'), cpp_r_lit('%d %c'), cpp_r_slot, cpp_r_slot), v, flag)
    call check('slot int into long', r == 2 .and. v == -12 .and. flag == ichar('z'))
    allocate(sf)
    sf%k = 1
    floats(1) = 99.99
    af => sf
    r = cpp_r_back_i_t1r__b(cpp_r_sscanf(cpp_r_lit('1.5 X'), cpp_r_lit('%f %c'), cpp_r_reff(af), cpp_r_slot), flag)
    call check('RefF + slot', r == 2 .and. floats(1) == 1.5 .and. flag == ichar('X'))
    r = cpp_r_back_i_t1r__b(cpp_r_sscanf(cpp_r_lit('x'), cpp_r_lit('%d'), cpp_r_slot), flag)
    call check('slot untouched', r == 0 .and. flag == ichar('X'))
    sb = cpp_r_buf_i(80)
    r = cpp_r_sscanf(cpp_r_lit('  SWMM5 x'), cpp_r_lit('%s'), sb)
    call check('%s CPtr', r == 1 .and. cpp_r_strcmp(sb, cpp_r_lit('SWMM5')) == 0)
    r = cpp_r_sscanf(cpp_r_lit('a,b'), cpp_r_lit('%[^,],%s'), sb, cpp_r_slot)
    call check('%[ into CPtr', r == 2 .and. cpp_r_strcmp(sb, cpp_r_lit('a')) == 0)

    ! ---- strtod details
    s = cpp_r_lit('  0x1.8p1rest')
    e = nul
    x = cpp_r_strtod_cptr_cptrr(s, e)
    call check('strtod hex', x == 3.0_8 .and. e%o - s%o == 9)
    call check('strtod nan payload', bits(cpp_r_strtod_cptr(cpp_r_lit('nan(0x5)'))) == 9221120237041090565_8)
    call check('strtod -nan', bits(cpp_r_strtod_cptr(cpp_r_lit('-nan'))) == -2251799813685248_8)
    s = cpp_r_lit('abc')
    x = cpp_r_strtod_cptr_cptrr(s, e)
    call check('strtod no number', x == 0.0_8 .and. e%o == s%o .and. associated(e%b, s%b))
    x = cpp_r_strtod_cptr(cpp_r_lit('1e99999999999'))
    call check('strtod huge exponent', x > huge(x) .and. cpp_r_strtod_cptr(cpp_r_lit('1e-99999999999')) == 0.0_8)
    call check('atof', cpp_r_atof(cpp_r_lit(' -2.5e1x')) == -25.0_8)
    call check('atoi wrap', cpp_r_atoi(cpp_r_lit('4294967297')) == 1 .and. cpp_r_atoi(cpp_r_lit('99999999999999999999')) == -1)
    call check('strtol0', cpp_r_strtol0(cpp_r_lit(' -42abc'), 10) == -42 .and. cpp_r_atol(cpp_r_lit('9223372036854775808')) == huge(1_8))
    e = nul
    call check('strtol base 16', cpp_r_strtol(cpp_r_lit('0x1fz'), e, 16) == 31 .and. e%o == 4)
    call check('strtol base 0', cpp_r_strtol(cpp_r_lit('017'), e, 0) == 15 .and. cpp_r_strtol(cpp_r_lit('0xg'), e, 0) == 0 .and. e%o == 1)

    ! ---- raw memory
    la => arr_l([0_8])
    da => arr_d([-2.75_8])
    q%b => null()
    pl = cpp_mem_memcpy_ptrt_ptru_ul__ld(cpp_ptr_l_new_ta_l(la, 0_8), cpp_ptr_d_new_ta_l(da, 0_8), 8_8)
    call check('memcpy double->u64', la%a(0) == bits(-2.75_8))
    la => arr_l([72623859790382856_8])           ! 0x0102030405060708
    view = cpp_bytes_view_ptrt__l(cpp_ptr_l_new_ta_l(la, 0_8))
    call check('bytes view', size(view%b%a) == 8 .and. view%b%a(0) == 8 .and. view%b%a(7) == 1)
    da => arr_d([0.0_8])
    pd = cpp_mem_memcpy_ptrt_cptr_ul__d(cpp_ptr_d_new_ta_l(da, 0_8), view, 8_8)
    call check('memcpy CPtr->double', bits(da%a(0)) == 72623859790382856_8)
    pd = cpp_mem_calloc__d(5_8, 8_8)
    call check('calloc', size(pd%a%a) == 5 .and. pd%o == 0 .and. all(pd%a%a == 0.0_8))
    allocate(oa)
    allocate(oa%a(0:2))
    allocate(dcount :: oa%a(0)%p, oa%a(1)%p, oa%a(2)%p)
    ob => oa%a(2)%p
    mk => h_mk
    call check('diff', cpp_mem_diff_t_ptrt__o(ob, cpp_ptr_o_new_ta_l(oa, 1_8), mk, 1) == 1)
    ia => arr_i([0_8, 0_8])
    call check('memcpy bytes->u32 offset', diffi(cpp_mem_memcpy_ptrt_ptru_ul__ib(cpp_ptr_i_new_ta_l(ia, 1_8), &
         cpp_ptr_b_new_ta_l(arr_b([1, 2, 3, 4, 5]), 1_8), 4_8)) .and. ia%a(0) == 0 .and. ia%a(1) == 84148994)
    ia => arr_i([0_8])
    call check('memcpy uint crc', diffi(cpp_mem_memcpy_ptrt_ptru_ul__ib(cpp_ptr_i_new_ta_l(ia, 0_8), &
         cpp_ptr_b_new_ta_l(arr_b([1, 2, 3, 244]), 0_8), 4_8)) .and. ia%a(0) == int(4093837825_8 - 4294967296_8, 4))
    ia => arr_i([286331153_8, 572662306_8])
    call check('memcpy partial element', diffi(cpp_mem_memcpy_ptrt_cptr_ul__i(cpp_ptr_i_new_ta_l(ia, 0_8), &
         cbuf([1, 2, 3, 4, 5, 6]), 6_8)) .and. ia%a(0) == 67305985 .and. ia%a(1) == 572655109)
    ia => arr_i([0_8])
    call check('memcpy int signed', diffi(cpp_mem_memcpy_ptrt_cptr_ul__i(cpp_ptr_i_new_ta_l(ia, 0_8), &
         cbuf([255, 255, 255, 255]), 4_8)) .and. ia%a(0) == -1)
    allocate(za)
    allocate(za%a(0:2))
    za%a = [.true., .false., .true.]
    view = cpp_bytes_view_ptrt__z(cpp_ptr_z_new_ta_l(za, 1_8))
    call check('bool view', size(view%b%a) == 2 .and. view%b%a(0) == 0 .and. view%b%a(1) == 1)
    view = cpp_bytes_view_ptrt__d(cpp_ptr_d_new_ta_l(arr_d([1.0_8, 2.0_8]), 1_8))
    call check('double view offset', size(view%b%a) == 8 .and. view%b%a(7) == 64 .and. view%b%a(6) == 0)
    allocate(sa)
    allocate(sa%a(0:1))
    sa%a = 0
    view = cpp_mem_memcpy_ptrt_cptr_ul__s_v(sa)
    call check('memcpy shorts', sa%a(0) == 32767 .and. sa%a(1) == -32767)
    allocate(fa)
    allocate(fa%a(0:0))
    fa%a = 0
    call check('memcpy float', memcpy_float(fa) == 0.1_4)
    allocate(fa)
    allocate(fa%a(0:0))
    fa%a(0) = -1.5_4
    view = cpp_bytes_view_ptrt__f(cpp_ptr_f_new_ta_l(fa, 0_8))
    call check('float view', size(view%b%a) == 4 .and. view%b%a(3) == -65 .and. view%b%a(2) == -64)
    view = cpp_bytes_view_ptrt__b(cpp_ptr_b_new_ta_l(arr_b([9, 8, 7]), 1_8))
    call check('byte view', size(view%b%a) == 2 .and. view%b%a(0) == 8)
    allocate(sa)
    allocate(sa%a(0:0))
    sa%a(0) = -2
    view = cpp_bytes_view_ptrt__s(cpp_ptr_s_new_ta_l(sa, 0_8))
    call check('short view', size(view%b%a) == 2 .and. view%b%a(0) == -2 .and. view%b%a(1) == -1)
    view = cpp_bytes_view_ptrt__i(cpp_ptr_i_new_ta_l(arr_i([4294967295_8, 1_8]), 0_8))
    call check('int view', size(view%b%a) == 8 .and. view%b%a(3) == -1 .and. view%b%a(4) == 1)
    call check('bytes view cptr', same(cpp_bytes_view_cptr(view), view))
    pd = cpp_ptr_d_new_ta_l(arr_d([1.0_8, 2.0_8, 3.0_8]), 0_8)
    q%b => null()
    pd2 = cpp_mv_poststep_ptrtr_i__d(pd, 1)
    call check('PostStep', pd2%o == 0 .and. pd%o == 1)

    ! ---- binary files
    dir = 'crtcheck_f90.tmp'
    call execute_command_line('rm -rf ' // dir // ' && mkdir ' // dir, exitstat=st)
    name = cpp_r_lit(dir // '/t.bin')
    fp => cpp_r_fopen(name, cpp_r_lit('w+b'))
    call check('fopen w+b', associated(fp))
    k = cpp_mem_fwrite_cptr_ul_ul_cfile(cpp_r_lit('STAMP'), 1_8, 5_8, fp)
    k = cpp_r_fwritei1(42, fp)
    pos = cpp_r_ftell(fp)
    k = cpp_r_fwrited1(3.5_8, fp)
    k = cpp_r_fwritef1(0.25_4, fp)
    k = cpp_mem_fwrite_ptrt_ul_ul_cfile__i(cpp_ptr_i_new_ta_l(arr_i([7_8, -8_8 + 4294967296_8, 9_8]), 0_8), 4_8, 3_8, fp)
    r = cpp_r_fseek_cfile_l_i(fp, pos, 0)
    k = cpp_r_fwrited1(4.5_8, fp)
    call cpp_r_rewind(fp)
    stamp = cpp_r_bufof(16, arr_i([integer(8) ::]))
    k = cpp_mem_fread_cptr_ul_ul_cfile(stamp, 1_8, 5_8, fp)
    i4 = 0
    dv = 0
    f4 = 0
    call check('binary round trip', k == 5 .and. cpp_r_strncmp(stamp, cpp_r_lit('STAMP'), 5_8) == 0 .and. &
         cpp_r_freadi1(i4, fp) == 1 .and. i4 == 42 .and. cpp_r_freadd1(dv, fp) == 1 .and. dv == 4.5_8 .and. &
         cpp_r_freadf1(f4, fp) == 1 .and. f4 == 0.25_4)
    ia => arr_i([0_8, 0_8, 0_8, 0_8])
    k = cpp_mem_fread_ptrt_ul_ul_cfile__i(cpp_ptr_i_new_ta_l(ia, 0_8), 4_8, 4_8, fp)
    call check('short fread', k == 3 .and. ia%a(0) == 7 .and. ia%a(1) == -8 .and. ia%a(2) == 9 .and. cpp_r_feof(fp) == 1)
    call check('fgetc at end', cpp_r_fgetc(fp) == -1)
    call check('ftell at end', cpp_r_ftell(fp) == 5 + 4 + 8 + 4 + 12)
    call check('fseek negative', cpp_r_fseek_cfile_l_i(fp, -1_8, 0) == -1)
    call check('fseek end', cpp_r_fseek_cfile_l_i(fp, -4_8, 2) == 0 .and. cpp_r_ftell(fp) == 29 .and. cpp_r_feof(fp) == 0)
    i4 = 0
    call check('fread after seek', cpp_r_freadi1(i4, fp) == 1 .and. i4 == 9)
    call check('fflush', cpp_r_fflush(fp) == 0)
    call check('fclose', cpp_r_fclose(fp) == 0)
    fp => cpp_r_fopen(name, cpp_r_lit('r'))
    line = cpp_r_buf_i(8)
    call check('fgets', .not. cpp_cptr_isnull_c(cpp_r_fgets(line, 4, fp)) .and. cpp_r_strcmp(line, cpp_r_lit('STA')) == 0)
    ia => arr_i([286331153_8, 572662306_8])
    r = cpp_r_fseek_cfile_l_i(fp, -6_8, 2)
    k = cpp_mem_fread_ptrt_ul_ul_cfile__i(cpp_ptr_i_new_ta_l(ia, 0_8), 4_8, 2_8, fp)
    call check('fread partial element', k == 1 .and. ia%a(0) == 655359 .and. ia%a(1) == 572653568 .and. cpp_r_feof(fp) == 1)
    r = cpp_r_fclose(fp)
    call check('fopen missing', .not. associated(cpp_r_fopen(cpp_r_lit(dir // '/none'), cpp_r_lit('r'))))
    ! glibc: a directory opens for reading, and the first read fails (ferror set, feof not); r+ and w fail
    fp => cpp_r_fopen(cpp_r_lit(dir), cpp_r_lit('r'))
    call check('fopen directory', associated(fp))
    if (associated(fp)) then
      call check('fopen directory reads', cpp_cptr_isnull_c(cpp_r_fgets(cpp_r_buf_i(8), 8, fp)) .and. cpp_r_feof(fp) == 0 &
           .and. cpp_r_ferror(fp) == 1 .and. cpp_r_fgetc(fp) == -1 .and. cpp_r_feof(fp) == 0 .and. cpp_r_fclose(fp) == 0)
    end if
    call check('fopen directory r+ w', .not. associated(cpp_r_fopen(cpp_r_lit(dir), cpp_r_lit('r+'))) .and. &
         .not. associated(cpp_r_fopen(cpp_r_lit(dir), cpp_r_lit('w'))))
    fp => cpp_r_fopen(name, cpp_r_lit('r'))
    call check('ferror of a file', cpp_r_ferror(fp) == 0)
    r = cpp_r_fclose(fp)
    call check('fopen bad mode', .not. associated(cpp_r_fopen(name, cpp_r_lit('x'))))
    ! text lines: fgets and the end-of-file flag
    an = cpp_r_lit(dir // '/l.txt')
    fp => cpp_r_fopen(an, cpp_r_lit('w'))
    r = cpp_r_fprintf(fp, cpp_r_lit('ab' // achar(10) // 'cdef' // achar(10) // 'g'))
    r = cpp_r_fclose(fp)
    fp => cpp_r_fopen(an, cpp_r_lit('r'))
    lb = cpp_r_buf_i(16)
    sline = ''
    do
      if (cpp_cptr_isnull_c(cpp_r_fgets(lb, 4, fp))) exit
      sline = sline // '[' // cstr(lb) // ']' // achar(48 + cpp_r_feof(fp))
    end do
    call check('fgets lines', sline == '[ab' // achar(10) // ']0[cde]0[f' // achar(10) // ']0[g]1' .and. cpp_r_feof(fp) == 1)
    r = cpp_r_fclose(fp)
    ! fprintf of every argument kind the translator passes
    fp => cpp_r_fopen(cpp_r_lit(dir // '/t.txt'), cpp_r_lit('w'))
    r = cpp_r_fprintf(fp, cpp_r_lit('%-6s|%3d|%5.2f|%c|%zu|%s|%d|%hu|%u|%c|%d|%s|%x|%ld|%lld|%.3f' // achar(10)), &
         cpp_r_lit('ab'), -5_1, 1.25_4, cpp_u8(int(ichar('Z'), 1)), cpp_u64(-1_8), mkstr('str'), 7, cpp_u16(-1_2), &
         cpp_u32(-1_4), cpp_u16(int(ichar('q'), 2)), .true., 'fstr', -1_2, -huge(1_8) - 1_8, cpp_u64(-huge(1_8) - 1_8), 2)
    r = cpp_r_fclose(fp)
    txt = readfile(dir // '/t.txt')
    call check('fprintf argument kinds', txt == 'ab    | -5| 1.25|Z|18446744073709551615|str|7|65535|4294967295|q|1' // &
         '|fstr|ffffffff|-9223372036854775808|-9223372036854775808|2.000' // achar(10))
    nofile => null()
    call check('fprintf null file', cpp_r_fprintf(nofile, cpp_r_lit('x')) == -1)
    sb = cpp_r_buf_i(8)
    call check('snprintf truncation', cpp_r_snprintf_cptr_ul_cptr_oa(sb, 4_8, cpp_r_lit('%d'), 123456) == 6 .and. &
         cpp_r_strcmp(sb, cpp_r_lit('123')) == 0)
    call check('snprintf size 0', cpp_r_snprintf_cptr_ul_cptr_oa(sb, 0_8, cpp_r_lit('%s'), cpp_r_lit('abc')) == 3 .and. &
         cpp_r_strcmp(sb, cpp_r_lit('123')) == 0)
    fb = cpp_r_format(cpp_r_lit('%f %+.2e %5.1f %G %a'), d(-2251799813685248_8), d(9218868437227405312_8), &
         d(-4503599627370496_8), cpp_std_qnan, -0.0_8)
    call check('printf nan/inf', size(fb) == 27 .and. transfer(fb, repeat(' ', 27)) == '-nan +inf  -inf NAN -0x0p+0')
    fb = cpp_r_format(cpp_r_lit('%5.1f|%-4d|%s'), 2.25_8, 3, nul)
    call check('format', transfer(fb, repeat(' ', size(fb))) == '  2.2|3   |(null)')
    call check('fileno/ferror/setvbuf', cpp_r_fileno(cpp_r_stdout_x) == 3 .and. cpp_r_ferror(cpp_r_stdout_x) == 0 .and. &
         cpp_r_setvbuf(cpp_r_stdout_x, nul, 0, 0_8) == 0)
    call check('std streams', associated(cpp_r_stdout_x) .and. associated(cpp_r_stderr_x) .and. &
         .not. associated(cpp_r_stdout_x, cpp_r_stderr_x) .and. cpp_r_fflush(cpp_r_stdout_x) == 0)
    call execute_command_line('rm -rf ' // dir, exitstat=st)

    ! ---- tuples, atomics, once, defer, chrono, pairs, exceptions
    da => arr_d([1.0_8, 2.0_8])
    da2 => arr_d([1.0_8, 2.0_8])
    ta => tup(1, 2.5_8, cpp_ptr_d_new_ta_l(da, 0_8), .true., 3)
    tb => tup(1, 2.5_8, cpp_ptr_d_new_ta_l(da, 0_8), .true., 3)
    tc => tup(1, 2.5_8, cpp_ptr_d_new_ta_l(da2, 0_8), .true., 3)
    td => tup(1, 2.5_8, cpp_ptr_d_new_ta_l(da, 0_8), .true., 4)
    call check('tuple equal', cpp_tupn_compareto(ta, tb) == 0)
    call check('tuple identity', cpp_tupn_compareto(ta, tc) /= 0 .and. cpp_tupn_compareto(ta, tc) == -cpp_tupn_compareto(tc, ta))
    call check('tuple order', cpp_tupn_compareto(ta, td) < 0 .and. cpp_tupn_compareto(td, ta) > 0)
    oc => ta%clone()
    select type (oc)
    class is (cpp_tupn)
      call check('tuple clone', cpp_tupn_compareto(oc, ta) == 0 .and. .not. associated(oc, ta))
    class default
      call check('tuple clone', .false.)
    end select
    te => cpp_tupn_new_0()
    oc => ta
    call te%assign(oc)
    call check('tuple new_0 + assign', size(te%v) == 5 .and. cpp_tupn_compareto(te, ta) == 0 .and. &
         associated(te%v(3)%p, ta%v(3)%p))
    te => cpp_tupn_new_0()
    oc => te%clone()
    select type (oc)
    class is (cpp_tupn)
      call check('tuple new_0 + clone', size(oc%v) == 0 .and. size(te%v) == 0 .and. .not. associated(oc, te))
    class default
      call check('tuple new_0 + clone', .false.)
    end select
    oc => ta%clone()
    call cpp_tupn_assign_check(oc, ta)
    al => cpp_atomic_l_new_0()
    call check('fetch_add', cpp_atomic_l_fetch_add(al, 5_8, 0) == 0 .and. cpp_atomic_l_fetch_add(al, 2_8, 0) == 5 .and. al%v == 7)
    al => cpp_atomic_l_new_t(-1_8)
    k = cpp_atomic_l_fetch_add(al, 1_8, 0)
    call check('atomic ulong wraps', al%v == 0)
    allocate(ad)
    ad%v = 1.5_8
    dv = 1.0_8
    call check('compare_exchange', .not. cpp_atomic_d_compare_exchange(ad, dv, 3.0_8) .and. dv == 1.5_8 .and. &
         cpp_atomic_d_compare_exchange(ad, dv, 3.0_8) .and. ad%v == 3.0_8)
    az => cpp_atomic_z_new_t(.true.)
    oc => az%clone()
    az%v = .false.
    select type (oc)
    class is (cpp_atomic_z)
      call check('atomic bool clone', oc%v .and. .not. az%v)
    class default
      call check('atomic bool clone', .false.)
    end select
    ai7 => cpp_atomic_i_new_t(7)
    ad => cpp_atomic_d_new_0()
    call check('atomic int', ai7%v == 7 .and. ad%v == 0.0_8)
    once => cpp_onceflag_new()
    allocate(cnt)
    act => cnt
    call cpp_std_call_once(once, act)
    call cpp_std_call_once(once, act)
    call check('call_once', cnt%n == 1)
    df => cpp_defer_new(act)
    call df%dispose()
    call df%dispose()
    call check('defer', cnt%n == 2)
    df => cpp_defer_new(act)
    call cpp_defer_cancel(df)
    call df%dispose()
    call check('defer cancel', cnt%n == 2)
    t0 = cpp_timepoint_now()
    call check('duration', cpp_duration_count(cpp_duration_clone(cpp_timepoint_op_sub(cpp_timepoint_now(), &
         cpp_timepoint_clone(t0)))) >= 0.0_8)
    call check('qnan', bits(cpp_std_qnan) == 9221120237041090560_8)
    x1 => null()
    call check('nop', cpp_std_nop(x1) == 0)
    allocate(pr)
    allocate(pr%first%p, source=4)
    allocate(pr%second%p, source=0.5_8)
    call check('get0/get1', cpp_std_get0_pairab__id(pr) == 4 .and. cpp_std_get1_pairab__id(pr) == 0.5_8 .and. &
         associated(cpp_std_get0_pairab__od(pr, mk, 0), pr%first))
    dp => cpp_std_get1_pairab__id(pr)
    dp = 0.75_8
    select type (pp => pr%second%p)
    type is (real(8))
      call check('get1 is a reference', pp == 0.75_8)
    end select
    se => cpp_sysexception_new('InvalidOperationException', 'm')
    call check('SysException', se%name == 'InvalidOperationException' .and. se%message == 'm')
    oc => se
    call cpp_std_rethrow(oc)
    call check('Rethrow', associated(cpp_exc, se))
    cpp_exc => null()
    call cpp_dbg_caught(oc)
    ec => cpp_errorcode_new()
    call check('error_code', .not. cpp_errorcode_bool(ec))
    ec%v = 2
    call check('error_code set', cpp_errorcode_bool(ec))
    call check('one-thread objects', associated(cpp_thread_new()) .and. associated(cpp_mutex_new()) .and. &
         associated(cpp_condvar_new()) .and. associated(cpp_lock_new()))
    call check('hardware_concurrency', cpp_sys_hardware_concurrency() > 0)
    ! destructors
    allocate(d1, d2)
    ndisposed = 0
    allocate(oa)
    allocate(oa%a(0:2))
    oa%a(0)%p => d1
    oa%a(2)%p => d2
    x1 => oa
    call cpp_dtor_run(x1)
    call check('Dtor.Run array', ndisposed == 2 .and. d1%n == 1 .and. d2%n == 1)
    x1 => d1
    call cpp_dtor_run(x1)
    call cpp_mv_delete(x1)
    x2 => null()
    call cpp_dtor_run(x2)
    call cpp_mv_delete(x2)
    call cpp_mem_free(x2)
    call check('Dtor.Run / Mv.Delete', d1%n == 3)
    ! Box<bool> and a Ptr<long> to a field
    zt = .false.
    allocate(zr)
    zr%p => zt
    rg => zr
    bz => cpp_box_z_of(rg)
    lp => cpp_box_z_v(bz)
    lp = .true.
    call check('Box<bool>', zt)
    allocate(lr)
    lt = 5
    lr%p => lt
    ral => lr
    x1 => null()
    t = cpp_r_time_ptrl(cpp_ptr_l_new_o_refatt(x1, ral))
    call check('time into a field', lt == t .and. t > 1700000000_8)
    la => arr_l([0_8])
    t = cpp_r_time_ptrl(cpp_ptr_l_new_ta_l(la, 0_8))
    call check('time', la%a(0) == t .and. cpp_r_difftime(t + 3, t) == 3.0_8 .and. cpp_r_time_ptrl(cpp_ptr_l()) >= t)

    ! ---- math corners
    call check('CopySign NaN sign', cpp_math_copysign_dd(1.0_8, d(-2251799813685248_8)) == -1.0_8 .and. &
         bits(cpp_math_copysign_dd(-0.0_8, 1.0_8)) == 0)
    call check('Truncate', cpp_math_truncate_d(-2.7_8) == -2.0_8 .and. bits(cpp_math_truncate_d(-0.5_8)) == -huge(1_8) - 1)
    call check('Ceiling -0', bits(cpp_math_ceiling_d(-0.5_8)) == -huge(1_8) - 1 .and. cpp_math_ceiling_d(0.2_8) == 1.0_8)
    call check('Floor', bits(cpp_math_floor_d(-0.0_8)) == -huge(1_8) - 1 .and. cpp_math_floor_d(-0.5_8) == -1.0_8 .and. &
         cpp_math_floor_d(1.0e300_8) == 1.0e300_8)
    call check('round', cpp_libm_round(2.5_8) == 3.0_8 .and. cpp_libm_round(-2.5_8) == -3.0_8 .and. &
         cpp_libm_round(0.49999999999999994_8) == 0.0_8 .and. bits(cpp_libm_round(-0.4_8)) == -huge(1_8) - 1)
    call check('pow errors', cpp_math_pow_dd(0.0_8, -1.0_8) > huge(1.0_8) .and. cpp_math_pow_dd(-0.0_8, -3.0_8) < -huge(1.0_8) &
         .and. bits(cpp_math_pow_dd(-8.0_8, 1.0_8 / 3)) == -2251799813685248_8 .and. cpp_math_pow_dd(10.0_8, 400.0_8) > huge(1.0_8))
    call check('log errors', cpp_math_log_d(0.0_8) < -huge(1.0_8) .and. isnan(cpp_math_log_d(-1.0_8)) .and. &
         cpp_math_log10_d(-0.0_8) < -huge(1.0_8))
    call check('exp overflow', cpp_math_exp_d(1000.0_8) > huge(1.0_8) .and. cpp_math_cosh_d(1000.0_8) > huge(1.0_8) .and. &
         cpp_math_sinh_d(-1000.0_8) < -huge(1.0_8))
    call check('Log2 powers', cpp_math_log2_d(1024.0_8) == 10.0_8 .and. cpp_math_log2_d(5.0e-324_8) == -1074.0_8)
    call check('Conv D2I', cpp_conv_d2i(d(-2251799813685248_8)) == -huge(1_4) - 1 .and. cpp_conv_d2i(2147483647.9_8) == huge(1_4) &
         .and. cpp_conv_d2i(-2147483648.9_8) == -huge(1_4) - 1 .and. cpp_conv_d2i(2147483648.0_8) == -huge(1_4) - 1 .and. &
         cpp_conv_d2i(-7.9_8) == -7)
    call check('Conv D2L', cpp_conv_d2l(-9.3e18_8) == -huge(1_8) - 1 .and. cpp_conv_d2l(1.5e18_8) == 1500000000000000000_8)
    call check('Conv D2UL', cpp_conv_d2ul(1.0e19_8) == 10000000000000000000_8 - 18446744073709551616_8 .and. &
         cpp_conv_d2ul(-1.0_8) == -1_8 .and. cpp_conv_d2ul(d(-2251799813685248_8)) == 0)
    call check('Conv D2SB', cpp_conv_d2sb(200.7_8) == -56 .and. cpp_conv_d2sb(-3.5_8) == -3)

    ! ---- char arrays and strings
    p = cpp_r_litn('OPENSWMM_HS_V1', 16)
    call check('LitN', size(p%b%a) == 16 .and. p%b%a(14) == 0 .and. p%b%a(15) == 0 .and. p%b%a(0) == ichar('O'))
    q = cpp_r_copybuf(p)
    q%b%a(0) = int(ichar('X'), 1)
    call check('CopyBuf', p%b%a(0) == ichar('O') .and. q%o == p%o .and. cpp_cptr_isnull_c(cpp_r_copybuf(nul)))
    q = cpp_r_bufof(6, arr_i([97_8, 98_8]))
    call check('BufOf', size(q%b%a) == 6 .and. all(q%b%a == [97_1, 98_1, 0_1, 0_1, 0_1, 0_1]))
    oa => cpp_r_bufs(5, 24)
    call check('Bufs', size(oa%a) == 5 .and. bufsize(oa%a(4)%p) == 24)
    call check('Buf', bsz(cpp_r_buf_i(0)) == 1 .and. bsz(cpp_r_lit('')) == 1)
    q = cpp_r_buf_i(4)
    call cpp_r_memcpybuf(q, cpp_r_lit('abcdefg'))
    call check('memcpyBuf', all(q%b%a == [97_1, 98_1, 99_1, 100_1]))
    call check('strcmp unsigned', cpp_r_strcmp(cbuf([233, 0]), cpp_r_lit('e')) > 0 .and. &
         cpp_r_strcmp(cpp_r_lit('ab'), cpp_r_lit('abc')) == -ichar('c') .and. cpp_r_strcmp(cbuf([97, 0]), cpp_r_lit('a')) == 0)
    call check('strncmp/memcmp', cpp_r_strncmp(cpp_r_lit('abcx'), cpp_r_lit('abcy'), 3_8) == 0 .and. &
         cpp_r_strncmp(cpp_r_lit('ab'), cpp_r_lit('ac'), 5_8) < 0 .and. cpp_r_memcmp(cbuf([1, 0, 3]), cbuf([1, 0, 2]), 3_8) == 1)
    s = cpp_r_lit('a,b,,c')
    call check('strchr', oof(cpp_r_strchr(s, ichar(','))) == 1 .and. oof(cpp_r_strchr(s, 0)) == 6 .and. &
         cpp_cptr_isnull_c(cpp_r_strchr(s, ichar('z'))))
    call check('strrchr', oof(cpp_r_strrchr(s, ichar(','))) == 4 .and. cpp_cptr_isnull_c(cpp_r_strrchr(s, ichar('z'))))
    call check('strstr', oof(cpp_r_strstr(s, cpp_r_lit(',,'))) == 3 .and. cpp_cptr_isnull_c(cpp_r_strstr(s, cpp_r_lit('x'))) &
         .and. same(cpp_r_strstr(s, cpp_r_lit('')), s))
    sline = ''
    e = cpp_r_strtok(s, cpp_r_lit(','))
    do while (.not. cpp_cptr_isnull_c(e))
      sline = sline // cstr(e) // '|'
      e = cpp_r_strtok(nul, cpp_r_lit(','))
    end do
    call check('strtok', sline == 'a|b|c|')
    q = cpp_r_buf_i(8)
    q%b%a = 85
    e = cpp_r_strncpy(q, cpp_r_lit('ab'), 5_8)
    call check('strncpy', all(q%b%a == [97_1, 98_1, 0_1, 0_1, 0_1, 85_1, 85_1, 85_1]))
    call check('strlen', cpp_r_strlen(cpp_r_lit('abc')) == 3 .and. cpp_r_strlen(cbuf([65, 66])) == 2)
    call check('toupper/tolower', cpp_r_toupper(ichar('a')) == ichar('A') .and. cpp_r_toupper(ichar('{')) == ichar('{') .and. &
         cpp_r_tolower(ichar('Q')) == ichar('q') .and. cpp_r_tolower(-1) == -1)
    call get_environment_variable('HOME', home, hl, st)
    call check('getenv', cstr(cpp_r_getenv(cpp_r_lit('HOME'))) == home(1:hl) .and. &
         cpp_cptr_isnull_c(cpp_r_getenv(cpp_r_lit('NO_SUCH_VARIABLE_CRTCHECK'))))
    write(*, '(a,i0,a,i0)') 'units cases: ', nunit, ', failures: ', nfail
  contains
    logical function diffi(pi)
      type(cpp_ptr_i), intent(in) :: pi
      diffi = associated(pi%a)
    end function
    function cpp_mem_memcpy_ptrt_cptr_ul__s_v(sa_) result(vv)
      class(cpp_arr_s), pointer, intent(in) :: sa_
      type(cpp_cptr) :: vv
      type(cpp_ptr_s) :: ps
      vv = cbuf([255, 127, 1, 128])
      ps = cpp_mem_memcpy_ptrt_cptr_ul__s(cpp_ptr_s_new_ta_l(sa_, 0_8), vv, 4_8)
    end function
    real(4) function memcpy_float(fa_)
      class(cpp_arr_f), pointer, intent(in) :: fa_
      class(cpp_arr_f), pointer :: src
      type(cpp_ptr_f) :: pf
      allocate(src)
      allocate(src%a(0:0))
      src%a(0) = 0.1_4
      pf = cpp_mem_memcpy_ptrt_cptr_ul__f(cpp_ptr_f_new_ta_l(fa_, 0_8), cpp_bytes_view_ptrt__f(cpp_ptr_f_new_ta_l(src, 0_8)), 4_8)
      memcpy_float = fa_%a(0)
    end function
    integer function bufsize(x)
      class(*), pointer, intent(in) :: x
      bufsize = -1
      select type (x)
      type is (cpp_cptr)
        bufsize = size(x%b%a)
      end select
    end function
    function tup(a, b, c, dd, e5) result(t)
      integer, intent(in) :: a, e5
      real(8), intent(in) :: b
      type(cpp_ptr_d), intent(in) :: c
      logical, intent(in) :: dd
      class(cpp_tupn), pointer :: t
      class(*), pointer :: y1, y2, y3, y4, y5
      allocate(y1, source=a)
      allocate(y2, source=b)
      allocate(y3, source=c)
      allocate(y4, source=dd)
      allocate(y5, source=e5)
      t => cpp_tupn_new_oa(y1, y2, y3, y4, y5)
    end function
    function readfile(fn) result(s_)
      character(len=*), intent(in) :: fn
      character(len=:), allocatable :: s_
      integer :: u, sz, ios
      open(newunit=u, file=fn, access='stream', form='unformatted', status='old', iostat=ios)
      if (ios /= 0) then
        s_ = ''
        return
      end if
      inquire(unit=u, size=sz)
      allocate(character(len=sz) :: s_)
      if (sz > 0) read(u) s_
      close(u)
    end function
  end subroutine units

  !> microseconds per call of the hot paths (no reference: a record for comparison between versions)
  subroutine speed()
    type(cpp_cptr) :: f, nm, buf, num, inum, nm2, tf
    class(set_i), pointer :: s1, s2, s3
    class(cpp_action_i), pointer :: a1, a2, a3
    class(cpp_refi), pointer :: r1, r2, r3
    integer(1), allocatable :: fb(:)
    integer :: i, k
    integer(4) :: r
    integer(8) :: c0, c1, rate
    real(8) :: x, acc
    character(len=40), parameter :: NAMES(9) = [character(len=40) :: 'R.Format %-16s %14.3f %8.2f %10d', &
         'snprintf %02d/%02d/%04d %02d:%02d', 'sscanf %d:%d:%d', 'strtod', 'atoi', 'strcmp', 'R.Lit', 'Math.Pow', &
         'Math.Exp']
    f = cpp_r_lit('%-16s %14.3f %8.2f %10d' // achar(10))
    nm = cpp_r_lit('Conduit C1')
    nm2 = cpp_r_lit('Conduit C2')
    buf = cpp_r_buf_i(64)
    num = cpp_r_lit('  123.456e-1 ')
    inum = cpp_r_lit(' 4711')
    tf = cpp_r_lit('%02d/%02d/%04d %02d:%02d')
    allocate(s1, s2, s3)
    s1%k = 1
    s2%k = 2
    s3%k = 3
    a1 => s1
    a2 => s2
    a3 => s3
    r1 => cpp_r_refi(a1)
    r2 => cpp_r_refi(a2)
    r3 => cpp_r_refi(a3)
    acc = 0
    do k = 1, 9
      call system_clock(c0, rate)
      do i = 1, 200000
        select case (k)
        case (1)
          fb = cpp_r_format(f, nm, 1234.5678_8, 0.25_8, 42)
        case (2)
          r = cpp_r_snprintf_cptr_ul_cptr_oa(buf, 32_8, tf, 1, 2, 2020, 3, 4)
        case (3)
          r = cpp_r_sscanf(cpp_r_lit('12:30:45'), cpp_r_lit('%d:%d:%d'), r1, r2, r3)
        case (4)
          x = cpp_r_strtod_cptr(num)
          acc = acc + x
        case (5)
          r = cpp_r_atoi(inum)
        case (6)
          r = cpp_r_strcmp(nm, nm2)
        case (7)
          buf = cpp_r_lit('CONDUITS')
        case (8)
          acc = acc + cpp_math_pow_dd(1.7_8, 0.6667_8 + i * 1.0e-9_8)
        case (9)
          acc = acc + cpp_math_exp_d(0.3_8 + i * 1.0e-9_8)
        end select
      end do
      call system_clock(c1)
      write(*, '(a40,f10.3,a)') NAMES(k), real(c1 - c0, 8) / real(rate, 8) / 200000.0_8 * 1.0e6_8, ' us/call'
      if (k == 7) buf = cpp_r_buf_i(64)
    end do
    if (acc == 1.0_8) write(*, *) acc
  end subroutine speed

  !> a cloned key is a copy: changing the clone's item list leaves the original as it was
  subroutine cpp_tupn_assign_check(oc, ta)
    class(cpp_object), pointer, intent(in) :: oc
    class(cpp_tupn), pointer, intent(in) :: ta
    select type (oc)
    class is (cpp_tupn)
      oc%v(1)%p => null()
      call check('tuple clone is a copy', associated(ta%v(1)%p) .and. size(oc%v) == size(ta%v))
    class default
      call check('tuple clone is a copy', .false.)
    end select
  end subroutine cpp_tupn_assign_check

  function h_mk() result(zres)
    class(*), pointer :: zres
    zres => null()
  end function h_mk
  integer function oof(p)
    type(cpp_cptr), intent(in) :: p
    oof = p%o
  end function
  integer function bsz(p)
    type(cpp_cptr), intent(in) :: p
    bsz = size(p%b%a)
  end function
  logical function same(p, q)
    type(cpp_cptr), intent(in) :: p, q
    same = associated(p%b, q%b) .and. p%o == q%o
  end function
  logical function cpp_cptr_isnull_c(p)
    type(cpp_cptr), intent(in) :: p
    cpp_cptr_isnull_c = .not. associated(p%b)
  end function

end module ckunits

program crtcheck
  use ckutil
  use ckunits
  implicit none
  character(len=32) :: what, arg
  integer :: n
  call get_command_argument(1, what)
  select case (what)
  case ('libm')
    n = 300000
    if (command_argument_count() > 1) then
      call get_command_argument(2, arg)
      read(arg, *) n
    end if
    call libm_cases(n)
  case ('printf')
    call printf_cases()
  case ('scanf')
    call scanf_cases()
  case ('misc')
    call misc_cases()
  case ('units')
    call units()
  case ('speed')
    call speed()
  case default
    stop 'crtcheck: libm N | printf | scanf | misc | units | speed'
  end select
end program crtcheck
