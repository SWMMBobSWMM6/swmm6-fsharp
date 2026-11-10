// check2.ts - compare set 2 of the TypeScript glibc ports (glibm2.ts: cbrt, hypot, atan2, cosh, sinh, tanh, log2,
// expm1, log1p, and the runtime's frexp and ldexp) bit for bit with system-libm vectors (mkvectors2.c). A NaN
// result must match glibc's in sign and payload too. Written by Claude (Anthropic) for the SWMM6 ports.
import * as fs from "fs";
import * as M from "./glibm2.js";
import * as R from "./glibm2_rt.js";
const buf = fs.readFileSync(process.argv[2]);
const only = process.argv[3] ? process.argv[3].split(",") : null;
const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
const fns: Record<string, (...a: number[]) => number> = { ...(M as any), frexp: R.frexp, ldexp: R.ldexp };
const f = new Float64Array(1), u = new BigUint64Array(f.buffer);
const bits = (x: number) => { f[0] = x; return u[0]; };
const hex = (x: number) => bits(x).toString(16).padStart(16, "0");
let p = 0, total = 0;
while (p < buf.length) {
  let e = p; while (buf[e] !== 0) e++;
  const name = buf.toString("latin1", p, e); p = e + 1;
  const n = dv.getUint32(p, true); p += 4;
  const na = name === "hypot" || name === "atan2" || name === "ldexp" ? 2 : 1;
  const nr = name === "frexp" ? 2 : 1;
  const fn = fns[name];
  if (!fn || (only && !only.includes(name))) { p += n * 8 * (na + nr); continue; }
  let bad = 0, shown = 0, nanSign = 0, nanPay = 0; const t0 = Date.now();
  for (let i = 0; i < n; i++) {
    const a = dv.getFloat64(p, true), b = na > 1 ? dv.getFloat64(p + 8, true) : 0;
    const want = dv.getFloat64(p + 8 * na, true), wantE = nr > 1 ? dv.getFloat64(p + 8 * na + 8, true) : 0;
    p += 8 * (na + nr);
    const got = na === 1 ? fn(a) : fn(a, b);
    const gotE = name === "frexp" ? R.frexpExp(a) : 0;
    if (Number.isNaN(got) && Number.isNaN(want)) {
      if ((bits(got) >> 63n) !== (bits(want) >> 63n)) nanSign++; else if (bits(got) !== bits(want)) nanPay++;
      continue;
    }
    if (bits(got) !== bits(want) || gotE !== wantE) {
      bad++;
      if (shown++ < 4) console.log(`  ${name}(${[a, b].slice(0, na).map((v) => v + " [" + hex(v) + "]").join(", ")}) = ${got} [${hex(got)}], libm ${want} [${hex(want)}]`);
    }
  }
  total += bad + nanSign + nanPay;
  console.log(`${name.padEnd(6)} ${bad} of ${n} differ, ${nanSign} NaN sign, ${nanPay} NaN payload   (${((Date.now() - t0) / n * 1e3).toFixed(3)} us/call)`);
}
console.log(`total differences: ${total}`);
