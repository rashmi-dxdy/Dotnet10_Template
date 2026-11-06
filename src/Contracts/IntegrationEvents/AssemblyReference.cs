using System.Reflection;

namespace IntegrationEvents;

/// <summary>
/// Marker for the integration event contracts assembly. Add cross-service message contracts only.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the assembly that contains this marker type.</summary>
    public static Assembly Assembly => typeof(AssemblyReference).Assembly;
}
