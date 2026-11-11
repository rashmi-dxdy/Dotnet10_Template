using System.Reflection;

namespace Catalog.Domain;

/// <summary>
/// Marker for the Catalog domain assembly. Domain model types belong in this project.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the assembly that contains this marker type.</summary>
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
