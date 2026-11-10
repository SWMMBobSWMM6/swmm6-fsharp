// TEST-ONLY driver for flowtest (same output format as csdrv/drv2.cs)
import * as E from './gen/engine.js';
const L = [0, 1, 2, 3, 5, -1, -7, 100, 9223372036854775807n, -9223372036854775808n];
const F: any = (E as any).FlowTest;
const out: string[] = [];
for (let k = 1; k <= 13; k++) for (const x of L) { let s: string; try { s = String(F['T' + k](x)); } catch (e) { s = 'E ' + (e as any).constructor.name; } out.push('T' + k + ' ' + String(x) + ' ' + s); }
(globalThis as any).process.stdout.write(out.join('\n') + '\n');
