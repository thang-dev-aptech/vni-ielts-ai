using System.IO.Compression;
using System.Text;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content.Import;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// Builds the downloadable exam-package skeleton behind
/// <c>GET /api/v1/admin/import/template</c> (task 7 of the 2026-09-11
/// out-of-band import slice).
///
/// <b>Built from <see cref="ExamPackageArchiveInspector"/>'s own tables, not
/// a copy of them.</b> A checked-in ZIP would drift the first time a folder
/// name is added or renamed there; this one reads
/// <see cref="ExamPackageArchiveInspector.AcceptedSkillFolders"/> and
/// <see cref="ExamPackageArchiveInspector.AcceptedRoleFolders"/> at call
/// time. It does not pre-validate those names and fail loudly on drift —
/// see <see cref="BuildZip"/> for why — so drift is caught by the real
/// inspector, in the integration test that feeds the built ZIP back through
/// it, not by this class.
///
/// <b>Unaccented spellings only.</b> A ZIP stores entry names as CP437 or
/// UTF-8 depending on a per-entry flag many tools set wrongly, so an accented
/// folder name arrives mangled unpredictably — the inspector's own comment on
/// <c>RoleFolders</c> explains why it never matches on one. The skeleton
/// exists so an operator never has to type one.
///
/// <b>Nothing here reads, logs or echoes package content.</b> The builder
/// never opens an uploaded archive; every byte it produces is fixed
/// instruction text baked into this file.
/// </summary>
public static class ImportTemplate
{
    private const string InstructionFileName = "HUONG-DAN.txt";

    /// <summary>
    /// The one spelling this endpoint ships per role. Picked explicitly
    /// rather than read off dictionary enumeration order — insertion order on
    /// a <c>Dictionary</c> is an implementation detail, not a contract — and
    /// checked against <see cref="ExamPackageArchiveInspector.AcceptedRoleFolders"/>
    /// before every build.
    /// </summary>
    private static readonly IReadOnlyDictionary<PackageEntryRole, string> CanonicalRoleFolder =
        new Dictionary<PackageEntryRole, string>
        {
            [PackageEntryRole.Paper] = "de",
            [PackageEntryRole.Key] = "dap-an",
            [PackageEntryRole.Audio] = "audio",
        };

    /// <summary>
    /// Which role folders each skill gets. A domain fact, not something read
    /// off the inspector: only Reading and Listening carry a deterministic
    /// answer key (CLAUDE.md rule 9), and only Listening carries audio.
    /// Writing and Speaking ship a paper folder only — Writing is marked by
    /// AI rather than an answer key, and Speaking is recorded, not marked
    /// (`P-02`).
    /// </summary>
    private static readonly IReadOnlyDictionary<ExamModule, IReadOnlyList<PackageEntryRole>> RolesPerSkill =
        new Dictionary<ExamModule, IReadOnlyList<PackageEntryRole>>
        {
            [ExamModule.Reading] = [PackageEntryRole.Paper, PackageEntryRole.Key],
            [ExamModule.Listening] = [PackageEntryRole.Paper, PackageEntryRole.Key, PackageEntryRole.Audio],
            [ExamModule.Writing] = [PackageEntryRole.Paper],
            [ExamModule.Speaking] = [PackageEntryRole.Paper],
        };

    /// <summary>
    /// Builds the ZIP in memory. Ships whatever <see cref="CanonicalRoleFolder"/>
    /// and <see cref="RolesPerSkill"/> say, without pre-checking them against
    /// <see cref="ExamPackageArchiveInspector"/> — a check here that throws
    /// would turn a drifted name into a 500 instead of the specific,
    /// assertable signal
    /// <c>AdminImportEndpointsTests.The_template_is_a_zip_whose_folders_the_inspector_accepts</c>
    /// is built to catch: the expected path prefix goes missing, and the real
    /// inspector reports <c>LAYOUT_UNKNOWN_ROLE_FOLDER</c> on the produced
    /// ZIP. That round trip through the actual inspector is the proof; this
    /// method stays a plain writer so the proof is not pre-empted.
    /// </summary>
    public static byte[] BuildZip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var module in Enum.GetValues<ExamModule>())
            {
                var skillFolder = SkillFolderNameFor(module);
                foreach (var role in RolesPerSkill[module])
                {
                    var roleFolder = CanonicalRoleFolder[role];
                    var entryName = $"{skillFolder}/{roleFolder}/{InstructionFileName}";
                    WriteTextEntry(archive, entryName, InstructionTextFor(module, role));
                }
            }
        }

        return stream.ToArray();
    }

    private static string SkillFolderNameFor(ExamModule module) =>
        ExamPackageArchiveInspector.AcceptedSkillFolders.First(kv => kv.Value == module).Key;

    private static void WriteTextEntry(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    private static string InstructionTextFor(ExamModule module, PackageEntryRole role) => role switch
    {
        PackageEntryRole.Paper => PaperInstructions(module),
        PackageEntryRole.Key => KeyInstructions(),
        PackageEntryRole.Audio => AudioInstructions(),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    private static string PaperInstructions(ExamModule module)
    {
        var skillLabel = module switch
        {
            ExamModule.Reading => "phần Đọc (Reading)",
            ExamModule.Listening => "phần Nghe (Listening)",
            ExamModule.Writing => "phần Viết (Writing)",
            ExamModule.Speaking => "phần Nói (Speaking)",
            _ => module.ToString(),
        };

        return
            $"""
            Thư mục này chứa ĐỀ BÀI của {skillLabel}: đoạn văn, câu hỏi, đề bài — nội dung
            thí sinh sẽ đọc hoặc làm bài.

            Định dạng tệp được đọc: .docx, .pdf, .txt

            Lưu ý bắt buộc:
            - Tên thư mục và tên tệp không được có dấu (viết không dấu tiếng Việt),
              ví dụ "de", không phải "đề". Một công cụ nén ZIP có thể ghi sai tên có dấu,
              khiến hệ thống không nhận ra đúng vai trò của thư mục.
            - Xoá tệp hướng dẫn này trước khi nộp gói đề thật; đây chỉ là khung mẫu.
            """;
    }

    private static string KeyInstructions() =>
        """
        Thư mục này chứa ĐÁP ÁN (answer key) do nhà cung cấp đề đưa ra.

        Định dạng tệp được đọc: .docx, .pdf, .txt

        Lưu ý bắt buộc — quan trọng:
        - Tên thư mục không được có dấu, ví dụ "dap-an", không phải "đáp án".
          Nếu tên thư mục sai chính tả hoặc có dấu, hệ thống sẽ không nhận ra
          đây là đáp án và sẽ xử lý các tệp trong đó như thể chúng là ĐỀ BÀI —
          nghĩa là đáp án sẽ bị gửi cho mô hình AI. Đây là lỗi nghiêm trọng
          nhất có thể xảy ra khi đặt sai tên thư mục.
        - Đáp án chỉ được đọc bằng mã nguồn của hệ thống, không bao giờ được
          gửi cho mô hình AI.
        - Xoá tệp hướng dẫn này trước khi nộp gói đề thật; đây chỉ là khung mẫu.
        """;

    private static string AudioInstructions() =>
        """
        Thư mục này chứa các tệp ÂM THANH (audio) của phần Nghe (Listening).

        Định dạng tệp được đọc: .mp3

        Lưu ý bắt buộc:
        - Tên thư mục không được có dấu, ví dụ "audio".
        - Xoá tệp hướng dẫn này trước khi nộp gói đề thật; đây chỉ là khung mẫu.
        """;
}
