using System;
using System.Reflection;
using System.Linq;
var asm = Assembly.LoadFrom("/home/runner/.nuget/packages/microsoft.entityframeworkcore/10.0.3/lib/net10.0/Microsoft.EntityFrameworkCore.dll");
var types = asm.GetTypes().Where(t => t.Name.Contains("ConnectionInterceptor") || t.Name.Contains("DbConnection")).Take(10);
foreach (var t in types) Console.WriteLine($"{t.Namespace}.{t.Name}");
