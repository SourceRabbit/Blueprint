namespace Blueprint.Database.Entities.Annotations
{
    /// <summary>Marks an interface as a persistent entity and optionally specifies its table.</summary>
    [AttributeUsage(AttributeTargets.Interface, Inherited = false)]
    public sealed class EntityAttribute : Attribute
    {
        /// <summary>Sets the optional entity table name.</summary>
        /// <param name="tableName">The table name, or null to use the default.</param>
        public EntityAttribute(string? tableName = null)
        {
            TableName = tableName;
        }

        /// <summary>The table name, or null to use the interface name.</summary>
        public string? TableName { get; }

        /// <summary>The SQL Server table schema.</summary>
        public string Schema { get; set; } = "dbo";
    }
}
