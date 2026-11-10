// Check2.java - compare set 2 of the Java glibc ports (Glibm2: cbrt, hypot, atan2, cosh, sinh, tanh, log2, expm1,
// log1p, and the runtime's frexp and ldexp) bit for bit with system-libm vectors (mkvectors2.c). A NaN result must
// match glibc's in sign and payload too. Optional second argument: a comma-separated list of functions. With
// "-Xint" on the java command line it checks the interpreter instead of the JIT. Written by Claude (Anthropic)
// for the SWMM6 ports.
package cpp;

import java.io.*;
import java.nio.*;
import java.nio.file.*;

public class Check2 {
  public static void main(String[] a) throws IOException {
    ByteBuffer b = ByteBuffer.wrap(Files.readAllBytes(Paths.get(a[0]))).order(ByteOrder.LITTLE_ENDIAN);
    java.util.Set<String> only = a.length > 1 ? new java.util.HashSet<>(java.util.Arrays.asList(a[1].split(","))) : null;
    long total = 0;
    while (b.hasRemaining()) {
      StringBuilder sb = new StringBuilder();
      byte c;
      while ((c = b.get()) != 0) sb.append((char) c);
      String name = sb.toString();
      int n = b.getInt();
      int na = name.equals("hypot") || name.equals("atan2") || name.equals("ldexp") ? 2 : 1;
      int nr = name.equals("frexp") ? 2 : 1;
      if (only != null && !only.contains(name)) { b.position(b.position() + n * 8 * (na + nr)); continue; }
      long bad = 0, nanSign = 0, nanPay = 0; int shown = 0;
      long t0 = System.nanoTime();
      for (int i = 0; i < n; i++) {
        double x = b.getDouble(), y = na > 1 ? b.getDouble() : 0, want = b.getDouble(), wantE = nr > 1 ? b.getDouble() : 0;
        double got; int gotE = 0;
        switch (name) {
          case "cbrt": got = Glibm2.cbrt(x); break;
          case "hypot": got = Glibm2.hypot(x, y); break;
          case "atan2": got = Glibm2.atan2(x, y); break;
          case "cosh": got = Glibm2.cosh(x); break;
          case "sinh": got = Glibm2.sinh(x); break;
          case "tanh": got = Glibm2.tanh(x); break;
          case "log2": got = Glibm2.log2(x); break;
          case "expm1": got = Glibm2.expm1(x); break;
          case "log1p": got = Glibm2.log1p(x); break;
          case "frexp": got = Glibm2Rt.frexp(x); gotE = Glibm2Rt.frexpExp(x); break;
          default: got = Glibm2Rt.ldexp(x, (int) y);
        }
        long gb = Double.doubleToRawLongBits(got), wb = Double.doubleToRawLongBits(want);
        if (Double.isNaN(got) && Double.isNaN(want)) {
          if ((gb >>> 63) != (wb >>> 63)) nanSign++; else if (gb != wb) nanPay++;
          continue;
        }
        if (gb != wb || gotE != wantE) {
          bad++;
          if (shown++ < 4) System.out.printf("  %s(%s [%016x], %s [%016x]) = %s [%016x], libm %s [%016x]%n", name, x,
              Double.doubleToRawLongBits(x), y, Double.doubleToRawLongBits(y), got, gb, want, wb);
        }
      }
      total += bad + nanSign + nanPay;
      System.out.printf("%-6s %d of %d differ, %d NaN sign, %d NaN payload   (%.3f us/call)%n", name, bad, n, nanSign, nanPay,
          (System.nanoTime() - t0) / 1e3 / n);
    }
    System.out.println("total differences: " + total);
  }
}
