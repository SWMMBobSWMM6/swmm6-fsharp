// manifest.mjs - checks that every entry of ts/cs2ts_runtime.txt that belongs to part A (CPtr, Str, Vec, Iter,
// RevIter, Ptr, CSpan, Pair, Opt, Arr, M, Mv, Dtor, Defer, Box, Conv, the exceptions, cpp.L and the references) names a
// member that the compiled runtime has. Run after run.sh: node manifest.mjs [path to the compiled index.js].
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const idx = process.argv[2] ?? path.join(here, 'build/js/cpp/index.js');
const cpp = await import(pathToFileURL(path.resolve(idx)).href);
const mine = new Set(['CPtr', 'Str', 'Vec', 'Iter', 'RevIter', 'Ptr', 'CSpan', 'Pair', 'Opt', 'Arr', 'M', 'Mv', 'Dtor', 'Defer', 'Box',
	'Conv', 'CppException', 'RuntimeError', 'LogicError', 'InvalidArgument', 'OutOfRange', 'LengthError', 'DomainError', 'RangeError',
	'OverflowError', 'BadAlloc', 'BadOptionalAccess', 'BadFunctionCall', 'BadVariantAccess']);
// a sample instance of each class, for instance members and fields
const inst = {
	CPtr: cpp.CPtr.ZERO, Str: new cpp.Str(), Vec: new cpp.Vec(() => 0), Iter: cpp.Iter.ZERO, RevIter: cpp.RevIter.ZERO, Ptr: cpp.Ptr.ZERO,
	CSpan: cpp.CSpan.ZERO, Pair: new cpp.Pair(() => 0, () => 0), Opt: new cpp.Opt(() => 0), Defer: new cpp.Defer(() => { }),
	Box: cpp.Box.new_TA_l([0], 0), CppException: new cpp.CppException(),
};
const lines = fs.readFileSync(path.join(here, '../../ts/cs2ts_runtime.txt'), 'utf8').split('\n').filter(l => l.length > 0);
let checked = 0, missing = [];
function need(ok, line) { checked++; if (!ok) missing.push(line); }
for (const line of lines) {
	const [cs, ts] = line.split('\t');
	const m = /^(\w+(?: \w+)?) Cpp\.(\w+)/.exec(cs);
	if (cs.startsWith('helper cpp.L.')) { need(typeof cpp.L[ts.split(' ')[0].slice(6)] === 'function', line); continue; }
	if (/^helper cpp\.(ArrRef|fieldRef)/.test(cs)) { need(cpp[cs.split('.')[1].split(' ')[0]] !== undefined, line); continue; }
	if (cs.startsWith('new cell')) { need(typeof cpp.Cell === 'function', line); continue; }
	if (cs.startsWith('type long/ulong')) { need(typeof cpp.L === 'object', line); continue; }
	if (cs.startsWith('CPtr indexer')) { need(['get', 'set', 'ref'].every(k => typeof inst.CPtr[k] === 'function'), line); continue; }
	if (cs.startsWith('Str.b ') || cs.startsWith('Vec.a ')) { need(cs.startsWith('Str') ? inst.Str.b instanceof Uint8Array : Array.isArray(inst.Vec.a), line); continue; }
	if (m == null) continue;
	const kind = m[1], cls = m[2];
	if (!mine.has(cls)) continue;
	const C = cpp[cls];
	if (C === undefined) { need(false, line); continue; }
	const name = ts.replace(/^cpp\.\w+\./, '').replace(/\(.*$/, '').replace(/ .*$/, '');
	switch (kind) {
		case 'type': case 'base class': case 'catch': need(typeof C === 'function' || typeof C === 'object', line); break;
		case 'method': case 'property': need(typeof inst[cls]?.[name] === 'function', line); break;
		case 'field': need(inst[cls] !== undefined && name in inst[cls], line); break;
		case 'static': case 'operator': case 'conversion': case 'static field': need(C[name] !== undefined, line); break;
		case 'new': need(ts.startsWith('new ') ? typeof C === 'function' : typeof C[name] === 'function', line); break;
		default: need(false, 'unknown kind: ' + line);
	}
}
console.log(`manifest: ${checked} part A entries checked, ${missing.length} missing`);
for (const l of missing) console.log('  missing: ' + l);
process.exit(missing.length === 0 ? 0 : 1);
