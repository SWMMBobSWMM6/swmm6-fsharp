using System;
using System.Linq;
using System.Reflection;
public static class Driver
{
    public static void Main()
    {
        var t = typeof(openswmm.dt._F);
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.StartsWith("t")).OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            object r;
            try { r = m.Invoke(null, null); } catch (Exception e) { r = "EXC " + (e.InnerException?.GetType().Name ?? e.GetType().Name); }
            string v = r switch
            {
                double d => "d" + BitConverter.DoubleToInt64Bits(d),
                float f => "d" + BitConverter.DoubleToInt64Bits(f),
                bool b => b ? "True" : "False",
                _ => r?.ToString() ?? "None"
            };
            Console.WriteLine(m.Name + "=" + v);
        }
    }
}
