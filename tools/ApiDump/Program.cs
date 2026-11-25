using System.Reflection;
using System.Runtime.InteropServices;

var pkgs = new[] {
  "classisland.core","classisland.shared","classisland.pluginsdk","avalonia","avalonia.base",
  "avalonia.controls","avalonia.controls.colorpicker","avalonia.controls.datagrid","avalonia.desktop",
  "avalonia.themes.fluent","avalonia.themes.simple","fluentavaloniaui","communitytoolkit.mvvm",
  "microsoft.extensions.hosting","microsoft.extensions.hosting.abstractions",
  "microsoft.extensions.dependencyinjection","microsoft.extensions.dependencyinjection.abstractions",
  "microsoft.extensions.configuration.abstractions","microsoft.extensions.logging.abstractions",
  "microsoft.extensions.options","yamldotnet","avalonia.svg","avalonia.labs.commandmanager","avalonia.win32",
  "avalonia.skia"
};
var nuget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
var dirs = new List<string>();
foreach (var p in pkgs) {
  var dir = Path.Combine(nuget, p);
  if (!Directory.Exists(dir)) continue;
  foreach (var ver in Directory.GetDirectories(dir)) {
    foreach (var sub in new[]{"lib","ref"}) {
      var sp = Path.Combine(ver, sub);
      if (!Directory.Exists(sp)) continue;
      foreach (var tfm in Directory.GetDirectories(sp)) {
        if (Directory.GetFiles(tfm, "*.dll").Length > 0) { dirs.Add(tfm); break; }
      }
    }
  }
}
var rt = RuntimeEnvironment.GetRuntimeDirectory();
var byName = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
foreach (var f in Directory.GetFiles(rt, "*.dll")) byName[Path.GetFileName(f)] = f;
var cand = dirs.SelectMany(d => Directory.GetFiles(d, "*.dll"))
  .GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
  .Select(g => {
      string Score(string p) {
        var m = System.Text.RegularExpressions.Regex.Matches(Path.GetDirectoryName(p)!, "[0-9]+([.][0-9]+){1,3}");
        if (m.Count == 0) return "0.0.0.0";
        return m[m.Count-1].Value;
      }
      return g.OrderByDescending(Score, VersionComparer.Instance).First();
  });
foreach (var f in cand) byName[Path.GetFileName(f)] = f;
var all = byName.Values.ToList();
Console.WriteLine("resolver files: " + all.Count);
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(all));
foreach (var n in new[]{"ClassIsland.Core","ClassIsland.Shared","ClassIsland.PluginSdk","Avalonia.Controls"}) {
  try { mlc.LoadFromAssemblyName(new AssemblyName(n)); } catch (Exception ex) { Console.WriteLine("FAIL " + n + ": " + ex.Message); }
}

string Fmt(Type t) {
  if (t.IsGenericParameter) return t.Name;
  if (t.IsArray) return Fmt(t.GetElementType()!) + "[]";
  if (t.IsByRef) return "ref " + Fmt(t.GetElementType()!);
  var n = t.Name;
  var i = n.IndexOf((char)96);
  if (i > 0) n = n.Substring(0, i);
  if (t.IsGenericType) n += "<" + string.Join(",", t.GetGenericArguments().Select(Fmt)) + ">";
  return n;
}

Type? Find(string full) {
  foreach (var asm in mlc.GetAssemblies()) { var t = asm.GetType(full); if (t != null) return t; }
  return null;
}

