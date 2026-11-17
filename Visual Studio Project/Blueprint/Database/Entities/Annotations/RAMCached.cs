namespace Blueprint.Database.Entities.Annotations
{
    /// <summary>Loads all entity rows and transitive AutoInclude tables into RAM, synchronizing their changes through SQL Server Change Tracking.</summary>
    [AttributeUsage(AttributeTargets.Interface, Inherited = false)]
    public sealed class RAMCached : Attribute
    {
    }
}
