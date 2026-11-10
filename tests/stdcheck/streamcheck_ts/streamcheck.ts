// streamcheck.ts - the cases of streamcheck.cpp / streamcheck.cs through the TypeScript runtime's streams and file
// system (ts/src/cpp: streams.ts, fs.ts, fsnet.ts, rfs.ts), statement for statement as streamcheck.cs. Section a
// must equal the C++ output (but for the one glibc %#g quirk) and the C# and Go outputs; section b the C# and Go
// outputs (the C# port's own behaviour); section c the C++ and C# outputs. run_stream_ts.sh builds and compares.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.

import * as cpp from '../../../ts/src/cpp/index.js';

const { Ios, OStringStream, IStringStream, OFStream, IFStream, FStream, Streams, FsPath, Fs, SetW, SetFill, SetPrec } = cpp;
type IosBase = cpp.IosBase;

let sb = '';
function Bits(u: bigint): number { const v = new DataView(new ArrayBuffer(8)); v.setBigUint64(0, u, true); return v.getFloat64(0, true); }
function St(s: IosBase): string { return '' + (s.good() ? 'g' : '-') + (s.eof() ? 'e' : '-') + (s.fail() ? 'f' : '-') + (s.bad() ? 'b' : '-'); }
function G(v: number): string { const o = new OStringStream(); o.Put_d(v); return o.str_().toString(); }
function S(s: string): cpp.Str { return cpp.Str.new_CPtr(cpp.R.Lit(s)); }
function B(b: boolean): string { return b ? '1' : '0'; }
function W(s: string): void { sb += s; }
function Lit(s: string): cpp.CPtr { return cpp.R.Lit(s); }
function P(s: string): cpp.FsPath { return FsPath.new_Str(S(s)); }
/** R.Str: the C string as a C# string (Latin-1). */
function CStr(p: cpp.CPtr): string { const q: any = p; let r = ''; for (let i = q.O; q.B[i] !== 0; i++) r += String.fromCharCode(q.B[i] & 0xFF); return r; }
function Latin1(b: ArrayLike<number>, off: number, n: number): string { let r = ''; for (let i = 0; i < n; i++) r += String.fromCharCode(b[off + i] & 0xFF); return r; }
function cell<T>(v: T): cpp.Cell<T> { return new cpp.Cell<T>(v); }
function ptr<T>(a: T[]): cpp.Ptr<T> { return cpp.Ptr.new_TA_l(a, 0); }

function main(args: string[]): void {
	const sec = args.length > 0 ? args[0] : 'a';
	if (sec === 'a') A();
	else if (sec === 'b') Bsec();
	else C();
	const bytes = cpp.R.Buf_i(sb.length);
	for (let i = 0; i < sb.length; i++) (bytes as any).B[i] = sb.charCodeAt(i) & 0xFF;
	Streams.cout.write_CPtr_l(bytes, sb.length);
}

function C(): void {
	const f = IFStream.new_Str_i(S('build/streamcheck.bin'), Ios.in | Ios.binary);
	const b = [0];
	f.read_PtrT_l__i(ptr(b), 4);
	f.seekg(4, Ios.cur);
	f.read_PtrT_l__i(ptr(b), 4);
	W('cur at8 ' + b[0] + ' tellg ' + f.tellg() + ' ' + St(f) + '\n');
}

