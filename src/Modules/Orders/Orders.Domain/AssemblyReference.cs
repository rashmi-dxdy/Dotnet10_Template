using System.Reflection;

namespace Orders.Domain;

/// <summary>
/// Marker for the Orders domain assembly. Domain model types belong in this project.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the assembly that contains this marker type.</summary>
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
