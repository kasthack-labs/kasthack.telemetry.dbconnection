#if NETSTANDARD2_0
// Polyfill for nullable reference type attributes not available in netstandard2.0
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue)]
    internal sealed class AllowNullAttribute : Attribute { }
}
#endif
