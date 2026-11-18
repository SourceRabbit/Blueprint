using Blueprint.Database;
using Blueprint.Database.Entities;
using Blueprint.Database.Entities.Annotations;
using Blueprint.Fundamentals;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;

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
            using (EntityModelDatabase database = new EntityModelDatabase())
            {
                Assert(typeof(BlueprintEntity).IsClass && typeof(BlueprintEntityDB).BaseType == typeof(BlueprintEntity) && typeof(BlueprintEntityRAM).BaseType == typeof(BlueprintEntity), "DB and RAM must be sibling entity base classes.");
                Assert(typeof(BlueprintEntity).Assembly.GetType("Blueprint.Database.Entities.Annotations.RAMCached") == null, "The cache annotation must be removed from the public assembly.");
                AssertThrows<ArgumentException>(() => database.RegisterEntity(typeof(BlueprintEntity)), "The common base must not be a registrable entity.");
                AssertThrows<ArgumentException>(() => database.RegisterEntity<SealedRecord>(), "Sealed classes cannot be dynamically extended.");
                AssertThrows<ArgumentException>(() => database.RegisterEntity<ConstructorRecord>(), "An accessible parameterless constructor is required.");
                AssertThrows<ArgumentException>(() => database.RegisterEntity<NonVirtualTranslation>(), "Translations must have overridable accessors.");
                AssertThrows<ArgumentException>(() => database.RegisterEntity<HiddenIdentity>(), "Hiding the common ID must be rejected.");
                AssertThrows<ArgumentException>(() => database.RegisterEntity<AbstractMethodRecord>(), "Unimplemented custom methods must be rejected before generation.");
                AssertThrows<ArgumentException>(() => database.RegisterEntity<UnmarkedRecord>(), "Every registered class must declare Entity explicitly.");
                AssertThrows<ArgumentException>(() => database.RegisterEntity<UnselectedRecord>(), "Entity classes must choose DB or RAM storage behavior.");

                database.RegisterEntity<ClassRecord>();
                Assert(database.TableCount == 2, "Related classes must map to two tables without common base tables or discriminators.");
                ClassRecord record = ClassRecord.Create();
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
                Assert(ClassGroup.Create().Records.Count == 0, "Abstract collection navigations must be initialized by Create.");
                string sql = ClassRecord.Where(item => item.Title == "French title" && item.Group!.Name == "Group", 2).Select(item => item.Title).ToQueryString();
                Assert(sql.Contains("ClassRecords") && sql.Contains("ClassGroups") && sql.Contains("TitleFR"), "Class relationships and translated projections must translate to their physical tables.");
                Assert(!sql.Contains("Discriminator"), "Class inheritance must not add EF inheritance discriminators.");
                database.RegisterEntity<ClassGroup>();
                Assert(database.TableCount == 2 && ClassRecord.Create().Code == "initial", "Repeated registration must preserve the generated model and class constructors.");
            }
            Console.WriteLine($"Entity class checks passed: {getAssertionCount()} assertions.");
        }

        /// <summary>Κατασκευάζει και επιθεωρεί το SQL μοντέλο χωρίς πρόσβαση σε πραγματική βάση.</summary>
        private sealed class EntityModelDatabase : BlueprintDatabase
        {
            private int fTableCount;

            /// <summary>Ενεργοποιεί τη δοκιμαστική βάση με προεπιλεγμένο language ID διαφορετικό από το ένα.</summary>
            internal EntityModelDatabase() : base("EntityClassModel", "Server=unused;Database=EntityClassModel", new List<BlueprintLanguage> { new BlueprintLanguage(7, string.Empty), new BlueprintLanguage(2, "FR") })
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
