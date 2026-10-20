using System;

// The mod targets .NET Framework 4.7.2, which does not ship this compiler
// attribute. The C# compiler uses it to emit a module initializer in the DLL.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}
