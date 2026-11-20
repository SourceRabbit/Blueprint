// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database;
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Fundamentals;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Text.Json;

namespace Blueprint.Tests.Entities
{
    /// <summary>Ελέγχει κληρονομικότητα, constructors, μεταφράσεις και ανεξάρτητα SQL mappings μέσω των δημόσιων APIs.</summary>
    internal sealed class EntityClassTest : BlueprintTest
    {
        /// <summary>Κατασκευάζει μοντέλα χωρίς σύνδεση SQL και επαληθεύει έγκυρες και μη υποστηριζόμενες κλάσεις.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος ή κατασκευή μοντέλου.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            ClassRecord record;
            ClassGroup group;
            List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(7, string.Empty), new BlueprintLanguage(2, "FR") };
            using (EntityModelDatabase database = new EntityModelDatabase(languages))
            {
                Assert(typeof(BlueprintEntity).IsClass && typeof(BlueprintEntityDB).BaseType == typeof(BlueprintEntity) && typeof(BlueprintEntityRAM).BaseType == typeof(BlueprintEntity), "DB and RAM must be sibling entity base classes.");
                Assert(typeof(BlueprintEntity).Assembly.GetType("Blueprint.Entities.Annotations.RAMCached") == null, "The cache annotation must be removed from the public assembly.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(BlueprintEntity)), "The common base must not be a registrable entity.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(SealedRecord)), "Sealed classes cannot be dynamically extended.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(ConstructorRecord)), "An accessible parameterless constructor is required.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(NonVirtualTranslation)), "Translations must have overridable accessors.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(HiddenIdentity)), "Hiding the common ID must be rejected.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(AbstractMethodRecord)), "Unimplemented custom methods must be rejected before generation.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(UnmarkedRecord)), "Every registered class must declare Entity explicitly.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(UnselectedRecord)), "Entity classes must choose DB or RAM storage behavior.");

                database.RegisterEntities(typeof(ClassRecord));
                Assert(database.TableCount == 2, "Related classes must map to two tables without common base tables or discriminators.");
                record = ClassRecord.Create();
                Assert(record is BlueprintEntityRAM && record.GetType().BaseType == typeof(ClassRecord), "Generated entities must derive from the user's class.");
                Assert(record.ID == 0 && record.CurrentLanguage == 7, "Creation must preserve the inherited identity and configured default language.");
                Assert(record.Code == "initial" && record.Title == "Constructor title", "Class initializers and constructors must run, including virtual translation assignments.");
                record.Rename("renamed");
                Assert(record.Display == "renamed: Constructor title", "Custom methods and computed properties must retain their implementation.");
                record.CurrentLanguage = 2;
                Assert(record.Title == "Constructor title", "Inherited fallback annotations must apply to generated translation accessors.");
                record.Title = "French title";
                record.CurrentLanguage = 7;
                Assert(record.Title == "Constructor title", "Translations must use independent storage.");
                record.CurrentLanguage = 2;
                Assert(record.Title == "French title", "Switching languages must recover the selected translation.");
                group = ClassGroup.Create();
                Assert(group.Records.Count == 0, "Abstract collection navigations must be initialized by Create.");
                string sql = ClassRecord.Where(item => item.Title == "French title" && item.Group!.Name == "Group", 2).Select(item => item.Title).ToQueryString();
                Assert(sql.Contains("ClassRecords") && sql.Contains("ClassGroups") && sql.Contains("TitleFR"), "Class relationships and translated projections must translate to their physical tables.");
                Assert(!sql.Contains("Discriminator"), "Class inheritance must not add EF inheritance discriminators.");
                database.RegisterEntities(typeof(ClassGroup));
                Assert(database.TableCount == 2 && ClassRecord.Create().Code == "initial", "Repeated registration must preserve the generated model and class constructors.");
                CheckFromJson();
            }
            CheckJson(record, group);
            CheckBatchRegistration(languages);
            CheckDescriptionFallbackAfterFromJson();
            Console.WriteLine($"Entity class checks passed: {getAssertionCount()} assertions.");
        }

        /// <summary>Verifies that a batch synchronizes once and a failed batch cannot leak contracts into a later registration.</summary>
        /// <param name="languages">The languages for the isolated model.</param>
        private void CheckBatchRegistration(List<BlueprintLanguage> languages)
        {
            using (EntityModelDatabase database = new EntityModelDatabase(languages))
            {
                int initialCount = database.SynchronizationCount;
                database.RegisterEntities(Array.Empty<Type>());
                AssertThrows<ArgumentNullException>(() => database.RegisterEntities((IEnumerable<Type>)null!), "A null collection must be rejected.");
                AssertThrows<ArgumentNullException>(() => database.RegisterEntities(typeof(PlainRecord), null!), "A null item must reject the batch.");
                AssertThrows<ArgumentException>(() => database.RegisterEntities(typeof(PlainRecord), typeof(SealedRecord)), "An invalid item must reject the batch.");
                Assert(database.SynchronizationCount == initialCount, "Empty and invalid batches must not reach the provider.");

                List<Type> contracts = new List<Type> { typeof(ClassRecord), typeof(DescriptionFallbackProduct), typeof(ClassRecord) };
                database.RegisterEntities(contracts);
                Assert(database.SynchronizationCount == initialCount + 1 && database.TableCount == 3, "One call must synchronize distinct roots and their related entities exactly once.");
                AssertThrows<InvalidOperationException>(() => PlainRecord.Create(), "Invalid batches must not register their valid items.");

                using (BlueprintDatabaseContext context = database.GetContext())
                {
                    AssertThrows<InvalidOperationException>(() => database.RegisterEntities(typeof(ClassRecord), typeof(PlainRecord), typeof(ClassGroup)), "A busy context must reject the whole batch.");
                }
                database.RegisterEntities(typeof(ClassRecord));
                Assert(database.SynchronizationCount == initialCount + 3 && ClassRecord.Create().Code == "initial", "Re-registering existing roots must synchronize once and preserve the active model.");
                database.RegisterEntities(typeof(ClassGroup));
                AssertThrows<InvalidOperationException>(() => PlainRecord.Create(), "The next synchronization must exclude new contracts from a failed batch.");
                database.RegisterEntities(typeof(PlainRecord), typeof(ClassGroup));
                Assert(database.TableCount == 4 && PlainRecord.Create().ID == 0, "Retrying the batch must register its new contract.");
            }
        }

        /// <summary>Checks scalar and multilingual JSON after the database has been disposed.</summary>
        /// <param name="record">A record with a French translation and a default title.</param>
        /// <param name="group">A group with no multilingual properties.</param>
        private void CheckJson(ClassRecord record, ClassGroup group)
        {
            record.ID = 42;
            JsonElement json = JsonSerializer.Deserialize<JsonElement>(record.ToJson());
            string[] names = json.EnumerateObject().Select(property => property.Name).ToArray();
            Assert(names.First() == "ID" && names.ToHashSet().SetEquals(new string[] { "ID", "Code", "GroupID", "Version", "Translations" }), "The root must contain ID first, shared scalars and Translations.");
            Dictionary<long, JsonElement> translations = json.GetProperty("Translations").EnumerateArray().ToDictionary(entry => entry.GetProperty("LanguageID").GetInt64());
            Assert(translations.Count == 2 && translations[7].GetProperty("Title").GetString() == "Constructor title" && translations[2].GetProperty("Title").GetString() == "French title", "Every configured language must have its own stored values.");
            Assert(translations.Values.All(entry => entry.GetProperty("Description").ValueKind == JsonValueKind.Null), "Each language entry must also include unset multilingual properties.");
            Assert(json.GetProperty("ID").GetInt64() == 42 && record.CurrentLanguage == 2, "Serializing must preserve the identity and selected language.");
            Assert(json.GetProperty("GroupID").ValueKind == JsonValueKind.Null, "A null foreign key must still appear in JSON.");

            group.ID = 7;
            group.Name = "Group";
            record.GroupID = group.ID;
            string unloadedJson = record.ToJson();
            record.Group = group;
            group.Records.Add(record);
            Assert(record.ToJson() == unloadedJson && JsonSerializer.Deserialize<JsonElement>(unloadedJson).GetProperty("GroupID").GetInt64() == 7, "Loading a reference must preserve the foreign key and leave JSON unchanged.");
            Assert(group.ToJson() == "{\"ID\":7,\"Name\":\"Group\"}", "An entity without multilingual fields must omit CurrentLanguage and loaded collections.");

            record.Title = null;
            string withMissingTitle = record.ToJson();
            json = JsonSerializer.Deserialize<JsonElement>(withMissingTitle);
            JsonElement french = json.GetProperty("Translations").EnumerateArray().Single(entry => entry.GetProperty("LanguageID").GetInt64() == 2);
            Assert(record.Title == "Constructor title" && french.GetProperty("Title").ValueKind == JsonValueKind.Null, "The fallback display value must not replace a missing stored translation.");
            record.CurrentLanguage = 7;
            Assert(record.ToJson() == withMissingTitle, "Changing CurrentLanguage must leave the serialized document identical.");
        }

        /// <summary>Checks JSON creation for registered DB and RAM entities without a SQL connection.</summary>
        private void CheckFromJson()
        {
            ClassRecord record = ClassRecord.FromJson("""
                {"GroupID":7,"ID":42,"Version":"AQI=","Translations":[
                    {"Title":"French title","Description":null,"LanguageID":2},
                    {"LanguageID":7,"Description":"Default description","Title":"Default title"}]}
                """);
            Assert(record.CurrentLanguage == 7 && record.Title == "Default title" && record.Description == "Default description", "Import must return the entity in the configured default language.");
            Assert(record.ID == 42 && record.GroupID == 7 && record.Group == null, "JSON must preserve scalar keys without fetching related rows.");
            Assert(record.Version.SequenceEqual(new byte[] { 1, 2 }), "Binary scalar properties must accept the base64 representation emitted by ToJson.");
            record.CurrentLanguage = 2;
            Assert(record.Title == "French title" && record.Description == null, "LanguageID must select the storage for every translated property regardless of JSON property order.");
            ClassRecord copy = ClassRecord.FromJson(record.ToJson());
            Assert(copy.CurrentLanguage == 7 && copy.ToJson() == record.ToJson(), "A round trip must preserve every translation and restore the default language.");

            ClassRecord withEmptyTitle = ClassRecord.FromJson("""{"Translations":[{"LanguageID":2,"Title":""}]}""");
            withEmptyTitle.CurrentLanguage = 2;
            JsonElement entries = JsonSerializer.Deserialize<JsonElement>(withEmptyTitle.ToJson()).GetProperty("Translations");
            JsonElement french = entries.EnumerateArray().Single(entry => entry.GetProperty("LanguageID").GetInt64() == 2);
            Assert(withEmptyTitle.Title == "Constructor title" && french.GetProperty("Title").GetString() == string.Empty, "Empty translations must survive fallback and omitted languages must keep their initialized values.");

            ClassRecord partial = ClassRecord.FromJson("""{"GroupID":null}""");
            Assert(partial.ID == 0 && partial.CurrentLanguage == 7 && partial.Code == "initial" && partial.GroupID == null, "Omitted fields must keep their creation defaults.");
            ClassRecord cleared = ClassRecord.FromJson("""{"Translations":[{"LanguageID":7,"Title":null}]}""");
            JsonElement emptyEntries = JsonSerializer.Deserialize<JsonElement>(cleared.ToJson()).GetProperty("Translations");
            Assert(cleared.Title == null && emptyEntries.GetArrayLength() == 2 &&
                emptyEntries.EnumerateArray().All(entry => entry.GetProperty("Title").ValueKind == JsonValueKind.Null && entry.GetProperty("Description").ValueKind == JsonValueKind.Null),
                "Explicit null must clear a constructor value and languages with no stored values must still be exported.");
            ClassGroup group = ClassGroup.FromJson("""{"Name":"Group"}""");
            Assert(group.Name == "Group" && group.Records.Count == 0, "A non-multilingual entity must keep its initialized collections.");

            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"CurrentLanguage":2}"""), "CurrentLanguage is runtime state and cannot be imported.");
            AssertThrows<JsonException>(() => ClassGroup.FromJson("""{"CurrentLanguage":2}"""), "Ordinary entities must reject CurrentLanguage.");
            AssertThrows<JsonException>(() => ClassGroup.FromJson("""{"Translations":[]}"""), "Ordinary entities must reject a translation array.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Title":"A"}"""), "Multilingual properties must be under Translations.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Translations":{}}"""), "Translations must be an array.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Translations":[{"Title":"A"}]}"""), "Each entry must have a LanguageID.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Translations":[{"LanguageID":999}]}"""), "Only configured languages are valid.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Translations":[{"LanguageID":2},{"LanguageID":2}]}"""), "Duplicate language entries are ambiguous.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Translations":[{"LanguageID":2,"Title":"A","Title":"B"}]}"""), "Duplicate translation properties are ambiguous.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Translations":[{"LanguageID":2,"Code":"shared"}]}"""), "Only multilingual fields belong in a translation entry.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Translations":[{"LanguageID":2,"Title":42}]}"""), "Translations must contain strings or null.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Group":{}}"""), "Nested entity input must be rejected.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"Display":"computed"}"""), "Unmapped properties must be rejected.");
            AssertThrows<JsonException>(() => ClassRecord.FromJson("""{"ID":"invalid"}"""), "Scalar values must match the declared property type.");
        }

        /// <summary>Checks that reading the English fallback preserves both null and empty Greek descriptions in JSON.</summary>
        private void CheckDescriptionFallbackAfterFromJson()
        {
            List<BlueprintLanguage> languages = new List<BlueprintLanguage> { new BlueprintLanguage(1, string.Empty), new BlueprintLanguage(2, "GR") };
            using (EntityModelDatabase database = new EntityModelDatabase(languages))
            {
                database.RegisterEntities(typeof(DescriptionFallbackProduct));
                foreach (string greekDescriptionJson in new string[] { "null", "\"\"" })
                {
                    DescriptionFallbackProduct product = DescriptionFallbackProduct.FromJson($$"""
                        {"Translations":[
                            {"LanguageID":1,"Description":"Up to 20 hours of battery life."},
                            {"LanguageID":2,"Description":{{greekDescriptionJson}}}]}
                        """);

                    product.CurrentLanguage = 2;
                    Assert(product.Description == "Up to 20 hours of battery life.", $"A Greek description of {greekDescriptionJson} must read as the English fallback after FromJson.");

                    JsonElement translations = JsonElement.Parse(product.ToJson()).GetProperty("Translations");
                    JsonElement greek = translations.EnumerateArray().Single(entry => entry.GetProperty("LanguageID").GetInt64() == 2);
                    Assert(greek.GetProperty("Description").GetRawText() == greekDescriptionJson, $"ToJson must preserve the Greek description as {greekDescriptionJson} after reading the fallback.");
                }
            }
        }

        /// <summary>Κατασκευάζει και επιθεωρεί το SQL μοντέλο χωρίς πρόσβαση σε πραγματική βάση.</summary>
        private sealed class EntityModelDatabase : BlueprintDatabase
        {
            private int fTableCount;

            /// <summary>Initializes the test model with the languages used by the calling test.</summary>
            /// <param name="languages">The configured languages, including the default with an empty prefix.</param>
            internal EntityModelDatabase(List<BlueprintLanguage> languages) : base("EntityClassModel", "Server=unused;Database=EntityClassModel", languages)
            {
                try
                {
                    SynchronizeEntities();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            /// <summary>Απορρίπτει SQL εκτέλεση στον provider ελέγχου μοντέλων.</summary>
            /// <param name="commandText">Η εντολή SQL.</param>
            /// <param name="parameters">Οι παράμετροι SQL.</param>
            /// <param name="commandTimeoutSeconds">Το προαιρετικό χρονικό όριο.</param>
            /// <returns>Δεν επιστρέφει τιμή.</returns>
            /// <exception cref="NotSupportedException">Δεν υποστηρίζεται εκτέλεση SQL.</exception>
            public override int ExecuteNonQuery(string commandText, IReadOnlyList<DbParameter>? parameters = null, int? commandTimeoutSeconds = null)
            {
                throw new NotSupportedException();
            }

            /// <summary>Επαληθεύει ότι κάθε generated entity αποτελεί ανεξάρτητο SQL mapping.</summary>
            /// <exception cref="InvalidOperationException">Εμφανίζεται ανεπιθύμητη EF κληρονομικότητα.</exception>
            protected override void SynchronizeEntities()
            {
                SynchronizationCount++;
                InitializeContext(new DbContextOptionsBuilder().UseSqlServer(ConnectionString), context =>
                {
                    fTableCount = context.Model.GetEntityTypes().Count();
                    foreach (Microsoft.EntityFrameworkCore.Metadata.IEntityType entity in context.Model.GetEntityTypes())
                    {
                        if (entity.BaseType != null || entity.FindProperty("Discriminator") != null || entity.FindProperty(nameof(BlueprintEntity.CurrentLanguage)) != null)
                        {
                            throw new InvalidOperationException("Common entity inheritance must not change the SQL schema.");
                        }
                    }
                });
            }

            /// <summary>Counts provider synchronization attempts, including failed attempts.</summary>
            internal int SynchronizationCount { get; private set; }

            /// <summary>Ο αριθμός των entity mappings του τελευταίου μοντέλου.</summary>
            internal int TableCount
            {
                get
                {
                    return fTableCount;
                }
            }
        }
    }

    /// <summary>Models a product whose missing description falls back to the default language.</summary>
    [Entity]
    public abstract class DescriptionFallbackProduct : BlueprintEntityDB
    {
        [Multilingual, FallbackToDefaultLanguage]
        public abstract string? Description { get; set; }
    }

    /// <summary>Κοινή μη καταχωρισμένη βάση που μεταβιβάζει RAM caching και translated properties.</summary>
    public abstract class ClassRecordBase : BlueprintEntityRAM
    {
        [Multilingual, FallbackToDefaultLanguage, MaxLength(200)]
        public virtual string? Title { get; set; }
    }

    /// <summary>Συνδυάζει υλοποιημένα properties και constructor με inherited μεταφράσεις και σχέση.</summary>
    [Entity("ClassRecords")]
    [EntityIndex("UX_ClassRecords_Code", nameof(Code), IsUnique = true)]
    public class ClassRecord : ClassRecordBase
    {
        /// <summary>Ορίζει αρχική μετάφραση χρησιμοποιώντας το επιλεγμένο language του generated instance.</summary>
        public ClassRecord()
        {
            Title = "Constructor title";
        }

        /// <summary>Αλλάζει τον κωδικό μέσω custom συμπεριφοράς της entity class.</summary>
        /// <param name="code">Ο νέος κωδικός.</param>
        public void Rename(string code)
        {
            Code = code;
        }

        [MaxLength(40)]
        public string Code { get; set; } = "initial";

        [Multilingual]
        public virtual string? Description { get; set; }

        public long? GroupID { get; set; }

        [ForeignKey(nameof(GroupID)), AutoInclude]
        public ClassGroup? Group { get; set; }

        [Timestamp]
        public byte[] Version { get; set; } = Array.Empty<byte>();

        [NotMapped]
        public string Display
        {
            get
            {
                return Code + ": " + Title;
            }
        }
    }

    /// <summary>Ορίζει abstract DB entity με inverse collection προς concrete RAM entity.</summary>
    [Entity("ClassGroups")]
    public abstract class ClassGroup : BlueprintEntityDB
    {
        public abstract string Name { get; set; }

        [InverseProperty(nameof(ClassRecord.Group)), AutoInclude]
        public abstract ICollection<ClassRecord> Records { get; set; }
    }

    /// <summary>Ελέγχει την απόρριψη sealed entity classes.</summary>
    [Entity]
    public sealed class SealedRecord : BlueprintEntityDB
    {
    }

    /// <summary>Ελέγχει την απαίτηση προσβάσιμου parameterless constructor.</summary>
    [Entity]
    public class ConstructorRecord : BlueprintEntityDB
    {
        /// <summary>Δηλώνει constructor που δεν μπορεί να χρησιμοποιηθεί από τη generated subclass.</summary>
        private ConstructorRecord()
        {
        }
    }

    /// <summary>Ελέγχει την απόρριψη translated property που δεν μπορεί να γίνει override.</summary>
    [Entity]
    public class NonVirtualTranslation : BlueprintEntityDB
    {
        [Multilingual]
        public string? Title { get; set; }
    }

    /// <summary>Ελέγχει ότι δεν δημιουργείται δεύτερη ασύμβατη ταυτότητα σε μία entity class.</summary>
    [Entity]
    public class HiddenIdentity : BlueprintEntityDB
    {
        public new long ID { get; set; }
    }

    /// <summary>Ελέγχει την απόρριψη abstract συμπεριφοράς που δεν υλοποιείται αυτόματα.</summary>
    [Entity]
    public abstract class AbstractMethodRecord : BlueprintEntityDB
    {
        /// <summary>Δηλώνει custom συμπεριφορά χωρίς υλοποίηση.</summary>
        public abstract void Execute();
    }

    /// <summary>Ελέγχει ότι η σήμανση Entity δεν κληρονομείται σιωπηρά.</summary>
    public class UnmarkedRecord : ClassRecord
    {
    }

    /// <summary>Ελέγχει ότι η κοινή βάση δεν υποκαθιστά την επιλογή DB ή RAM.</summary>
    [Entity]
    public class UnselectedRecord : BlueprintEntity
    {
    }
}
