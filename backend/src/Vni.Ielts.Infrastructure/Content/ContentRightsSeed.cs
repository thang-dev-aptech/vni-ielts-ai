using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// The content this project has, and what may be done with it.
///
/// <para>
/// <b>Every entry grants <c>fixture</c> and nothing else, and that is the
/// whole point.</b> <c>M-53</c> — which papers may be shown to a learner — is
/// open: the owner has said some are cleared and has not said which. An
/// unresolved policy becomes a configured seam with a null implementation,
/// never an invented default (`G-11`), so the seam is here, wired, tested and
/// empty. Granting a right is an act by a named reviewer with a licence
/// reference, and neither of those can come from a source file.
/// </para>
///
/// <para>
/// <b>Presence of a file grants nothing.</b> A source with no entry here is
/// refused everywhere by <see cref="ContentRightsPolicy"/>, so this list being
/// incomplete is safe in the only direction that matters. What it is not safe
/// to do is add a <c>LearnerProduction</c> entry — <c>ContentRightsSeedTests</c>
/// fails if anybody does.
/// </para>
///
/// <para>
/// <b>Paths, not bytes.</b> <c>/exam/</c> and <c>/Đề IELTS/</c> are gitignored
/// because nobody has established the right to redistribute them, so the
/// material is absent in CI and in a clean checkout. These records reference
/// where it would be; a hash is a fingerprint rather than content, and the
/// verification path reports "missing" rather than pretending agreement.
/// </para>
///
/// <para>
/// <b>Owner and licence are recorded as unknown where they are unknown.</b>
/// <c>null</c> owner means nobody has established one — it is not shorthand
/// for VNI. The publisher names below are statements of who printed the
/// material, not claims about who holds a licence.
/// </para>
/// </summary>
public static class ContentRightsSeed
{
    /// <summary>The one right anything in this workspace holds today.</summary>
    private static readonly ContentEnvironment[] FixtureOnly = [ContentEnvironment.Fixture];

    private const string CamRoot = "Đề IELTS/Đề CAM";

    private const string Vol9Root =
        "Đề IELTS/Đề CAM/Đề thi thật (Chỉ L và R) VOL 9 - REAL IELTS-20260819T082203Z-1-001"
        + "/VOL 9 - REAL IELTS";

    private const string CambridgePublisher = "Cambridge University Press & Assessment (publisher)";

    /// <summary>
    /// Built once. The list is static data, and every record is immutable.
    /// </summary>
    public static IReadOnlyList<ContentSource> Sources { get; } = Build();

    private static ContentSource Fixture(
        string id, string title, string? owner, string rootPath,
        IEnumerable<ContentFileRef>? files = null,
        IEnumerable<ExamVersionId>? examVersionIds = null,
        IEnumerable<ExamDefinitionId>? examDefinitionIds = null) =>
        ContentSource.Register(
            new ContentSourceId(id),
            title,
            owner,
            // No licence, no permission, no reviewer — for anything. Recording
            // an empty proof would be inventing one.
            proof: null,
            allowedEnvironments: FixtureOnly,
            // Nothing lapses, because nothing was granted.
            expiresAt: null,
            rootPath: rootPath,
            files: files ?? [],
            boundExamVersionIds: examVersionIds ?? [],
            boundExamDefinitionIds: examDefinitionIds ?? []);