void DumpType(Type t) {
  Console.WriteLine("### " + t.FullName + " : " + (t.BaseType is null ? "-" : Fmt(t.BaseType)) + " [" + t.Assembly.GetName().Name + "]");
  Console.WriteLine("  attrs: " + string.Join(", ", t.GetCustomAttributesData().Select(a => a.AttributeType.Name)));
  Type? cur = t;
  int depth = 0;
  while (cur != null && depth < 4) {
    foreach (var m in cur.GetMembers(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) {
      switch (m) {
        case ConstructorInfo ci:
          Console.WriteLine("  ctor(" + string.Join(", ", ci.GetParameters().Select(p=>Fmt(p.ParameterType)+" "+p.Name)) + ")");
          break;
        case MethodInfo mi when !mi.IsSpecialName:
          Console.WriteLine("  " + (mi.IsStatic?"static ":"") + (mi.IsPublic?"public ":mi.IsFamily?"protected ":"nonpublic ") + Fmt(mi.ReturnType) + " " + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(p=>Fmt(p.ParameterType)+" "+p.Name)) + ")");
          break;
        case PropertyInfo pi:
          Console.WriteLine("  prop " + Fmt(pi.PropertyType) + " " + pi.Name + " get=" + (pi.GetMethod is null ? "-" : pi.GetMethod.IsPublic ? "pub" : "prot") + " set=" + (pi.SetMethod is null ? "-" : pi.SetMethod.IsPublic ? "pub" : "prot"));
          break;
        case FieldInfo fi:
          Console.WriteLine("  field " + Fmt(fi.FieldType) + " " + fi.Name);
          break;
      }
    }
    cur = cur.BaseType; depth++;
    if (cur is null) break;
    Console.WriteLine("  -- base: " + cur.FullName);
  }
}

Type[] TypesOf(Assembly asm) {
  try { return asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(x => x != null).ToArray()!; }
}

if (args.Length >= 2 && args[0] == "dm") {
  var pat2 = args[1];
  foreach (var asm in mlc.GetAssemblies())
    foreach (var t in TypesOf(asm))
      if (t.FullName?.Contains(pat2, StringComparison.Ordinal) == true && (t.FullName.StartsWith("ClassIsland") || t.FullName.StartsWith("Avalonia")))
        DumpType(t);
} else if (args.Length >= 2 && args[0] == "dump") {
  var t = Find(args[1]);
  if (t is null) Console.WriteLine("NOT FOUND " + args[1]); else DumpType(t);
} else if (args.Length >= 2 && args[0] == "types") {
  var pat = args[1];
  foreach (var asm in mlc.GetAssemblies())
    foreach (var t in TypesOf(asm))
      if (t.FullName != null && t.FullName.Contains(pat, StringComparison.OrdinalIgnoreCase))
        Console.WriteLine(t.FullName + "  [" + asm.GetName().Name + "]  base=" + (t.BaseType?.Name ?? "-"));
} else if (args.Length >= 2 && args[0] == "statics") {
  var pat = args[1];
  foreach (var asm in mlc.GetAssemblies())
    foreach (var t in TypesOf(asm)) {
      if (t.FullName?.Contains(pat, StringComparison.OrdinalIgnoreCase) != true) continue;
      foreach (var mi in t.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly)) {
        try {
          if (!mi.Name.Contains("Add", StringComparison.OrdinalIgnoreCase)) continue;
          Console.WriteLine(t.FullName + " :: " + Fmt(mi.ReturnType) + " " + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(p=>Fmt(p.ParameterType))) + ")");
        } catch (Exception) { }
      }
    }
} else if (args.Length >= 2 && args[0] == "reg") {
  foreach (var asm in mlc.GetAssemblies())
    foreach (var t in TypesOf(asm))
      foreach (var mi in t.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly)) {
        try {
          foreach (var p in mi.GetParameters())
            if ((p.ParameterType.Namespace ?? "").Contains("ClassIsland") && p.ParameterType.Name.Contains("Registry"))
              Console.WriteLine(t.FullName + " :: " + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(x=>Fmt(x.ParameterType))) + ")");
        } catch (Exception) { }
      }
} else {
  foreach (var name in new[]{
    "ClassIsland.Core.Abstractions.Automation.ActionBase",
    "ClassIsland.Core.Abstractions.PluginBase",
    "ClassIsland.Core.Attributes.PluginEntrance",
  }) { var t = Find(name); if (t is null) Console.WriteLine("NOT FOUND " + name); else DumpType(t); }
}