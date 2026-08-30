using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: Vni.Ielts.PackageVerifier <schema.json> <exam.json>");
    return 2;
}

var reader = ExamPackageReader.FromSchemaFile(args[0]);
var result = reader.Read(await File.ReadAllTextAsync(args[1]), ExamDefinitionId.New(), 1);
if (!result.IsValid || result.Version is null)
{
    foreach (var finding in result.Findings)
        Console.Error.WriteLine($"{finding.Code} {finding.Path}: {finding.Message}");
    return 1;
}

var version = result.Version;
var expected = new[] { ExamModule.Reading, ExamModule.Listening }.ToHashSet();
if (!version.Sections.Select(section => section.Module).ToHashSet().SetEquals(expected))
{
    Console.Error.WriteLine("PILOT_MODULE_SCOPE: pilot must contain Reading and Listening only.");
    return 1;
}

foreach (var section in version.Sections.OrderBy(section => section.Order))
{
    var answers = section.Questions.ToDictionary(
        question => question.Id,
        question => (string?)AsClientWouldSend(question.AnswerKey!.Accepted[0]));
    var score = DeterministicScorer.Score(section, version.Scoring, answers);
    Console.WriteLine($"{section.Module}: objects={section.Questions.Count()}, slots={section.AutoScoredMarks}, perfect={score.RawScore}/{score.MaxScore}");
    if (score.RawScore != 40 || score.MaxScore != 40)
        return 1;
}

Console.WriteLine("publication=refused (verification tool never persists or publishes content)");
return 0;

static string AsClientWouldSend(AcceptedAnswer accepted) => accepted switch
{
    { All: { } all } => string.Join('|', all.Order(StringComparer.Ordinal)),
    { Pair: { } pair } => $"{pair.Left}:{pair.Right}",
    { Single: { } single } => single,
    _ => throw new InvalidOperationException("Answer key has no supported client shape."),
};