    private static List<ContentSource> Build()
    {
        var sources = new List<ContentSource>();

        // ── Cambridge IELTS 16–21 ────────────────────────────────────────
        //
        // One entry per book. Each book is a single PDF holding the tests,
        // the questions AND the answer keys — there are no separate key files
        // for any of them — plus loose audio in six mutually incompatible
        // naming conventions. Only the PDF is recorded: it is the paper, and
        // FS0.2's inventory script is what enumerates the audio.
        foreach (var (number, pdf) in new (int, string)[]
        {
            (16, "Cam 16/Cam 16.pdf"),
            (17, "Cam 17/Cambridge Ielts 17.pdf"),
            (18, "Cam 18/0. Cambridge 18 (1).pdf"),
            (19, "Cam 19/Cambridge 19.pdf"),
            (20, "Cam 20/Cambridge IELTS 20 Academic.pdf"),
            (21, "Cam 21/Cambridge IELTS 21.pdf"),
        })
        {
            sources.Add(Fixture(
                $"cambridge-ielts-{number}",
                $"Cambridge IELTS {number}",
                CambridgePublisher,
                $"{CamRoot}/{pdf[..pdf.IndexOf('/')]}",
                [new ContentFileRef($"{CamRoot}/{pdf}", null, null)],
                examDefinitionIds: number == 17
                    ? [
                        new ExamDefinitionId("seed-cam17-test-1"),
                        new ExamDefinitionId("seed-cam17-test-2"),
                        new ExamDefinitionId("seed-cam17-test-3"),
                        new ExamDefinitionId("seed-cam17-test-4"),
                    ]
                    : []));
        }

        // ── VOL 9 "REAL IELTS" — eight Reading/Listening tests ───────────
        //
        // Reading and Listening only; the folder name says so ("Chỉ L và R").
        // No Writing, no Speaking.
        //
        // <b>The filenames are recorded exactly as they are on disk, spelling
        // mistakes included.</b> `KET TEST 2-R.docx` and the directory
        // `KEY - EXPLAINATION` are both misspelled, and Reading test 1 carries
        // a stray space before its hyphen. Tidying any of them here would make
        // the registry describe files that do not exist and report a permanent
        // false "missing".
        //
        // Note the Google Drive export stamp `-20260819T082203Z-1-001` baked
        // into the path. That is why the source id is a hand-chosen slug: a
        // re-export moves the path and must not move the rights record.
        for (var test = 1; test <= 8; test++)
        {
            var readingName = test == 1 ? "TEST 1 -R.docx" : $"TEST {test}-R.docx";
            var readingKey = test == 2 ? "KET TEST 2-R.docx" : $"KEY TEST {test}-R.docx";

            sources.Add(Fixture(
                $"vol9-test-{test}",
                $"VOL 9 REAL IELTS — Test {test} (Reading and Listening)",
                owner: null,
                rootPath: Vol9Root,
                [
                    new ContentFileRef($"{Vol9Root}/READING/{readingName}", null, null),
                    new ContentFileRef(
                        $"{Vol9Root}/READING/KEY - EXPLAINATION/{readingKey}", null, null),
                    new ContentFileRef($"{Vol9Root}/LISTENING/TEST {test}-L.docx", null, null),
                    new ContentFileRef(
                        $"{Vol9Root}/LISTENING/KEY - TRANSCRIPT/KEY TEST {test}-L.docx", null, null),
                    new ContentFileRef($"{Vol9Root}/LISTENING/AUDIO/TEST {test}.mp4", null, null),
                ]));
        }

        // ── Writing and Speaking assessment criteria ─────────────────────
        //
        // The `H-8a` descriptor-source material. Published by the IELTS
        // partners; a public band descriptor is still third-party material and
        // still holds no recorded right to be reproduced inside a product.
        const string Partners = "British Council / IDP / Cambridge (IELTS partners, publisher)";
        const string WritingDir = "Đề IELTS/Tiêu chí chấm Writing";
        const string SpeakingDir = "Đề IELTS/Tiêu chí chấm speaking";

        foreach (var (id, title, dir, file) in new (string, string, string, string)[]
        {
            ("ielts-writing-band-descriptors",
             "IELTS Writing band descriptors", WritingDir,
             "ielts-writing-band-descriptors - Chi tiết các tiêu chí chấm điểm.pdf"),

            ("ielts-writing-key-assessment-criteria",
             "IELTS Writing key assessment criteria", WritingDir,
             "ielts-writing-key-assessment-criteria - tiêu chí chấm điểm.pdf"),

            ("ielts-academic-writing-sample-tasks-2023",
             "IELTS Academic Writing sample tasks (2023)", WritingDir,
             "ielts-academic-writing-sample-tasks-2023 - Bài mẫu và mẫu chấm điểm của giám thị IELTS.pdf"),

            ("ielts-speaking-band-descriptors",
             "IELTS Speaking band descriptors", SpeakingDir,
             "ielts-speaking-band-descriptors - Chi tiết các tiêu chí chấm điểm.pdf"),

            ("ielts-speaking-key-assessment-criteria",
             "IELTS Speaking key assessment criteria", SpeakingDir,
             "ielts-speaking-key-assessment-criteria - Tiêu chí chấm điểm.pdf"),
        })
        {
            sources.Add(Fixture(
                id, title, Partners, dir, [new ContentFileRef($"{dir}/{file}", null, null)]));
        }

        // ── exam/Exam1 ───────────────────────────────────────────────────
        //
        // <b>The clearest case in the whole registry, because it says so
        // itself.</b> Its README: "A fixture, not a deliverable … Do not ship
        // it to a learner" and "This material was not authored by VNI and the
        // right to use it has not been established." It is watermarked "REAL
        // IELTS TESTS" and its transcripts are whisper.cpp machine output.
        //
        // The six hashes are transcribed from `exam/Exam1/manifest.json`
        // (`assets[].sha256`) — the only per-file content hashes that exist in
        // this project. The manifest is inside the gitignored folder, so
        // without this copy they would vanish from a clean checkout along with
        // the material they describe.
        //
        // Bound to `seed-exam-1`, the definition id `DevelopmentExamSeeder`
        // gives `fixtures/exams/exam-1.json`. The seeder derives the *version*
        // id from a content fingerprint, so only the definition id is stable.
        sources.Add(Fixture(
            "exam1",
            "Exam 1 — borrowed third-party paper, fixture only",
            owner: null,
            rootPath: "exam/Exam1",
            [
                new ContentFileRef(
                    "exam/Exam1/assets/audio/listening-part1.mp3",
                    "9f026296e81ef64bbe61b6109d33f9a490725463ea5fc48815f4050ebccc003b", 4779946),
                new ContentFileRef(
                    "exam/Exam1/assets/audio/listening-part2.mp3",
                    "867a6dca9350b1463331d4842f0cbc3276b14d45aeee63ebf50e04b1afe0114d", 5507856),
                new ContentFileRef(
                    "exam/Exam1/assets/audio/listening-part3.mp3",
                    "c37d455047a05fcf7baecc5ffac7ca9a8fe691f1855f23b5d87874a554f0d208", 5156099),
                new ContentFileRef(
                    "exam/Exam1/assets/audio/listening-part4.mp3",
                    "a1eb82e89dab0d0f1031ba056522e581aa0f6fdf95a194218238a8ee24e0b6e8", 5092413),
                new ContentFileRef(
                    "exam/Exam1/assets/images/listening-part2-community-centre-map.jpg",
                    "20e45e2d699287a917d4043b89106c1a2e4707877cb82dae23fc53148854edf2", 13444),
                new ContentFileRef(
                    "exam/Exam1/assets/images/writing-task1-chart.jpg",
                    "b6c5eebc3368d66656e447e82b42d62927fb8b8e80d5d595b01225525933d8fc", 44464),

                // Not hashed: the manifest itself is regenerated by the
                // authoring tools, so a recorded hash would go stale by design.
                new ContentFileRef("exam/Exam1/manifest.json", null, null),
                new ContentFileRef("exam/Exam1/exam.json", null, null),
                new ContentFileRef("exam/Exam1/answer-keys.json", null, null),
                new ContentFileRef("fixtures/exams/exam-1.json", null, null),
            ],
            examDefinitionIds: [new ExamDefinitionId("seed-exam-1")]));

        return sources;
    }
}
