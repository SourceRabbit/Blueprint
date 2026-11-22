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
                        CheckInsertRangeRollback(database);
                        CheckUpdateRange(database);
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

        /// <summary>Ελέγχει μαζικές ενημερώσεις με rowversion και rollback όταν μία γραμμή άλλαξε ή διαγράφηκε.</summary>
        /// <param name="database">Η προσωρινή βάση του ελέγχου.</param>
        /// <exception cref="InvalidOperationException">Οι τιμές της βάσης ή τα rowversions δεν συμφωνούν με την έκβαση της συναλλαγής.</exception>
        private void CheckUpdateRange(BlueprintDatabase database)
        {
            ClassRecord first = ClassRecord.Create();
            ClassRecord second = ClassRecord.Create();
            first.Code = "bulk-a";
            second.Code = "bulk-b";
            DatabaseTransaction insert = database.TransactionFactory.CreateNewTransaction();
            insert.AddInsertRange([first, second]);
            database.TransactionFactory.ExecuteTransactionAsync(insert).GetAwaiter().GetResult();
            ClassRecord stale = ClassRecord.SingleOrDefault(item => item.ID == second.ID)!;
            byte[] firstVersion = first.Version.ToArray();
            first.Code = "bulk-a2";
            second.Code = "bulk-b2";
            DatabaseTransaction update = database.TransactionFactory.CreateNewTransaction();
            update.AddUpdateRange([first, second]);
            Assert(database.TransactionFactory.ExecuteTransactionAsync(update).GetAwaiter().GetResult() == 2, "Both updates must commit together.");
            Assert(!first.Version.SequenceEqual(firstVersion) && !second.Version.SequenceEqual(stale.Version), "Successful range updates must return new rowversions.");
            firstVersion = first.Version.ToArray();
            first.Code = "bulk-uncommitted";
            DatabaseTransaction conflict = database.TransactionFactory.CreateNewTransaction();
            conflict.AddUpdateRange([first]);
            conflict.AddUpdateRange([stale]);
            AssertThrows<DbUpdateConcurrencyException>(() => database.TransactionFactory.ExecuteTransactionAsync(conflict).GetAwaiter().GetResult(), "A stale rowversion must roll back earlier ranges.");
            Assert(first.Version.SequenceEqual(firstVersion) && ClassRecord.SingleOrDefault(item => item.ID == first.ID)!.Code == "bulk-a2", "Rollback must restore generated values and database changes.");
            ClassRecord.Delete(item => item.ID == second.ID);
            DatabaseTransaction missing = database.TransactionFactory.CreateNewTransaction();
            missing.AddUpdateRange([first, second]);
            AssertThrows<DbUpdateConcurrencyException>(() => database.TransactionFactory.ExecuteTransactionAsync(missing).GetAwaiter().GetResult(), "A deleted row must fail the entire range.");
            Assert(ClassRecord.SingleOrDefault(item => item.ID == first.ID)!.Code == "bulk-a2", "A missing sibling must not leave partial updates.");
            ClassRecord.Delete(item => item.ID == first.ID);
        }

        /// <summary>Ελέγχει τις παραγόμενες τιμές διαδοχικών ομάδων εισαγωγής και την αποκατάστασή τους μετά από rollback.</summary>
        /// <param name="database">Η προσωρινή βάση του ελέγχου.</param>
        /// <exception cref="InvalidOperationException">Οι εγγραφές ή τα entities έχουν απροσδόκητη κατάσταση.</exception>
        private void CheckInsertRangeRollback(BlueprintDatabase database)
        {
            ClassGroup first = ClassGroup.Create();
            ClassGroup second = ClassGroup.Create();
            first.Name = "First";
            second.Name = "Second";
            ClassRecord firstChild = ClassRecord.Create();
            ClassRecord secondChild = ClassRecord.Create();
            firstChild.Code = "first";
            secondChild.Code = "second";
            firstChild.Group = first;
            secondChild.Group = second;
            DatabaseTransaction transaction = database.TransactionFactory.CreateNewTransaction();
            transaction.AddInsertRange([first, second]);
            transaction.AddInsertRange([firstChild, secondChild]);
            bool keysResolved = false;
            transaction.AddValidation(() =>
            {
                keysResolved = first.ID > 0 && second.ID > 0 && first.ID != second.ID && firstChild.GroupID == first.ID && secondChild.GroupID == second.ID
                    && firstChild.ID > 0 && secondChild.ID > 0 && firstChild.Version.Length == 8 && secondChild.Version.Length == 8;
                throw new InvalidOperationException("Expected insert range rollback.");
            });
            AssertThrows<InvalidOperationException>(() => database.TransactionFactory.ExecuteTransactionAsync(transaction).GetAwaiter().GetResult(), "Validation must roll back both insert ranges.");
            Assert(keysResolved && transaction.Operations.Count == 4, "Each child must receive the key of its own principal.");
            Assert(first.ID == 0 && second.ID == 0 && firstChild.ID == 0 && secondChild.ID == 0 && firstChild.GroupID == null && secondChild.GroupID == null, "Rollback must restore all generated keys.");
            Assert(firstChild.Version.Length == 0 && secondChild.Version.Length == 0 && !ClassGroup.Any() && !ClassRecord.Any(), "Rollback must restore rowversions and leave both tables empty.");
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
