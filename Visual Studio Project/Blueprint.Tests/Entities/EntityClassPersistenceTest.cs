// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

using Blueprint.Database;
using Blueprint.Entities;
using Blueprint.Entities.Annotations;
using Blueprint.Database.Sql;
using Blueprint.Database.TransactionFactory;
using Blueprint.Fundamentals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Blueprint.Tests.Entities
{
    /// <summary>Ελέγχει SQL persistence, transactions και RAM synchronization για entity classes σε απομονωμένη βάση.</summary>
    internal sealed class EntityClassPersistenceTest : BlueprintTest
    {
        /// <summary>Εκτελεί πραγματικές εγγραφές και εξωτερικές αλλαγές και αφαιρεί την προσωρινή βάση.</summary>
        /// <exception cref="Exception">Αποτυγχάνει έλεγχος ή λειτουργία SQL.</exception>
        public override void RunTest()
        {
            ResetAssertions();
            string databaseName = "BlueprintEntityClasses_" + Guid.NewGuid().ToString("N");
            SqlConnectionStringBuilder settings = new SqlConnectionStringBuilder(fDatabaseConnectionString) { InitialCatalog = "master", Pooling = false, ConnectTimeout = 5 };
            using (SqlConnection connection = new SqlConnection(settings.ConnectionString))
            {
                connection.Open();
                try
                {
                    using (BlueprintDatabase database = new SqlDatabase(databaseName, settings.ConnectionString, new List<BlueprintLanguage> { new BlueprintLanguage(7, string.Empty), new BlueprintLanguage(2, "FR") }))
                    {
                        database.RAMCacheRefreshInterval = TimeSpan.FromHours(1);
                        database.RegisterEntities(typeof(ClassRecord), typeof(PlainRecord));
                        ClassGroup group = ClassGroup.Create();
                        group.Name = "Group";
                        ClassRecord record = ClassRecord.Create();
                        record.Group = group;
                        record.CurrentLanguage = 2;
                        record.Title = "French";
                        DatabaseTransaction insert = database.TransactionFactory.CreateNewTransaction();
                        insert.AddInsert(group);
                        insert.AddInsert(record);
                        Assert(database.TransactionFactory.ExecuteTransactionAsync(insert).GetAwaiter().GetResult() == 2, "A mixed DB/RAM transaction must insert both classes.");
                        Assert(record.ID > 0 && record.GroupID == group.ID && record.Version.Length == 8, "Generated IDs, related foreign keys and rowversions must be populated.");
                        ClassRecord stored = ClassRecord.SingleOrDefault(item => item.ID == record.ID, 2)!;
                        Assert(stored.Title == "French" && stored.Group!.Name == "Group", "RAM materialization must preserve class translations and included relationships.");
                        Assert(ClassRecord.SingleOrDefault(item => item.ID == record.ID)!.Title == "Constructor title", "Saving another language must preserve constructor-initialized default content.");
                        Assert(ClassRecord.Where(item => item.ID == record.ID, 2).Select(item => item.Title).ToArray().Single() == "French", "Cached projections must read inherited translation storage.");
                        ClassRecord stale = ClassRecord.SingleOrDefault(item => item.ID == record.ID)!;
                        stored.Rename("updated");
                        Assert(ClassRecord.Update(stored) == 1, "Ordinary implemented properties must persist through the existing Entity API.");
                        stale.Rename("stale");
                        AssertThrows<DbUpdateConcurrencyException>(() => ClassRecord.Update(stale), "Rowversions must reject stale class instances.");

                        DatabaseTransaction rollback = database.TransactionFactory.CreateNewTransaction(pending =>
                        {
                            ClassRecord changed = ClassRecord.SingleOrDefault(item => item.ID == record.ID)!;
                            changed.Rename("rolled back");
                            pending.AddUpdate(changed);
                            pending.AddValidation(() =>
                            {
                                Assert(ClassRecord.SingleOrDefault(item => item.ID == record.ID)!.Code == "rolled back", "Transaction reads must see uncommitted SQL changes for RAM entities.");
                                throw new InvalidOperationException("Expected rollback.");
                            });
                        });
                        AssertThrows<InvalidOperationException>(() => database.TransactionFactory.ExecuteTransactionAsync(rollback).GetAwaiter().GetResult(), "Validation failure must roll back the class update.");
                        Assert(ClassRecord.SingleOrDefault(item => item.ID == record.ID)!.Code == "updated", "Rolled-back values must not reach RAM.");

                        PlainRecord plain = PlainRecord.Create();
                        plain.Title = "Before";
                        PlainRecord.Insert(plain);
                        Assert(plain.InsertedUtc > DateTime.UtcNow.AddMinutes(-1) && plain.InsertedUtc < DateTime.UtcNow.AddMinutes(1), "An insert must return the UTC timestamp generated by the SQL default.");
                        Assert(ClassRecord.SingleOrDefault(item => item.ID == record.ID, 2)!.Title == "French", "Synchronize cache invalidation from the preceding SQL write before testing external changes.");
                        using (SqlCommand command = connection.CreateCommand())
                        {
                            command.CommandText = $"UPDATE [{databaseName}].dbo.ClassRecords SET Title = N'External default', TitleFR = N'External French' WHERE ID = @id; UPDATE [{databaseName}].dbo.PlainRecords SET Title = N'External SQL' WHERE ID = @plainID; UPDATE [{databaseName}].dbo.ClassGroups SET Name = N'External group' WHERE ID = @groupID;";
                            command.Parameters.AddWithValue("@id", record.ID);
                            command.Parameters.AddWithValue("@plainID", plain.ID);
                            command.Parameters.AddWithValue("@groupID", group.ID);
                            command.ExecuteNonQuery();
                        }
                        Assert(PlainRecord.SingleOrDefault(item => item.ID == plain.ID)!.Title == "External SQL", "DB entities outside AutoInclude cache dependencies must read SQL directly.");
                        Assert(ClassRecord.SingleOrDefault(item => item.ID == record.ID, 2)!.Title == "French", "RAM entities must use the synchronized snapshot until refresh.");
                        ClassGroup mixedGroup = ClassGroup.Where(item => item.ID == group.ID, 2).Single();
                        Assert(mixedGroup.Name == "External group" && mixedGroup.Records.Single().Title == "French", "DB parents must load RAM collections from the cached snapshot.");
                        Assert(ReferenceEquals(mixedGroup, mixedGroup.Records.Single().Group), "Mixed-source cycles must reuse the parent instance.");
                        Assert(ClassRecord.SingleOrDefault(item => item.ID == record.ID)!.Group!.Name == "External group", "RAM parents must load DB references directly from SQL.");
                        database.RAMCacheRefreshInterval = TimeSpan.FromMilliseconds(50);
                        Assert(SpinWait.SpinUntil(() => ClassRecord.SingleOrDefault(item => item.ID == record.ID, 2)!.Title == "External French", TimeSpan.FromSeconds(10)), "Change Tracking must synchronize external updates for inherited RAM entities.");
                        Assert(stored.Title == "French", "Refreshes must preserve previously returned independent objects.");
                        database.RegisterEntities(typeof(ClassRecord));
                        Assert(ClassRecord.SingleOrDefault(item => item.ID == record.ID)!.Title == "External default", "Schema resynchronization must retain stored data and translations.");
                        Assert(ClassRecord.Delete(item => item.ID == record.ID) == 1 && !ClassRecord.Any(), "Predicate deletes must persist in SQL and invalidate RAM.");
                        using (SqlCommand command = connection.CreateCommand())
                        {
                            command.CommandText = $"SELECT COUNT(*) FROM [{databaseName}].sys.tables;";
                            Assert(Convert.ToInt32(command.ExecuteScalar()) == 3, "The shared base classes must not create additional SQL tables.");
                        }
                    }
                }
                finally
                {
                    using (SqlCommand command = connection.CreateCommand())
                    {
                        command.CommandText = $"IF DB_ID(@name) IS NOT NULL DROP DATABASE [{databaseName}];";
                        command.Parameters.AddWithValue("@name", databaseName);
                        command.ExecuteNonQuery();
                    }
                }
            }
            Console.WriteLine($"Entity class persistence checks passed: {getAssertionCount()} assertions.");
        }
    }

    /// <summary>Ορίζει concrete SQL entity χωρίς εξάρτηση από RAM-cached σχέσεις.</summary>
    [Entity("PlainRecords")]
    public class PlainRecord : BlueprintEntityDB
    {
        [DefaultValueSql("SYSUTCDATETIME()")]
        public DateTime InsertedUtc { get; set; }

        [Multilingual]
        public virtual string? Title { get; set; }
    }
}