function A(): void {
	const vals = [0.0, Bits(0x8000000000000000n), 1.0, -1.0, 0.1, 0.5, 1.5, 2.5, 123.456, 1e-5,
		1.23456789e-7, 1e21, 1.7976931348623157e308, Bits(1n), 2.2250738585072014e-308, 1.0 / 3.0, 2.0 / 3.0, 100.0,
		1e15, 123456789012345678.0, 0.000123456, 9.999999, 99.995, 0.125, Bits(0x7ff8000000000000n),
		Bits(0x7ff0000000000000n), Bits(0xfff0000000000000n), 1e100, 5e-310, 3.14159265358979, -0.0004567,
		1234567.0, 0.95, 0.05, 1e-300, 6.02214076e23];
	const precs = [0, 1, 2, 3, 4, 6, 8, 10, 15, 16, 17, 20, -1];
	for (const v of vals) {
		for (let mode = 0; mode < 4; mode++)
			for (let sp = 0; sp < 2; sp++)
				for (const p of precs) {
					if (mode === 3 && p !== 6) continue;
					const o = new OStringStream();
					if (mode === 1) o.Manip_fixed();
					if (mode === 2) o.Manip_scientific();
					if (mode === 3) o.setf_i_i(Ios.fixed | Ios.scientific, Ios.floatfield);
					if (sp === 1) o.Manip_showpoint();
					o.precision_l(p);
					o.Put_d(v);
					W('d ' + mode + sp + ' ' + p + ' [' + o.str_() + ']\n');
				}
		const f = new OStringStream();
		f.Put_f(Math.fround(v));
		W('f [' + f.str_() + ']\n');
	}
	{
		const o = new OStringStream();
		o.Put_c(91).Manip_setw(10).Put_d(3.5).Put_CPtr(Lit('][')).Manip_left().Manip_setw(8).Manip_setfill(42).Put_i(-42).Put_c(93);
		o.Put_c(91).Manip_right().Manip_setw(6).Put_CPtr(Lit('ab')).Put_CPtr(Lit('][')).Manip_setw(2).Put_CPtr(Lit('abcdef')).Put_c(93);
		o.Put_c(91).Manip_setw(5).Manip_setfill(48).Put_u(7).Put_CPtr(Lit('][')).Put_i(123).Put_c(93);
		o.Manip_boolalpha().Put_c(91).Put_b(true).Put_c(32).Put_b(false).Put_c(93).Manip_noboolalpha().Put_c(91).Put_b(true).Put_b(false).Put_c(93);
		o.Put_c(91).Manip_setw(12).Manip_setfill(32).Manip_quoted_Str(S('a"b\\c')).Put_c(93);
		o.Put_c(91).Manip_quoted_CPtr(Lit('plain')).Put_c(93);
		o.Put_c(91).Manip_fixed().Manip_setprecision(3).Manip_setw(9).Put_d(2.0 / 3.0).Put_c(93);
		o.Put_c(91).Manip_defaultfloat().Manip_setprecision(4).Put_d(2.0 / 3.0).Put_c(93);
		o.Put_c(91).Put_l(-9223372036854775808n).Put_c(32).Put_ul(18446744073709551615n).Put_c(32).Put_s(-5).Put_c(93);
		o.Put_c(91).Put_c(120).Put_c(65).Put_c(93);
		W('w ' + o.str_() + '\n');
	}
	{
		const o = OStringStream.new_Str(S('abcdef'));
		o.Put_CPtr(Lit('XY'));
		W('init [' + o.str_() + '] tellp ' + o.tellp() + '\n');
		const q = new OStringStream();
		q.Put_CPtr(Lit('hello world'));
		q.seekp(6);
		q.Put_CPtr(Lit('W'));
		q.seekp(-1, Ios.end);
		q.Put_CPtr(Lit('D'));
		q.seekp(2, Ios.beg);
		q.seekp(1, Ios.cur);
		q.Put_CPtr(Lit('L'));
		W('seekp [' + q.str_() + '] tellp ' + q.tellp() + '\n');
	}
	{
		const is = IStringStream.new_Str(S('  3.25 -7 abc\nline two\n\nlast'));
		const d = cell(-1), i = cell(-1), s = new cpp.Str();
		is.Get_dr(d); W('x d ' + G(d.v) + ' ' + St(is) + '\n');
		is.Get_ir(i); W('x i ' + i.v + ' ' + St(is) + '\n');
		is.Get_Str(s); W('x s ' + s + ' ' + St(is) + '\n');
		const line = new cpp.Str();
		for (let k = 0; k < 4; k++) {
			Streams.getline_IStream_Str(is, line);
			W('x line [' + line + '] ' + St(is) + '\n');
		}
	}
	{
		const is = IStringStream.new_Str(S('12 4294967296 -2147483648 2147483648 -9223372036854775808 9000000000000000000 4294967295 7'));
		const a = cell(-1), b = cell(-1), c = cell(-1), e = cell(-1);
		is.Get_ir(a).Get_ir(b);
		W('x ints ' + a.v + ' ' + b.v + ' ' + St(is) + '\n');
		is.clear();
		is.Get_ir(c).Get_ir(e);
		W('x ints ' + c.v + ' ' + e.v + ' ' + St(is) + '\n');
		is.clear();
		const L = cell<cpp.L>(-1);
		is.Get_lr(L);
		W('x long ' + L.v + ' ' + St(is) + '\n');
		is.clear();
		const U = cell<cpp.L>(0), u = cell(0);
		is.Get_ulr(U);
		W('x ulong ' + U.v + ' ' + St(is) + '\n');
		is.clear();
		is.Get_ur(u);
		W('x uint ' + u.v + ' ' + St(is) + '\n');
	}
	{
		const is = IStringStream.new_Str(S('a,b,,c'));
		const f = new cpp.Str();
		while (Streams.getline_IStream_Str_c(is, f, 44).ok()) W('x field [' + f + '] ' + St(is) + '\n');
		W('x field end ' + St(is) + '\n');
	}
	{
		const is = IStringStream.new_Str(S('   \t\n  tail'));
		is.Manip_ws();
		const rest = new cpp.Str();
		Streams.getline_IStream_Str(is, rest);
		W('x ws [' + rest + '] ' + St(is) + '\n');
		const e = IStringStream.new_Str(S('   '));
		e.Manip_ws();
		W('x ws-end ' + St(e) + '\n');
	}
	{
		const is = IStringStream.new_Str(S('0123456789'));
		const buf: any = cpp.R.Buf_i(16);
		is.read_CPtr_l(buf, 4);
		W('r [' + Latin1(buf.B, 0, 4) + '] gc ' + is.gcount() + ' ' + St(is) + ' tellg ' + is.tellg() + '\n');
		W('r peek ' + is.peek());
		W(' get ' + is.get_());
		W(' get ' + is.get_() + ' ' + St(is) + '\n');
		is.ignore(3, 56);
		W('r ignore tellg ' + is.tellg() + ' ' + St(is) + '\n');
		is.seekg(-2, Ios.cur);
		W('r seekg cur tellg ' + is.tellg() + ' get ' + is.get_() + '\n');
		is.seekg(-3, Ios.end);
		W('r seekg end tellg ' + is.tellg() + ' get ' + is.get_() + '\n');
		is.read_CPtr_l(buf, 10);
		W('r short gc ' + is.gcount() + ' ' + St(is) + '\n');
		is.clear();
		is.seekg(0, Ios.beg);
		W('r rewind tellg ' + is.tellg() + ' get ' + is.get_() + ' ' + St(is) + '\n');
		is.ignore(100, -1);
		W('r ignore-all ' + St(is) + '\n');
		W('r get-end ' + is.get_());
		W(' ' + St(is) + '\n');
		W('r str [' + is.str_() + ']\n');
	}
	{
		{
			const f = OFStream.new_Str_i(S('build/streamcheck.bin'), Ios.trunc | Ios.binary);
			W('f open ' + B(f.is_open()) + ' ' + St(f) + '\n');
			const a = [1, -2, 300000];
			const d = [1.5, -0.25];
			f.write_PtrT_l__i(ptr(a), 12);
			f.write_PtrT_l__d(ptr(d), 16);
			f.write_CPtr_l(Lit('MAGIC'), 5);
			W('f tellp ' + f.tellp() + '\n');
			f.Put_d(3.25).Put_c(32).Put_i(42).Put_c(10);
			W('f tellp ' + f.tellp() + '\n');
			f.close();
			W('f closed ' + B(f.is_open()) + '\n');
		}
		{
			const f = OFStream.new_Str_i(S('build/streamcheck.bin'), Ios.app);
			f.Put_CPtr(Lit('tail\n'));
			f.Dispose();
		}
		{
			const f = IFStream.new_Str_i(S('build/streamcheck.bin'), Ios.in | Ios.binary);
			W('f in open ' + B(f.is_open()) + ' ' + St(f) + '\n');
			const a = [0, 0, 0];
			f.read_PtrT_l__i(ptr(a), 12);
			W('f ints ' + a[0] + ' ' + a[1] + ' ' + a[2] + ' gc ' + f.gcount() + ' tellg ' + f.tellg() + '\n');
			const d = [0, 0];
			f.read_PtrT_l__d(ptr(d), 16);
			W('f dbl ' + G(d[0]) + ' ' + G(d[1]) + ' tellg ' + f.tellg() + '\n');
			const m = cpp.R.Buf_i(6);
			f.read_CPtr_l(m, 5);
			W('f magic ' + CStr(m) + '\n');
			const x = cell(0), n = cell(0), t = new cpp.Str();
			f.Get_dr(x).Get_ir(n).Get_Str(t);
			W('f text ' + G(x.v) + ' ' + n.v + ' ' + t + ' ' + St(f) + '\n');
			f.seekg(-5, Ios.end);
			Streams.getline_IStream_Str(f, t);
			W('f end-5 [' + t + '] ' + St(f) + '\n');
			Streams.getline_IStream_Str(f, t);
			W('f after ' + St(f) + '\n');
			f.clear();
			f.seekg(0, Ios.end);
			W('f size ' + f.tellg() + '\n');
			f.seekg(4, Ios.beg);
			const b = [0];
			f.read_PtrT_l__i(ptr(b), 4);
			W('f at4 ' + b[0] + ' tellg ' + f.tellg() + '\n');
			f.seekg(1000, Ios.beg);
			f.read_PtrT_l__i(ptr(b), 4);
			W('f past-end gc ' + f.gcount() + ' ' + St(f) + '\n');
			f.close();
		}
		{
			const f = IFStream.new_Str_i(S('build/no-such-file.bin'), Ios.in);
			W('f missing open ' + B(f.is_open()) + ' ' + St(f) + '\n');
		}
	}
}

