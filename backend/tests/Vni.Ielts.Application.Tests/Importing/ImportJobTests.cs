using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class ImportJobTests
{
    /// <summary>
    /// Re-uploading the same bytes for the same version must not buy a second
    /// parse. The id is derived from what the work IS, never generated, so a
    /// retried upload collides with the job already running.
    /// </summary>
    [Fact]
    public void The_operation_id_is_derived_from_the_work_not_generated()
    {
        var a = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123");
        var b = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123");

        Assert.Equal(a, b);
    }

    [Fact]
    public void A_different_upload_for_the_same_version_is_a_different_job()
    {
        var a = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123");
        var b = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "def456");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// A stage is how far the money went. Restarting a job that already paid
    /// for a parse must not pay again, so the stage has to be ordered and
    /// recorded, not inferred from what happens to be on the draft.
    /// </summary>
    [Fact]
    public void Stages_advance_in_one_direction()
    {
        Assert.True(ImportJobStage.Extracting < ImportJobStage.Parsing);
        Assert.True(ImportJobStage.Parsing < ImportJobStage.Transcribing);
        Assert.True(ImportJobStage.Transcribing < ImportJobStage.Keying);
        Assert.True(ImportJobStage.Keying < ImportJobStage.Checking);
        Assert.True(ImportJobStage.Checking < ImportJobStage.Explaining);
        Assert.True(ImportJobStage.Explaining < ImportJobStage.Done);
    }

    [Fact]
    public void A_job_out_of_attempts_is_failed_rather_than_retried_forever()
    {
        var job = ImportJob.New(
            new ExamDefinitionId("cam-16"), 1, "abc123", "uploads/abc123.zip", DateTimeOffset.UtcNow);

        var exhausted = job with { Attempts = ImportJob.MaxAttempts };

        Assert.False(exhausted.MayRetry);
    }
}
