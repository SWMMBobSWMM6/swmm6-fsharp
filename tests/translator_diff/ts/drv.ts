// TEST-ONLY driver: the same calls and output format as csdrv/drv.cs
import * as E from './gen/engine.js';
import { readFileSync } from 'fs';
const NT = process.argv[2];
const tests = readFileSync(NT + '/tests.txt', 'utf8').trim().split('\n').map(l => l.split(' '));
const [ls, ds] = readFileSync(NT + '/inputs.txt', 'utf8').trim().split('\n').map(l => l.split(' '));
const canon = (b: bigint) => (b >= -9007199254740991n && b <= 9007199254740991n) ? Number(b) : b;
const Ls = ls.map(s => canon(BigInt.asIntN(64, BigInt(s))));
const cnan = new Float64Array(new BigUint64Array([0xfff8000000000000n]).buffer)[0];
const Ds = ds.map(s => s === '-0' ? -0 : s === 'NaN' ? cnan : Number(s));
const bits = (v: number) => { const f = new Float64Array([v]); return new BigUint64Array(f.buffer)[0].toString(16).padStart(16, '0'); };
const out: string[] = [];
const T: any = (E as any).NumTest;
for (const [n, r, k] of tests) {
  const arr: any[] = k === 'l' ? Ls : Ds;
  for (const x of arr) for (const y of arr) {
    let s: string;
    try { const v = T[n](x, y); s = r === 'double' ? bits(v) : String(v); } catch (e) { s = 'E'; }
    out.push(n + ' ' + s);
  }
}
process.stdout.write(out.join('\n') + '\n');
