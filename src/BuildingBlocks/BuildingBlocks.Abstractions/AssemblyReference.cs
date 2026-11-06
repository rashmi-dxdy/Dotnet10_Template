using System.Reflection;

namespace BuildingBlocks.Abstractions;

/// <summary>
/// Marker for the shared technical abstractions assembly. Add technology-agnostic interfaces only.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the assembly that contains this marker type.</summary>
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
