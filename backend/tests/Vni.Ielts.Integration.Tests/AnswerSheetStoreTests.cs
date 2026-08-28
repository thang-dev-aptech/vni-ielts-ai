using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The answer sheet's two writes, against a real MongoDB.
///
/// <b>Every shape here is one an in-memory fake gets right for free and the
/// driver does not.</b> A dictionary merges, upserts and increments without
/// being asked; a document does none of those unless the update says so, and
/// says so in a way that is atomic against a second writer. So these run
/// against a real server or they do not run.
///
/// The sheet keeps two layers — answers written before 27/08/2026 as an array,
/// everything since as a map — and the tests below hold both: that a patch
/// lands, that it does not disturb its neighbours, and that an old sheet keeps
/// what it had when a new write arrives on top of it.
/// </summary>
public sealed class AnswerSheetStoreTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private static readonly ExamSessionId Sitting = ExamSessionId.New();

    private IServiceScope Scope() => app.Services.CreateScope();

    private static IAnswerSheetStore StoreIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IAnswerSheetStore>();

    private static readonly DateTimeOffset At = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Setting_one_entry_creates_the_sheet_when_there_is_none()
    {
        // The first Speaking recording of a sitting arrives before anything
        // else has written that sheet. Without the upsert it would land
        // nowhere, and the learner's Part 1 would simply not exist.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.SetAnswerAsync(
            session, ExamModule.Speaking, "s-part-1", "rec-first", At, default);

        var sheet = await store.LoadAsync(session, ExamModule.Speaking, default);
        Assert.Equal("rec-first", sheet["s-part-1"]);
    }

    [SkippableFact]
    public async Task Setting_one_entry_leaves_every_other_entry_alone()
    {
        // The property the Speaking upload is built on. Read-modify-write would
        // pass this test single-threaded and lose an answer the moment two
        // parts finished uploading together; a positional update cannot.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "paper", ["r-2"] = "wood", ["r-3"] = null },
            At, default);

        await store.SetAnswerAsync(session, ExamModule.Reading, "r-2", "stone", At, default);

        var sheet = await store.LoadAsync(session, ExamModule.Reading, default);

        Assert.Equal("paper", sheet["r-1"]);
        Assert.Equal("stone", sheet["r-2"]);
        Assert.Null(sheet["r-3"]);
        Assert.Equal(3, sheet.Count);
    }

    [SkippableFact]
    public async Task Re_recording_a_part_replaces_its_entry_rather_than_adding_a_second()
    {
        // A learner who re-records Part 2 must end with one answer for it. Two
        // entries for one question is the corruption `LoadAsync` tolerates
        // rather than invites — it takes the last, so a duplicate would be
        // survivable and still wrong.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.SetAnswerAsync(session, ExamModule.Speaking, "s-part-2", "rec-take-1", At, default);
        await store.SetAnswerAsync(session, ExamModule.Speaking, "s-part-2", "rec-take-2", At, default);

        var sheet = await store.LoadAsync(session, ExamModule.Speaking, default);

        Assert.Equal("rec-take-2", sheet["s-part-2"]);
        Assert.Single(sheet);
    }

    [SkippableFact]
    public async Task Sheets_are_scoped_to_one_section_of_one_sitting()
    {
        // The key is session plus module. A mistake here would show a learner
        // their Reading answers inside Listening, or somebody else's inside
        // both — and it is the kind of mistake that only appears once two
        // sittings exist, which is never in a single-test fixture.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var other = ExamSessionId.New();

        await store.SetAnswerAsync(Sitting, ExamModule.Reading, "r-1", "mine", At, default);
        await store.SetAnswerAsync(other, ExamModule.Reading, "r-1", "theirs", At, default);
        await store.SetAnswerAsync(Sitting, ExamModule.Listening, "l-1", "elsewhere", At, default);

        Assert.Equal("mine", (await store.LoadAsync(Sitting, ExamModule.Reading, default))["r-1"]);
        Assert.Equal("theirs", (await store.LoadAsync(other, ExamModule.Reading, default))["r-1"]);
        Assert.Equal(
            "elsewhere", (await store.LoadAsync(Sitting, ExamModule.Listening, default))["l-1"]);
    }

    // ── Patching ──────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The bug the whole-sheet write could not be rid of, gone rather than
    /// detected.</b>
    ///
    /// An autosave used to carry the whole sheet, so a stale one did not merge
    /// — it erased. Compare-and-swap made that visible and left it just as
    /// fatal: the client took the new revision and re-sent the same whole
    /// sheet, finishing the overwrite the compare had refused.
    ///
    /// Two patches on different questions have nothing to contradict. Both land
    /// whatever order they arrive in, and no client has to reconcile anything.
    /// </summary>
    [SkippableFact]
    public async Task Two_writers_on_different_questions_both_keep_their_answers()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        // One tab answers r-1 …
        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "paper" }, At, default);

        // … and another, which never saw that, answers r-2. Under a whole-sheet
        // write this second one deletes r-1.
        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-2"] = "wood" }, At, default);

        var sheet = await store.ReadAsync(session, ExamModule.Reading, default);

        Assert.Equal("paper", sheet.Answers["r-1"]);
        Assert.Equal("wood", sheet.Answers["r-2"]);
        Assert.Equal(2, sheet.Revision);
    }

    /// <summary>
    /// A cleared answer is a written null, and it survives being written.
    ///
    /// <b>This is the distinction a whole sheet could not draw.</b> There, a
    /// blank meant both "the learner rubbed this out" and "this client has
    /// never heard of this question", so no rule could delete the first without
    /// deleting the second. In a patch a blank appears only because somebody
    /// cleared it.
    /// </summary>
    [SkippableFact]
    public async Task Clearing_an_answer_writes_a_null_and_leaves_its_neighbours()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "paper", ["r-2"] = "wood" }, At, default);

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = null }, At, default);

        var sheet = await store.ReadAsync(session, ExamModule.Reading, default);

        Assert.Null(sheet.Answers["r-1"]);
        Assert.Equal("wood", sheet.Answers["r-2"]);
    }

    /// <summary>
    /// <b>The revision reports what was amended, not what a caller guessed.</b>
    ///
    /// It is the only thing that tells a second tab it is behind. If it were
    /// derived by the caller as "one less than what came back", the day the
    /// increment stops being one is the day every caller is quietly wrong.
    /// </summary>
    [SkippableFact]
    public async Task A_patch_reports_the_revision_it_amended()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        var first = await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "paper" }, At, default);

        Assert.Equal(0, first.PreviousRevision);
        Assert.Equal(1, first.Sheet.Revision);

        var second = await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-2"] = "wood" }, At, default);

        Assert.Equal(1, second.PreviousRevision);
        Assert.Equal(2, second.Sheet.Revision);

        // And the sheet it hands back is merged, not just the patch — that is
        // what a caller who was behind reads to catch up.
        Assert.Equal("paper", second.Sheet.Answers["r-1"]);
        Assert.Equal("wood", second.Sheet.Answers["r-2"]);
    }

    /// <summary>
    /// An empty patch changes nothing, including the revision.
    ///
    /// <b>Not a micro-optimisation.</b> A revision that moves when nothing
    /// changed tells every other reader its view is stale, so two idle tabs
    /// would pull the whole section back and forth on a timer, for ever.
    /// </summary>
    [SkippableFact]
    public async Task An_empty_patch_does_not_move_the_revision()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "paper" }, At, default);

        var idle = await store.PatchAsync(
            session, ExamModule.Reading, new Dictionary<string, string?>(), At, default);

        Assert.Equal(1, idle.Sheet.Revision);
        Assert.Equal(1, idle.PreviousRevision);
        Assert.Equal("paper", idle.Sheet.Answers["r-1"]);
    }

    /// <summary>
    /// Two writers on the <i>same</i> question, at the same instant.
    ///
    /// <b>A barrier, not two sequential calls.</b> Sequential writes exercise
    /// the update; they do not exercise the race. Both tasks are held until
    /// both are ready, so only the database's own atomicity separates them.
    ///
    /// This is the one case a patch cannot make disappear, and it does not need
    /// to: the answer ends up as one of the two, whole, and the revision counts
    /// both writes. A blend, a duplicate entry, or a lost increment would each
    /// be a defect.
    /// </summary>
    [SkippableFact]
    public async Task Two_writers_on_one_question_leave_one_answer_and_count_both_writes()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        using var barrier = new Barrier(2);

        async Task Racer(string value)
        {
            barrier.SignalAndWait();

            await store.PatchAsync(
                session, ExamModule.Reading,
                new Dictionary<string, string?> { ["r-1"] = value }, At, default);
        }

        await Task.WhenAll(Task.Run(() => Racer("wood")), Task.Run(() => Racer("stone")));

        var sheet = await store.ReadAsync(session, ExamModule.Reading, default);

        Assert.Contains(sheet.Answers["r-1"], new[] { "wood", "stone" });
        Assert.Single(sheet.Answers);
        Assert.Equal(2, sheet.Revision);
    }

    /// <summary>
    /// Two first-writes for one section both land.
    ///
    /// <b>The upsert is where two writers can both find no document.</b> Mongo
    /// refuses the second on the duplicate <c>_id</c>, and that refusal has to
    /// become a retry rather than an exception escaping into a learner's
    /// autosave — because by then the document exists, so the same patch
    /// applies as an update and nothing is lost.
    /// </summary>
    [SkippableFact]
    public async Task Two_first_writes_for_one_section_both_land()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        using var barrier = new Barrier(2);

        async Task Racer(string question, string value)
        {
            barrier.SignalAndWait();

            await store.PatchAsync(
                session, ExamModule.Listening,
                new Dictionary<string, string?> { [question] = value }, At, default);
        }

        await Task.WhenAll(
            Task.Run(() => Racer("l-1", "alpha")),
            Task.Run(() => Racer("l-2", "beta")));

        var sheet = await store.ReadAsync(session, ExamModule.Listening, default);

        Assert.Equal("alpha", sheet.Answers["l-1"]);
        Assert.Equal("beta", sheet.Answers["l-2"]);
    }

    /// <summary>
    /// A sheet written before 27/08/2026 keeps its answers when patched.
    ///
    /// <b>The array is read under the map and never rewritten.</b> Converting
    /// it would need a migration and a window in which a sitting in progress
    /// cannot save — during an exam. Reading both layers costs two lines and
    /// has neither, and this is the test that says the old layer is still
    /// there after a new write lands on top of it.
    ///
    /// Without it the learner would watch a section they had filled in empty
    /// itself on their next keystroke, with the chip reading "Đã lưu".
    /// </summary>
    [SkippableFact]
    public async Task A_sheet_in_the_old_array_shape_keeps_its_answers_when_patched()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var database = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        var session = ExamSessionId.New();

        // Exactly what the store used to write: answers as an array of
        // documents, and no `revision` field at all.
        await database.GetCollection<BsonDocument>("answer_sheets").InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = $"{session.Value}:{ExamModule.Reading}",
                ["sessionId"] = session.Value,
                ["module"] = ExamModule.Reading.ToString(),
                ["answers"] = new BsonArray
                {
                    new BsonDocument { ["questionId"] = "r-1", ["value"] = "paper" },
                    new BsonDocument { ["questionId"] = "r-2", ["value"] = "wood" },
                },
                ["updatedAt"] = At.UtcDateTime,
            },
            cancellationToken: default);

        var before = await store.ReadAsync(session, ExamModule.Reading, default);
        Assert.Equal(0, before.Revision);
        Assert.Equal("paper", before.Answers["r-1"]);

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-3"] = "stone" }, At, default);

        var after = await store.ReadAsync(session, ExamModule.Reading, default);

        Assert.Equal("paper", after.Answers["r-1"]);
        Assert.Equal("wood", after.Answers["r-2"]);
        Assert.Equal("stone", after.Answers["r-3"]);
        Assert.Equal(1, after.Revision);
    }

    /// <summary>
    /// The map layer wins over the array layer for the same question.
    ///
    /// An old sheet that is edited again must show the edit, not the original.
    /// Reading the array last would restore an answer the learner has already
    /// changed — silently, and only for sittings that predate the change.
    /// </summary>
    [SkippableFact]
    public async Task Patching_a_question_the_old_array_holds_replaces_its_value()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var database = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
        var session = ExamSessionId.New();

        await database.GetCollection<BsonDocument>("answer_sheets").InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = $"{session.Value}:{ExamModule.Writing}",
                ["sessionId"] = session.Value,
                ["module"] = ExamModule.Writing.ToString(),
                ["answers"] = new BsonArray
                {
                    new BsonDocument { ["questionId"] = "w-1", ["value"] = "draft" },
                },
                ["updatedAt"] = At.UtcDateTime,
            },
            cancellationToken: default);

        await store.PatchAsync(
            session, ExamModule.Writing,
            new Dictionary<string, string?> { ["w-1"] = "final" }, At, default);

        var sheet = await store.ReadAsync(session, ExamModule.Writing, default);

        Assert.Equal("final", sheet.Answers["w-1"]);
        Assert.Single(sheet.Answers);
    }

    /// <summary>
    /// Two recordings filed at the same instant both reach the sheet.
    ///
    /// <b>The race the two-statement version could lose.</b> It tried a
    /// positional update and fell back to a push, and two parts finishing
    /// together could both take the push branch and leave two entries for one
    /// question — survivable only because the read happened to take the last.
    /// Against a map there is no branch and no duplicate.
    /// </summary>
    [SkippableFact]
    public async Task Two_recordings_filed_at_once_both_reach_the_sheet()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        using var barrier = new Barrier(2);

        async Task Racer(string question, string recording)
        {
            barrier.SignalAndWait();
            await store.SetAnswerAsync(
                session, ExamModule.Speaking, question, recording, At, default);
        }

        await Task.WhenAll(
            Task.Run(() => Racer("s-part-1", "rec-one")),
            Task.Run(() => Racer("s-part-2", "rec-two")));

        var sheet = await store.LoadAsync(session, ExamModule.Speaking, default);

        Assert.Equal("rec-one", sheet["s-part-1"]);
        Assert.Equal("rec-two", sheet["s-part-2"]);
    }

    // ── The closure protocol ─────────────────────────────────────────────
    //
    // The sheet and the sitting are different collections. The transition
    // compare-and-swap guards the session document and says nothing at all
    // about a write to the sheet, so until 27/08/2026 this interleaving lost
    // work and reported success:
    //
    //   1. an autosave loads the sitting and finds its section open
    //   2. a submit wins the CAS and marks the sheet at revision R
    //   3. the autosave's patch lands — revision R+1
    //
    // The learner's chip read "Đã lưu" and the result was computed without that
    // answer. These are the tests that fail if the freeze is removed.

    [SkippableFact]
    public async Task A_frozen_sheet_refuses_a_patch()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "before" }, At, default);

        await store.CloseAsync(session, ExamModule.Reading, At, default);

        await Assert.ThrowsAsync<SectionSheetClosedException>(() =>
            store.PatchAsync(
                session, ExamModule.Reading,
                new Dictionary<string, string?> { ["r-2"] = "after" }, At, default));

        // And the refusal is a refusal, not a partial write.
        var sheet = await store.LoadAsync(session, ExamModule.Reading, default);
        Assert.Equal("before", sheet["r-1"]);
        Assert.False(sheet.ContainsKey("r-2"));
    }

    [SkippableFact]
    public async Task Closing_a_section_nobody_answered_still_refuses_a_later_write()
    {
        // Without the upsert there would be no document to carry the freeze,
        // and a late write would find nothing to refuse it. A learner who wrote
        // nothing is exactly the case where a stray patch is least expected and
        // most damaging: it would appear in a sheet marking has already read.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        var frozen = await store.CloseAsync(session, ExamModule.Listening, At, default);
        Assert.Empty(frozen.Answers);

        await Assert.ThrowsAsync<SectionSheetClosedException>(() =>
            store.PatchAsync(
                session, ExamModule.Listening,
                new Dictionary<string, string?> { ["l-1"] = "late" }, At, default));
    }

    [SkippableFact]
    public async Task Closing_twice_returns_the_same_frozen_sheet()
    {
        // Two tabs on "Nộp bài", a submit meeting the expiry sweep, and a
        // retried request all reach the freeze. Re-freezing at a later revision
        // would change the content marking has already read, which is the
        // failure the freeze exists to prevent, arriving by another door.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "kept" }, At, default);

        var first = await store.CloseAsync(session, ExamModule.Reading, At, default);
        var second = await store.CloseAsync(session, ExamModule.Reading, At.AddMinutes(5), default);

        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(first.Answers["r-1"], second.Answers["r-1"]);
    }

    /// <summary>
    /// <b>The barrier: a patch held past validation, released after the freeze.</b>
    ///
    /// This is the interleaving in production — an autosave that has already
    /// passed every check the handler makes, sitting in the network while a
    /// submit closes the section underneath it. The two are started together
    /// and the freeze is given a head start, which is what a real submit has
    /// once the autosave's own round trip is in flight.
    ///
    /// The property is not "the patch fails". It is that <b>there is no third
    /// outcome</b>: either the patch commits before the freeze and the frozen
    /// sheet contains it, or it is refused and the client is never told it
    /// landed. An accepted write that the frozen sheet does not contain is the
    /// data loss, and it is what this asserts against.
    /// </summary>
    [SkippableFact]
    public async Task An_autosave_racing_a_freeze_either_lands_before_it_or_is_refused()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        for (var round = 0; round < 25; round++)
        {
            using var scope = Scope();
            var store = StoreIn(scope);
            var session = ExamSessionId.New();

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var patching = Task.Run(async () =>
            {
                await gate.Task;
                try
                {
                    await store.PatchAsync(
                        session, ExamModule.Reading,
                        new Dictionary<string, string?> { ["r-1"] = "racing" }, At, default);
                    return true;
                }
                catch (SectionSheetClosedException)
                {
                    return false;
                }
            });

            var closing = Task.Run(async () =>
            {
                await gate.Task;
                return await store.CloseAsync(session, ExamModule.Reading, At, default);
            });

            gate.SetResult();

            var accepted = await patching;
            var frozen = await closing;

            if (accepted)
            {
                Assert.True(
                    frozen.Answers.TryGetValue("r-1", out var value) && value == "racing",
                    $"Round {round}: the patch was accepted and the frozen sheet does not "
                    + "contain it. That is the exact loss the closure protocol exists to "
                    + "prevent — the learner was told the answer was saved and the result "
                    + "was computed without it.");
            }
            else
            {
                Assert.False(
                    frozen.Answers.ContainsKey("r-1"),
                    $"Round {round}: the patch was refused and the sheet contains it anyway.");
            }
        }
    }

    [SkippableFact]
    public async Task A_frozen_sheet_refuses_a_recording_too()
    {
        // Speaking writes through `SetAnswerAsync`, not through a patch, so it
        // needs the same barrier or the recording path walks straight past the
        // freeze. → I2.2
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.CloseAsync(session, ExamModule.Speaking, At, default);

        await Assert.ThrowsAsync<SectionSheetClosedException>(() =>
            store.SetAnswerAsync(
                session, ExamModule.Speaking, "s-part-1", "rec-late", At, default));

        var sheet = await store.LoadAsync(session, ExamModule.Speaking, default);
        Assert.False(sheet.ContainsKey("s-part-1"));
    }


    // ── Per-question ordering ────────────────────────────────────────────
    //
    // Mongo's arrival order is not the learner's edit order. Two writes for one
    // question can be reordered by a retry on a changed network, a proxy, a
    // stalled request, or a second tab — and the stored value then becomes
    // whichever the server applied last, which is the older answer as often as
    // the newer one. The learner watches their correction revert and nothing on
    // screen says why.
    //
    // The revision cannot answer this. It is one number for the whole sheet, so
    // it says whether a caller was behind, not which of two edits to one
    // question came second.

    [SkippableFact]
    public async Task A_stale_write_for_the_same_question_is_ignored()
    {
        // The report's own interleaving: A is composed first with "cat" and is
        // delayed; B is composed second with "dog" and arrives first. The final
        // value must be "dog".
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        // B, composed second, arrives first.
        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "dog" }, At, default,
            new Dictionary<string, long> { ["r-1"] = 2 });

        // A, composed first, arrives late.
        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "cat" }, At, default,
            new Dictionary<string, long> { ["r-1"] = 1 });

        var sheet = await store.LoadAsync(session, ExamModule.Reading, default);

        Assert.Equal("dog", sheet["r-1"]);
    }

    [SkippableFact]
    public async Task A_stale_write_does_not_hold_back_the_other_questions_in_its_batch()
    {
        // The ordering rule is per question, not per request. A batch carrying
        // one stale entry and one fresh one must apply the fresh one — dropping
        // the whole batch would be the data loss of I1.1 arriving by a
        // different door.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "newer" }, At, default,
            new Dictionary<string, long> { ["r-1"] = 5 });

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "older", ["r-2"] = "first time" },
            At, default,
            new Dictionary<string, long> { ["r-1"] = 4, ["r-2"] = 1 });

        var sheet = await store.LoadAsync(session, ExamModule.Reading, default);

        Assert.Equal("newer", sheet["r-1"]);
        Assert.Equal("first time", sheet["r-2"]);
    }

    [SkippableFact]
    public async Task An_equal_token_is_ignored_so_a_replayed_request_cannot_move_the_value()
    {
        // Strictly greater, not greater-or-equal. A retried request carries the
        // token it carried the first time, so treating equal as newer would let
        // a replay overwrite an edit made in between.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "kept" }, At, default,
            new Dictionary<string, long> { ["r-1"] = 7 });

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "replay" }, At, default,
            new Dictionary<string, long> { ["r-1"] = 7 });

        var sheet = await store.LoadAsync(session, ExamModule.Reading, default);

        Assert.Equal("kept", sheet["r-1"]);
    }

    [SkippableFact]
    public async Task A_cleared_answer_is_ordered_like_any_other_write()
    {
        // A null is an erase, and an erase can be stale too. Exempting it would
        // mean a late "the learner rubbed this out" deleting the answer they
        // typed afterwards.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "typed again" }, At, default,
            new Dictionary<string, long> { ["r-1"] = 3 });

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = null }, At, default,
            new Dictionary<string, long> { ["r-1"] = 2 });

        var sheet = await store.LoadAsync(session, ExamModule.Reading, default);

        Assert.Equal("typed again", sheet["r-1"]);
    }

    [SkippableFact]
    public async Task The_sheet_reports_the_tokens_it_holds_so_a_caller_can_catch_up()
    {
        // A tab that takes in another writer's answers has to raise its own
        // counters past theirs. Not told, its next edit to one of those
        // questions carries a token the server ignores — and the learner
        // watches their correction do nothing at all.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "theirs" }, At, default,
            new Dictionary<string, long> { ["r-1"] = 11 });

        var sheet = await store.ReadAsync(session, ExamModule.Reading, default);

        Assert.Equal(11, sheet.SequenceOf("r-1"));
        Assert.Equal(-1, sheet.SequenceOf("never-written"));
    }

    [SkippableFact]
    public async Task A_patch_with_no_tokens_still_writes()
    {
        // The contract before 27/08/2026, and what a client that has not been
        // updated sends. Refusing it would break every such caller to gain a
        // guarantee they were not asking for.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var store = StoreIn(scope);
        var session = ExamSessionId.New();

        await store.PatchAsync(
            session, ExamModule.Reading,
            new Dictionary<string, string?> { ["r-1"] = "no token" }, At, default);

        var sheet = await store.LoadAsync(session, ExamModule.Reading, default);

        Assert.Equal("no token", sheet["r-1"]);
    }

}