function Ec(e: cpp.ErrorCode): string { return (e as any).v + ' (' + e.message() + ')'; }

function Bsec(): void {
	{
		const is = IStringStream.new_Str(S('x\nlast'));
		const line = new cpp.Str();
		for (let k = 0; k < 3; k++) { Streams.getline_IStream_Str(is, line); W('b line [' + line + '] ' + St(is) + '\n'); }
	}
	{
		const is = IStringStream.new_Str(S('12abc 99999999999999999999 18446744073709551615 -1 1e400 inf 0x1p3 -.5 1e 5'));
		const i = cell(-1), L = cell<cpp.L>(0), U = cell<cpp.L>(0), u = cell(0), d = cell(0);
		is.Get_ir(i); W('b 12abc ' + i.v + ' ' + St(is) + '\n'); is.clear();
		is.Get_lr(L); W('b long-over ' + L.v + ' ' + St(is) + '\n'); is.clear();
		is.Get_ulr(U); W('b ulong-max ' + U.v + ' ' + St(is) + '\n'); is.clear();
		is.Get_ur(u); W('b uint-neg ' + u.v + ' ' + St(is) + '\n'); is.clear();
		for (let k = 0; k < 5; k++) { is.Get_dr(d); W('b dbl ' + G(d.v) + ' ' + St(is) + '\n'); is.clear(); }
		is.Get_ir(i); W('b last ' + i.v + ' ' + St(is) + '\n');
		i.v = 77; is.Get_ir(i); W('b after-eof ' + i.v + ' ' + St(is) + '\n');
		const fl = cell(0); is.clear(); is.str_Str(S(' 0.1 ')); is.Get_fr(fl); W('b float ' + G(fl.v) + ' ' + St(is) + '\n');
		const c = cell(0); is.str_Str(S('  q')); is.Get_cr(c); W('b char ' + c.v + ' ' + St(is) + '\n');
	}
	{
		const f = OFStream.new_Str_i(S('build/streamcheck.bin'), Ios.app);
		W('b app tellp ' + f.tellp() + '\n');
		f.close();
		const g = OFStream.new_CPtr_i(Lit('build/no-dir/x.txt'), Ios.out);
		W('b bad-open ' + B(g.is_open()) + ' ' + St(g) + '\n');
		g.Put_CPtr(Lit('dropped'));
		const h = new OFStream();
		W('b unopened ' + B(h.is_open()) + ' ' + St(h) + '\n');
		h.open_Str_i(S('build/streamcheck.txt'));
		h.Put_d(1.25).Manip_endl();
		W('b reopened ' + B(h.is_open()) + ' ' + St(h) + ' tellp ' + h.tellp() + '\n');
		h.seekp(0, Ios.beg);
		h.Put_CPtr(Lit('9'));
		h.seekp(0, Ios.end);
		h.Put_CPtr(Lit('z'));
		h.Dispose();
		const r = IFStream.new_FsPath_i(P('build/streamcheck.txt'), Ios.in);
		const t = new cpp.Str();
		Streams.getline_IStream_Str(r, t);
		W('b reread [' + t + '] ');
		Streams.getline_IStream_Str(r, t);
		W('[' + t + '] ' + St(r) + '\n');
		r.close();
		const dir = IFStream.new_Str_i(S('build'), Ios.in);
		W('b dir-open ' + B(dir.is_open()) + ' ' + St(dir) + '\n');
	}
	{
		const o = new OStringStream();
		o.str_Str(S('abc'));
		o.Put_CPtr(Lit('XY'));
		W('b str-set [' + o.str_() + '] tellp ' + o.tellp() + '\n');
		o.precision_l(3);
		const o2 = new OStringStream();
		o2.MoveFrom(o);
		o2.Put_d(1.0 / 3.0);
		o.Put_CPtr(Lit('q'));
		W('b moved [' + o2.str_() + '] [' + o.str_() + ']\n');
		const i1 = IStringStream.new_Str(S('one two'));
		const w = new cpp.Str();
		i1.Get_Str(w);
		const i2 = new IStringStream();
		i2.MoveFrom(i1);
		i2.Get_Str(w);
		W('b imoved [' + w + '] ' + St(i2) + ' str [' + i2.str_() + ']\n');
		const p = new OStringStream();
		p.Put_d(1.5).Manip_scientific().Put_d(1.5).Manip_fixed().Put_d(1.5).Manip_defaultfloat().Put_d(1.5).Manip_showpoint().Put_d(1.5).Manip_noshowpoint().Put_d(1.5);
		p.Put_SetW(new SetW(6)).Put_SetFill(new SetFill(46)).Put_SetPrec(new SetPrec(2)).Put_d(3.14159).Put_SetW(Streams.setw(4)).Put_SetFill(Streams.setfill(35)).Put_i(1);
		p.setf_i_i(Ios.left, Ios.adjustfield); p.width_l(5); p.Put_CPtr(Lit('ab')); p.unsetf(Ios.left); p.width_l(5); p.Put_CPtr(Lit('cd'));
		p.Put_f(Math.fround(Math.fround(1) / Math.fround(3))).Put_uc(66).Put_u((2147483647 * 2) >>> 0);
		W('b manip [' + p.str_() + '] w ' + p.width_() + ' f ' + p.fill_() + ' p ' + p.precision_() + '\n');
	}
	{
		const fs = new FStream(S('build/streamcheck.fs'), Ios.in | Ios.out);
		fs.Put_CPtr(Lit('fstream'));
		W('b fstream ' + B(fs.is_open()) + ' tellp ' + fs.tellp() + '\n');
		fs.close();
	}
	const paths = ['', '/', 'a', 'a/', '/a/b.c', 'a/b.tar.gz', '.hidden', 'dir/.hidden', 'a/..', '..', '.',
		'a/./b/../c', '/../x', '//a//b//', 'a/b/', 'x.', 'dir.d/file', '/a/b/../../..', '../a/../..', './', 'a//b', '/a'];
	for (const ps of paths) {
		const p = P(ps);
		W('p [' + ps + '] fn [' + p.filename() + '] par [' + p.parent_path() + '] stem [' + p.stem() + '] ext [' + p.extension() +
			'] abs ' + B(p.is_absolute()) + B(p.is_relative()) + ' has ' + B(p.has_filename()) + B(p.has_parent_path()) + B(p.has_extension()) +
			' norm [' + p.lexically_normal() + '] empty ' + B(p.empty()) + '\n');
		W('  join [' + p.Join_FsPath(P('x')) + '] [' + p.Join_Str(S('/abs')) + '] [' + p.Join_FsPath(new FsPath()) + '] [' + p.Join_CPtr(Lit('y/z')) + ']');
		const q = p.Clone(); q.replace_extension_FsPath(P('.out'));
		const q2 = p.Clone(); q2.replace_extension_Str(S('txt'));
		const q3 = p.Clone(); q3.replace_extension_FsPath();
		const q4 = p.Clone(); q4.replace_filename(P('new.inp'));
		const q5 = p.Clone(); q5.remove_filename();
		W(' rext [' + q + '] [' + q2 + '] [' + q3 + '] rfn [' + q4 + '] rmfn [' + q5 + '] str [' + p.string() + '] gs [' + p.generic_string() +
			'] cs [' + CStr(p.c_str()) + '] cmp ' + Math.sign(p.compare(P('a'))) + '\n');
	}
	{
		const a = P('x'), b = FsPath.new_CPtr(Lit('y'));
		a.Assign_FsPath(b); b.Assign_Str(S('w'));
		const c = new FsPath(); c.MoveFrom(a);
		W('p assign [' + a + '] [' + b + '] [' + c + '] ');
		c.clear();
		W('[' + c + '] ' + B(c.empty()) + '\n');
	}
	{
		const dir = P('build/fsdir');
		const ec = new cpp.ErrorCode();
		Fs.remove_FsPath(P('build/fsdir/x/y/f.txt'));
		Fs.remove_FsPath(P('build/fsdir/x/y/g.txt'));
		Fs.remove_FsPath(P('build/fsdir/x/y/h.txt'));
		Fs.remove_FsPath_ErrorCode(P('build/fsdir/x/y'), ec);
		W('fs mkdir ' + B(Fs.create_directories_FsPath(P('build/fsdir/x/y'))) + ' again ' + B(Fs.create_directories_FsPath_ErrorCode(P('build/fsdir/x/y'), ec)) + ' ' + Ec(ec) + '\n');
		const file = P('build/fsdir/x/y/f.txt');
		const o = OFStream.new_FsPath_i(file);
		o.Put_CPtr(Lit('0123456789'));
		o.close();
		W('fs exists ' + B(Fs.exists_FsPath(file)) + B(Fs.exists_FsPath(dir)) + B(Fs.exists_FsPath(P('build/fsdir/none'))) + B(Fs.exists_FsPath_ErrorCode(file, ec)) +
			' reg ' + B(Fs.is_regular_file_FsPath(file)) + B(Fs.is_regular_file_FsPath(dir)) + ' dir ' + B(Fs.is_directory_FsPath(file)) + B(Fs.is_directory_FsPath(dir)) + '\n');
		W('fs size ' + Fs.file_size_FsPath(file) + ' ec ' + Fs.file_size_FsPath_ErrorCode(file, ec) + ' ' + Ec(ec));
		W(' missing ' + Fs.file_size_FsPath_ErrorCode(P('build/fsdir/none'), ec) + ' ' + Ec(ec));
		W(' dir ' + Fs.file_size_FsPath_ErrorCode(dir, ec) + ' ' + Ec(ec) + '\n');
		for (const sp of ['build/fsdir/x/y/f.txt', 'build/fsdir', 'build/fsdir/none', '/dev/null']) {
			const s1 = Fs.status_FsPath(P(sp)), s2 = Fs.symlink_status_FsPath_ErrorCode(P(sp), ec), s3 = Fs.symlink_status_FsPath(P(sp));
			W('fs status ' + sp + ' ' + s1.type() + ' ' + s2.type() + ' ' + s3.Clone().type() + ' ' + B(Fs.exists_FileStatus(s1)) + B(Fs.is_regular_file_FileStatus(s1)) + B(Fs.is_directory_FileStatus(s1)) + B(Fs.is_symlink(s1)) + '\n');
		}
		const g = P('build/fsdir/x/y/g.txt');
		Fs.create_hard_link(file, g, ec); W('fs link ' + Ec(ec));
		Fs.create_hard_link(file, g, ec); W(' again ' + Ec(ec));
		Fs.create_hard_link(P('build/fsdir/none'), P('build/fsdir/x/y/n.txt'), ec); W(' missing ' + Ec(ec) + ' size ' + Fs.file_size_FsPath(g) + '\n');
		const h = P('build/fsdir/x/y/h.txt');
		Fs.rename_FsPath_FsPath_ErrorCode(g, h, ec); W('fs rename ' + Ec(ec) + ' ' + B(Fs.exists_FsPath(g)) + B(Fs.exists_FsPath(h)));
		Fs.rename_FsPath_FsPath_ErrorCode(g, h, ec); W(' missing ' + Ec(ec));
		Fs.rename_FsPath_FsPath(h, g); W(' plain ' + B(Fs.exists_FsPath(g)) + B(Fs.exists_FsPath(h)));
		Fs.rename_FsPath_FsPath_ErrorCode(g, file, ec); W(' over ' + Ec(ec) + ' ' + B(Fs.exists_FsPath(g)) + B(Fs.exists_FsPath(file)) + '\n');
		W('fs equiv ' + B(Fs.equivalent_FsPath_FsPath(file, P('./build/fsdir/x/../x/y//f.txt'))) + B(Fs.equivalent_FsPath_FsPath(file, dir)) + B(Fs.equivalent_FsPath_FsPath_ErrorCode(file, P('build/fsdir/none'), ec)) + ' ' + Ec(ec) +
			' ' + B(Fs.equivalent_FsPath_FsPath_ErrorCode(file, file, ec)) + ' ' + Ec(ec) + '\n');
		W('fs canon [' + Fs.canonical_FsPath_ErrorCode(P('build/fsdir/none'), ec) + '] ' + Ec(ec) + ' [' + Fs.weakly_canonical_FsPath(P('build/../build/fsdir/./x/')) + '] [' +
			Fs.weakly_canonical_FsPath_ErrorCode(P('/a/b/../c/.'), ec) + '] ' + Ec(ec) + ' [' + Fs.canonical_FsPath_ErrorCode(P('/x/../y'), ec) + '] ' + Ec(ec) + ' [' + Fs.canonical_FsPath(dir) + ']\n');
		W('fs abs [' + Fs.absolute(P('rel/p')) + '] [' + Fs.absolute(P('/abs/p')) + '] cwd [' + Fs.current_path() + '] tmp [' + Fs.temp_directory_path() + ']\n');
		W('fs remove ' + B(Fs.remove_FsPath(file)) + B(Fs.remove_FsPath(file)) + B(Fs.remove_FsPath(dir)));
		W(' ec ' + B(Fs.remove_FsPath_ErrorCode(P('build/fsdir/x'), ec)) + ' ' + Ec(ec));
		W(' ' + B(Fs.remove_FsPath_ErrorCode(P('build/fsdir/x/y'), ec)) + ' ' + Ec(ec));
		W(' ' + B(Fs.remove_FsPath_ErrorCode(P('build/fsdir/x/y'), ec)) + ' ' + Ec(ec) + '\n');
		const e2 = new cpp.ErrorCode();
		W('ec ' + B(e2.Bool()) + ' ' + e2.value());
		for (const v of [13, 17, 20, 21, 95, 5]) { (e2 as any).v = v; W(' ' + Ec(e2)); }
		e2.clear(); W(' ' + B(e2.Bool()) + '\n');
	}
}

main(process.argv.slice(2));
