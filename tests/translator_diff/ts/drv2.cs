using System;
public static class Drv2 { public static void Main() {
  var L = new long[] { 0, 1, 2, 3, 5, -1, -7, 100, 9223372036854775807L, -9223372036854775808L };
  var o = new System.Text.StringBuilder();
  var ms = new Func<long, long>[] { openswmm.FlowTest.T1, openswmm.FlowTest.T2, openswmm.FlowTest.T3, openswmm.FlowTest.T4, openswmm.FlowTest.T5, openswmm.FlowTest.T6, openswmm.FlowTest.T7, openswmm.FlowTest.T8, openswmm.FlowTest.T9, openswmm.FlowTest.T10, openswmm.FlowTest.T11, openswmm.FlowTest.T12, openswmm.FlowTest.T13 };
  for (int k = 0; k < ms.Length; k++) foreach (var x in L) { string s; try { s = ms[k](x).ToString(); } catch (Exception e) { s = "E " + e.GetType().Name; } o.Append("T" + (k + 1) + " " + x + " " + s + "\n"); }
  Console.Write(o.ToString()); } }
