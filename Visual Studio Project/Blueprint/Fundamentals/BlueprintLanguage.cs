namespace Blueprint.Fundamentals
{
    /// <summary>Describes an application language with a stable identifier and a suffix for multilingual columns.</summary>
    public sealed class BlueprintLanguage
    {
        private readonly long fID;
        private readonly string fPrefix;

        /// <summary>Creates a language with the specified identifier and multilingual column suffix.</summary>
        /// <param name="id">The stable, unique language identifier.</param>
        /// <param name="prefix">The multilingual column suffix, or an empty string for the default language.</param>
        public BlueprintLanguage(long id, string prefix)
        {
            fID = id;
            fPrefix = prefix;
        }

        /// <summary>The stable, unique language identifier defined by the application.</summary>
        public long ID
        {
            get
            {
                return fID;
            }
        }

        /// <summary>The multilingual column suffix, such as GR, EN, or GR_CY. Uppercased in column names and empty for the default language.</summary>
        public string Prefix
        {
            get
            {
                return fPrefix;
            }
        }
    }
}
