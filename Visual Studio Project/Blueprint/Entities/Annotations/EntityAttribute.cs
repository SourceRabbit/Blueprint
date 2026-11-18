namespace Blueprint.Entities.Annotations
{
    /// <summary>Marks a class as a persistent entity and optionally specifies its table.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EntityAttribute : Attribute
    {
        /// <summary>Sets the optional entity table name.</summary>
        /// <param name="tableName">The table name, or null to use the default.</param>
        public EntityAttribute(string? tableName = null)
        {
            TableName = tableName;
        }

        /// <summary>The table name, or null to use the class name.</summary>
        public string? TableName { get; }

        /// <summary>The SQL Server table schema.</summary>
        public string Schema { get; set; } = "dbo";
    }
}
