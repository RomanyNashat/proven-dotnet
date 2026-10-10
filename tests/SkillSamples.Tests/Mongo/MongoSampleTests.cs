using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using SkillSamples.MongoOutbox;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Mongo;

public sealed class MongoSampleTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

    private static Notification For(int userId, string title = "Your result is ready") =>
        new() { UserId = userId, Title = title, CreatedAt = Now };

    private static int WriteErrorCode(Exception? error) => error is MongoWriteException write ? write.WriteError.Code : -1;

    // ---- §4 Validation: the script the skill shows ----------------------------------------------------

    [Fact]
    public async Task ValidatorScript_RunsOnANewDatabase_AndAgain()
    {
        var db = mongo.NewDatabase();

        await MongoShell.RunAsync(db.DatabaseNamespace.DatabaseName, "notifications-validator.js");   // creates the collection
        await MongoShell.RunAsync(db.DatabaseNamespace.DatabaseName, "notifications-validator.js");   // collMod

        var error = await Record.ExceptionAsync(() => db.GetCollection<Notification>("notifications").InsertOneAsync(For(7, new string('x', 201))));
        Assert.True(WriteErrorCode(error) == 121, $"{error}");
    }

    [Fact]
    public async Task CollMod_OnACollectionThatDoesNotExist_Fails()
    {
        var db = mongo.NewDatabase();

        var error = await Record.ExceptionAsync(() => db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", "notifications" },
            { "validator", new BsonDocument("$jsonSchema", new BsonDocument("bsonType", "object")) },
        }));

        Assert.True(error is MongoCommandException, $"{error}");
    }

    [Fact]
    public async Task Validator_AcceptsTheModel_RejectsLongTitlesAndTooManyAttempts()
    {
        var db = mongo.NewDatabase();
        await MongoShell.RunAsync(db.DatabaseNamespace.DatabaseName, "notifications-validator.js");
        var notifications = db.GetCollection<Notification>("notifications");

        await notifications.InsertOneAsync(For(7));
        var longTitle = await Record.ExceptionAsync(() => notifications.InsertOneAsync(For(7, new string('x', 201))));
        var tooMany = await Record.ExceptionAsync(() => notifications.InsertOneAsync(new Notification
        {
            UserId = 7, Title = "retried", CreatedAt = Now,
            Attempts = [.. Enumerable.Range(0, 6).Select(i => new DeliveryAttempt(Now.AddMinutes(i), "timeout"))],
        }));

        Assert.True(WriteErrorCode(longTitle) == 121 && WriteErrorCode(tooMany) == 121, $"title: {longTitle}{Environment.NewLine}attempts: {tooMany}");
    }

    [Fact]
    public async Task Validator_Moderate_StillLetsAnOldInvalidDocumentBeUpdated()
    {
        var db = mongo.NewDatabase();
        var raw = db.GetCollection<BsonDocument>("notifications");
        var old = new BsonDocument { { "userId", 7 }, { "title", "before the validator" } };   // no createdAt, no schemaVersion
        await raw.InsertOneAsync(old);

        await MongoShell.RunAsync(db.DatabaseNamespace.DatabaseName, "notifications-validator.js");
        var update = await raw.UpdateOneAsync(new BsonDocument("_id", old["_id"]), new BsonDocument("$set", new BsonDocument("isRead", true)));
        var newInvalid = await Record.ExceptionAsync(() => raw.InsertOneAsync(new BsonDocument { { "userId", 7 }, { "title", "after" } }));

        Assert.True(update.ModifiedCount == 1 && WriteErrorCode(newInvalid) == 121, $"modified {update.ModifiedCount}; insert: {newInvalid}");
    }

    // ---- §5 Indexes and §6 paging ---------------------------------------------------------------------

    private async Task<IMongoDatabase> NotificationsWith(int count)
    {
        var db = mongo.NewDatabase();
        await MongoShell.RunAsync(db.DatabaseNamespace.DatabaseName, "notifications-validator.js");
        await MongoShell.RunAsync(db.DatabaseNamespace.DatabaseName, "notifications-indexes.js");
        await MongoShell.RunAsync(db.DatabaseNamespace.DatabaseName, "notifications-indexes.js");   // running it again is a no-op
        var notifications = db.GetCollection<Notification>("notifications");
        // Two users, interleaved, so the plain _id index isn't an equally good plan for one user's page.
        await notifications.InsertManyAsync(Enumerable.Range(1, count).SelectMany(i => new[] { For(7, $"n{i}"), For(8, $"other{i}") }));
        return db;
    }

    [Fact]
    public async Task KeysetPaging_WalksEveryNotificationOnce_NewestFirst_OnTheIndex_WithoutASort()
    {
        var db = await NotificationsWith(25);
        var queries = new NotificationQueries(db);

        var page1 = await queries.GetPageAsync(7, null, 10, CancellationToken.None);
        var page2 = await queries.GetPageAsync(7, page1[^1].Id, 10, CancellationToken.None);
        var page3 = await queries.GetPageAsync(7, page2[^1].Id, 10, CancellationToken.None);
        var all = page1.Concat(page2).Concat(page3).Select(n => n.Title).ToList();

        Assert.Equal(Enumerable.Range(1, 25).Reverse().Select(i => $"n{i}"), all);

        var explain = await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument
                {
                    { "find", "notifications" },
                    { "filter", new BsonDocument { { "userId", 7 }, { "_id", new BsonDocument("$lt", page1[^1].Id) } } },
                    { "sort", new BsonDocument("_id", -1) },
                    { "limit", 10 },
                }
            },
            { "verbosity", "executionStats" },
        });
        var plan = explain["queryPlanner"]["winningPlan"].ToJson();
        var stats = explain["executionStats"];
        Assert.True(plan.Contains("ix_userId_id", StringComparison.Ordinal)
            && !Regex.IsMatch(plan, "\"stage\"\\s*:\\s*\"SORT\"")
            && stats["totalDocsExamined"].ToInt32() == 10 && stats["nReturned"].ToInt32() == 10,
            $"{plan}{Environment.NewLine}examined {stats["totalDocsExamined"]}, returned {stats["nReturned"]}");
    }

    [Fact]
    public async Task KeysetPaging_HoldsWhenANotificationArrivesBetweenPages_SkipRepeatsOne()
    {
        var db = await NotificationsWith(25);
        var queries = new NotificationQueries(db);
        var notifications = db.GetCollection<Notification>("notifications");

        var page1 = await queries.GetPageAsync(7, null, 10, CancellationToken.None);
        await notifications.InsertOneAsync(For(7, "arrived meanwhile"));
        var keyset2 = await queries.GetPageAsync(7, page1[^1].Id, 10, CancellationToken.None);
        var skip2 = await notifications.Find(n => n.UserId == 7).SortByDescending(n => n.Id).Skip(10).Limit(10).ToListAsync();

        Assert.Empty(keyset2.Select(n => n.Id).Intersect(page1.Select(n => n.Id)));
        Assert.Equal(page1[^1].Id, skip2[0].Id);   // the last item of page 1, again
    }

    [Fact]
    public async Task PartialIndex_IsNeverUsedByAQueryWithoutItsFilter()
    {
        var db = await NotificationsWith(5);

        var explain = await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument { { "find", "notifications" }, { "filter", new BsonDocument("userId", 7) } } },
            { "verbosity", "queryPlanner" },
        });

        Assert.DoesNotContain("ix_unread_userId", explain["queryPlanner"].ToJson(), StringComparison.Ordinal);
    }

    // ---- §2 Serialization (driver 3.x) ----------------------------------------------------------------

    private sealed class Fee
    {
        public decimal Amount { get; init; }
    }

    private sealed class ExternalRef
    {
        public Guid ExternalId { get; init; }
    }

    [Fact]
    public void Driver3_DecimalIsDecimal128_AndAGuidNeedsAFormat()
    {
        MongoConventions.Register();
        Assert.Equal(BsonType.Decimal128, new Fee { Amount = 12.50m }.ToBsonDocument()["amount"].BsonType);
        Assert.Equal(BsonBinarySubType.UuidStandard, new ExternalRef { ExternalId = Guid.NewGuid() }.ToBsonDocument()["externalId"].AsBsonBinaryData.SubType);

        // Without the registration: the driver's default Guid serializer refuses to write.
        var document = new BsonDocument();
        using var writer = new BsonDocumentWriter(document);
        writer.WriteStartDocument();
        writer.WriteName("id");
        var error = Record.Exception(() => new GuidSerializer().Serialize(BsonSerializationContext.CreateRoot(writer), new BsonSerializationArgs(), Guid.NewGuid()));
        Assert.True(error is BsonSerializationException, $"{error}");
    }

    [Fact]
    public async Task OldDocumentWithARemovedField_StillLoads_AndDatesComeBackUtc()
    {
        var db = mongo.NewDatabase();
        await db.GetCollection<BsonDocument>("notifications").InsertOneAsync(new BsonDocument
        {
            { "schemaVersion", 1 }, { "userId", 7 }, { "title", "old" }, { "createdAt", new BsonDateTime(Now) },
            { "smsChannel", "removed in v2" },
        });

        var loaded = await db.GetCollection<Notification>("notifications").Find(n => n.UserId == 7).SingleAsync();

        Assert.Equal((1, DateTimeKind.Utc, Now), (loaded.SchemaVersion, loaded.CreatedAt.Kind, loaded.CreatedAt));
    }

    private sealed class UsedBefore
    {
        public string? Note { get; init; }
    }

    private sealed class FirstUsedAfter
    {
        public string? Note { get; init; }
    }

    [Fact]
    public void Conventions_RegisteredAfterATypeWasFirstUsed_DoNotChangeIt()
    {
        _ = new UsedBefore().ToBsonDocument();   // builds and caches UsedBefore's class map

        ConventionRegistry.Register("ignore-null-test", new ConventionPack { new IgnoreIfNullConvention(true) },
            t => t == typeof(UsedBefore) || t == typeof(FirstUsedAfter));

        Assert.Equal((1, 0), (new UsedBefore().ToBsonDocument().ElementCount, new FirstUsedAfter().ToBsonDocument().ElementCount));
    }

    private static string Shout(string title) => title.ToUpperInvariant();

    [Fact]
    public async Task Linq_AProjectionThatCallsCSharp_Throws()
    {
        var db = mongo.NewDatabase();
        var notifications = db.GetCollection<Notification>("notifications");
        await notifications.InsertOneAsync(For(7));

        var error = await Record.ExceptionAsync(async () =>
            await notifications.AsQueryable().Where(n => n.UserId == 7).Select(n => Shout(n.Title)).ToListAsync());

        Assert.True(error?.GetType().Name == "ExpressionNotSupportedException", $"{error}");
    }

    // ---- §2 Command metrics -----------------------------------------------------------------------------

    [Fact]
    public async Task CommandMetrics_RecordNameAndOutcome_NeverTheDocument()
    {
        var recorded = new List<string>();
        var tagKeys = new HashSet<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MongoMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                tagKeys.Add(tag.Key);
                if (tag.Key == "command")
                {
                    recorded.Add((string)tag.Value!);
                }
            }
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddMongo(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MongoDB"] = MongoFixture.Url }).Build(),
            "notification-service",
            mongo.NewDatabase().DatabaseNamespace.DatabaseName);
        await using var provider = services.BuildServiceProvider();
        var notifications = provider.GetRequiredService<IMongoDatabase>().GetCollection<Notification>("notifications");

        await notifications.InsertOneAsync(For(7, "Your result is ready"));
        await notifications.Find(n => n.UserId == 7).ToListAsync();

        Assert.True(recorded.Contains("insert") && recorded.Contains("find") && tagKeys.SetEquals(["command", "failed"]),
            $"commands: {string.Join(",", recorded)}; tags: {string.Join(",", tagKeys)}");
    }

    // ---- §7 Transactions and durability -----------------------------------------------------------------

    [Fact]
    public async Task TransactionCallback_RunsAgainAfterATransientError()
    {
        var settings = MongoClientSettings.FromConnectionString(MongoFixture.Url);
        settings.ApplicationName = "transient-error-test";   // the fail point below hits only this client
        var client = new MongoClient(settings);
        var audit = client.GetDatabase(mongo.NewDatabase().DatabaseNamespace.DatabaseName).GetCollection<BsonDocument>("audit");
        await audit.InsertOneAsync(new BsonDocument("event", "created"));
        var admin = client.GetDatabase("admin");
        await admin.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "configureFailPoint", "failCommand" },
            { "mode", new BsonDocument("times", 1) },
            { "data", new BsonDocument
                {
                    { "failCommands", new BsonArray { "insert" } },
                    { "errorCode", 112 },   // WriteConflict
                    { "errorLabels", new BsonArray { "TransientTransactionError" } },
                    { "appName", "transient-error-test" },
                }
            },
        });

        try
        {
            var runs = 0;
            using var session = await client.StartSessionAsync();
            await session.WithTransactionAsync(async (s, ct) =>
            {
                runs++;
                await audit.InsertOneAsync(s, new BsonDocument("event", "moved"), cancellationToken: ct);
                return true;
            });

            Assert.Equal((2, 2L), (runs, await audit.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty)));
        }
        finally
        {
            await admin.RunCommandAsync<BsonDocument>(new BsonDocument { { "configureFailPoint", "failCommand" }, { "mode", "off" } });
        }
    }

    [Fact]
    public async Task DefaultWriteConcern_IsMajority()
    {
        var reply = await mongo.Client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("getDefaultRWConcern", 1));

        Assert.True(reply.TryGetValue("defaultWriteConcern", out var concern)
            && concern.AsBsonDocument.GetValue("w", BsonNull.Value).ToString() == "majority", reply.ToJson());
    }

    // ---- §8 Change streams ------------------------------------------------------------------------------

    private sealed class RecordingHandler(Func<Notification, bool>? failOn = null) : INotificationHandler
    {
        public List<string> Handled { get; } = [];

        public Task HandleAsync(Notification notification, CancellationToken ct)
        {
            if (failOn?.Invoke(notification) == true)
            {
                throw new InvalidOperationException($"could not send {notification.Title}");
            }

            lock (Handled)
            {
                Handled.Add(notification.Title);
            }

            return Task.CompletedTask;
        }
    }

    private static async Task SaveCurrentPositionAsync(IMongoCollection<Notification> notifications, ResumeTokens tokens)
    {
        // What a watcher that started now would save: everything after this point is delivered.
        using var probe = await notifications.WatchAsync(new ChangeStreamOptions { MaxAwaitTime = TimeSpan.FromMilliseconds(200) });
        await probe.MoveNextAsync();
        await tokens.SaveAsync(NewNotificationsWatcher.Stream, probe.GetResumeToken(), CancellationToken.None);
    }

    private static async Task<List<string>> RunUntilAsync(IMongoDatabase db, ResumeTokens tokens, int expected)
    {
        var handler = new RecordingHandler();
        using var watcher = new NewNotificationsWatcher(db, tokens, handler);
        await watcher.StartAsync(CancellationToken.None);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(20) && handler.Handled.Count < expected && !watcher.ExecuteTask!.IsCompleted)
        {
            await Task.Delay(100);
        }

        await Task.Delay(300);   // anything extra would arrive now
        Assert.False(watcher.ExecuteTask!.IsCompleted, $"the watcher stopped on its own: {watcher.ExecuteTask.Exception}");
        await watcher.StopAsync(CancellationToken.None);
        return handler.Handled;
    }

    [Fact]
    public async Task Watcher_AfterARestart_GetsWhatArrivedWhileItWasDown_AndNothingTwice()
    {
        var db = mongo.NewDatabase();
        var notifications = db.GetCollection<Notification>("notifications");
        var tokens = new ResumeTokens(db);
        await db.CreateCollectionAsync("notifications");
        await SaveCurrentPositionAsync(notifications, tokens);

        await notifications.InsertManyAsync([For(7, "a"), For(7, "b"), For(7, "c")]);
        var first = await RunUntilAsync(db, tokens, 3);
        await notifications.InsertManyAsync([For(7, "d"), For(7, "e")]);   // the watcher is stopped
        var second = await RunUntilAsync(db, tokens, 2);

        Assert.Equal(new[] { "a", "b", "c" }, first);
        Assert.Equal(new[] { "d", "e" }, second);
    }

    [Fact]
    public async Task Watcher_WhenHandlingFails_Stops_AndTheRestartGetsTheFailedOneAgain()
    {
        var db = mongo.NewDatabase();
        var notifications = db.GetCollection<Notification>("notifications");
        var tokens = new ResumeTokens(db);
        await db.CreateCollectionAsync("notifications");
        await SaveCurrentPositionAsync(notifications, tokens);
        await notifications.InsertManyAsync([For(7, "a"), For(7, "b"), For(7, "c")]);

        var failing = new RecordingHandler(n => n.Title == "b");
        using (var watcher = new NewNotificationsWatcher(db, tokens, failing))
        {
            await watcher.StartAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => watcher.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(20)));
        }

        var afterRestart = await RunUntilAsync(db, tokens, 2);

        Assert.Equal(new[] { "a" }, failing.Handled);
        Assert.Equal(new[] { "b", "c" }, afterRestart);
    }

    // Sends a push once per notification, even when the change stream delivers it twice: the "sent" record
    // is keyed by the notification's _id, so a second delivery finds it and sends nothing.
    private sealed class OncePerNotificationPush(IMongoDatabase db, string? killedAfterSending = null) : INotificationHandler
    {
        private readonly IMongoCollection<BsonDocument> _sent = db.GetCollection<BsonDocument>("sentPushes");

        public List<string> Delivered { get; } = [];
        public List<string> Pushed { get; } = [];

        public async Task HandleAsync(Notification notification, CancellationToken ct)
        {
            lock (Delivered)
            {
                Delivered.Add(notification.Title);
            }

            try
            {
                await _sent.InsertOneAsync(new BsonDocument("_id", notification.Id), cancellationToken: ct);
            }
            catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                return;   // pushed before the last crash
            }

            lock (Pushed)
            {
                Pushed.Add(notification.Title);
            }

            if (notification.Title == killedAfterSending)
            {
                throw new InvalidOperationException("pod killed before saving its position");
            }
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ThePushPodIsKilledAfterSendingButBeforeSavingItsPlace_TheUserStillGetsEachPushOnce()
    {
        var db = mongo.NewDatabase();
        var notifications = db.GetCollection<Notification>("notifications");
        var tokens = new ResumeTokens(db);
        await db.CreateCollectionAsync("notifications");
        await SaveCurrentPositionAsync(notifications, tokens);

        // Given: three results are ready, and the pod sending pushes dies right after sending the second
        await notifications.InsertManyAsync([For(7, "a"), For(7, "b"), For(7, "c")]);
        var dying = new OncePerNotificationPush(db, killedAfterSending: "b");
        using (var watcher = new NewNotificationsWatcher(db, tokens, dying))
        {
            await watcher.StartAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => watcher.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(20)));
        }

        // When: the replacement pod starts from the last saved place
        var replacement = new OncePerNotificationPush(db);
        using (var watcher = new NewNotificationsWatcher(db, tokens, replacement))
        {
            await watcher.StartAsync(CancellationToken.None);
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(20) && !Seen("c"))
            {
                await Task.Delay(100);
            }

            await watcher.StopAsync(CancellationToken.None);
        }

        // Then: the stream delivered "b" again (at least once), and the user got one push each
        bool Seen(string title)
        {
            lock (replacement.Delivered)
            {
                return replacement.Delivered.Contains(title);
            }
        }

        Assert.Equal(new[] { "a", "b" }, dying.Delivered);
        Assert.Equal(new[] { "b", "c" }, replacement.Delivered);
        Assert.Equal(new[] { "a", "b", "c" }, dying.Pushed.Concat(replacement.Pushed).ToArray());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_ArabicTitlesAndUtcDatesRoundTrip_AndPagingWorks()
    {
        ProductionConditions.Require();
        var db = mongo.NewDatabase();
        var notifications = db.GetCollection<Notification>("notifications");
        await notifications.InsertManyAsync([For(9, "نتيجة التحليل جاهزة"), For(9, "موعدك غداً الساعة ٩")]);

        var page = await new NotificationQueries(db).GetPageAsync(userId: 9, after: null, size: 10, CancellationToken.None);

        Assert.Equal(new[] { "موعدك غداً الساعة ٩", "نتيجة التحليل جاهزة" }, page.Select(n => n.Title).ToArray());
        Assert.All(page, n => Assert.Equal((Now, DateTimeKind.Utc), (n.CreatedAt, n.CreatedAt.Kind)));
    }
}
