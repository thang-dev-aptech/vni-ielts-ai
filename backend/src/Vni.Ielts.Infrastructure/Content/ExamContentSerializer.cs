using System.Text.Json;
using System.Text.Json.Nodes;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Turns a stored <see cref="ExamVersion"/> back into an <c>exam.schema.json</c>
/// document the CMS builder can PUT.
///
/// <b>Not a second write model.</b> The authoring workspace loads this, edits
/// it, and posts it to the same <see cref="ExamPackageReader"/> gate the ZIP
/// importer uses. A GET that returned the preview DTO would drop timing,
/// scoring, groups, slots and marks, and the next save would invent them.
/// </summary>
public static class ExamContentSerializer
{
    public static string ToJson(ExamVersion version) =>
        ToNode(version).ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    public static JsonObject ToNode(ExamVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var root = new JsonObject
        {
            ["formatVersion"] = "1.0",
            ["title"] = version.Title,
            ["variant"] = version.Variant.ToString().ToLowerInvariant(),
            ["timingProfile"] = Timing(version),
            ["scoringProfile"] = Scoring(version.Scoring),
            ["sections"] = Sections(version.Sections),
        };

        if (!string.IsNullOrWhiteSpace(version.Description))
            root["description"] = version.Description;


        var present = version.Sections.Select(s => s.Module).ToHashSet();
        var resolved = SequenceProfile.Resolve(null, present);
        if (present.Count > 0 && !version.ModuleSequence.SequenceEqual(resolved))
        {
            root["sequenceProfile"] = new JsonObject
            {
                ["modules"] = new JsonArray(
                    [.. version.ModuleSequence.Select(m => JsonValue.Create(Module(m)))]),
            };
        }

        return root;
    }

    private static JsonObject Timing(ExamVersion version)
    {
        var sections = new JsonObject();
        foreach (var (module, seconds) in version.Timing.SectionDurationSeconds)
        {
            if (module == ExamModule.Speaking) continue;
            var cfg = new JsonObject { ["durationSeconds"] = seconds };
            if (module == ExamModule.Listening && version.Timing.ListeningTransferSeconds is { } transfer)
                cfg["transferTimeSeconds"] = transfer;
            sections[Module(module)] = cfg;
        }

        if (version.Timing.SpeakingParts.Count > 0)
        {
            sections["speaking"] = new JsonObject
            {
                ["parts"] = new JsonArray(
                [
                    .. version.Timing.SpeakingParts.Select(p => new JsonObject
                    {
                        ["part"] = p.Part,
                        ["prepSeconds"] = p.PrepSeconds,
                        ["responseSeconds"] = p.ResponseSeconds,
                    }),
                ]),
            };
        }

        return new JsonObject { ["sections"] = sections };
    }

    private static JsonObject Scoring(ScoringProfile scoring)
    {
        var rawToBand = new JsonObject();
        foreach (var (module, table) in scoring.RawToBand)
        {
            rawToBand[Module(module)] = new JsonArray(
            [
                .. table.Select(b => new JsonObject
                {
                    ["minRaw"] = b.MinRaw,
                    ["band"] = b.Band.Value,
                }),
            ]);
        }

        var node = new JsonObject { ["rawToBand"] = rawToBand };

        if (scoring.WritingTask1Weight is { } t1 && scoring.WritingTask2Weight is { } t2)
        {
            node["criterionWeights"] = new JsonObject
            {
                ["writing"] = new JsonObject
                {
                    ["task1"] = t1,
                    ["task2"] = t2,
                },
            };
        }

        return node;
    }

    private static JsonArray Sections(IReadOnlyList<Section> sections)
    {
        var array = new JsonArray();
        foreach (var section in sections.OrderBy(s => s.Order))
        {
            array.Add(new JsonObject
            {
                ["module"] = Module(section.Module),
                ["order"] = section.Order,
                ["parts"] = Parts(section.Parts),
            });
        }

        return array;
    }

    private static JsonArray Parts(IReadOnlyList<SectionPart> parts)
    {
        var array = new JsonArray();
        foreach (var part in parts.OrderBy(p => p.Order))
        {
            var node = new JsonObject
            {
                ["order"] = part.Order,
                ["kind"] = part.Kind,
            };
            if (!string.IsNullOrWhiteSpace(part.Title)) node["title"] = part.Title;
            if (!string.IsNullOrWhiteSpace(part.Body)) node["body"] = part.Body;
            if (!string.IsNullOrWhiteSpace(part.AudioKey)) node["audio"] = part.AudioKey;
            if (!string.IsNullOrWhiteSpace(part.ImageKey)) node["image"] = part.ImageKey;
            if (!string.IsNullOrWhiteSpace(part.Transcript)) node["transcript"] = part.Transcript;
            if (part.TaskNumber is { } task) node["taskNumber"] = task;
            if (part.PartNumber is { } partNumber) node["partNumber"] = partNumber;
            if (part.CueCard is { } cue)
            {
                node["cueCard"] = new JsonObject
                {
                    ["topic"] = cue.Topic,
                    ["bullets"] = new JsonArray([.. cue.Bullets.Select(b => JsonValue.Create(b))]),
                };
            }

            if (part.MinWords is { } min)
                node["constraints"] = new JsonObject { ["minWords"] = min };

            if (part.Timing is { } timing)
            {
                var t = new JsonObject { ["durationSeconds"] = timing.DurationSeconds };
                if (timing.PrepSeconds is { } prep) t["prepSeconds"] = prep;
                if (timing.ResponseSeconds is { } response) t["responseSeconds"] = response;
                node["timing"] = t;
            }

            node["questions"] = Questions(part.Questions);
            array.Add(node);
        }

        return array;
    }

    private static JsonArray Questions(IReadOnlyList<Question> questions)
    {
        var array = new JsonArray();
        foreach (var question in questions.OrderBy(q => q.Order))
        {
            var node = new JsonObject
            {
                ["id"] = question.Id,
                ["order"] = question.Order,
                ["type"] = Type(question.Type),
            };
            if (!string.IsNullOrWhiteSpace(question.Prompt)) node["prompt"] = question.Prompt;
            if (question.Marks != 1) node["marks"] = question.Marks;
            if (question.MaxWords is { } max)
                node["constraints"] = new JsonObject { ["maxWords"] = max };

            if (question.Options.Count > 0)
            {
                node["options"] = new JsonArray(
                [
                    .. question.Options.Select(o => new JsonObject
                    {
                        ["key"] = o.Key,
                        ["text"] = o.Text,
                    }),
                ]);
            }

            if (question.Group is { } group) node["group"] = Group(group);
            if (question.AnswerKey is { } key) node["answerKey"] = AnswerKey(key);
            else if (question.Slots is { Count: > 0 } slots)
                node["slots"] = Slots(slots);
            if (question.Explanation is { } explanation) node["explanation"] = Explanation(explanation);

            array.Add(node);
        }

        return array;
    }

    private static JsonNode Explanation(QuestionExplanation explanation)
    {
        if (explanation.CorrectAnswer is null
            && explanation.Evidence.Count == 0
            && explanation.CommonMistake is null)
        {
            return JsonValue.Create(explanation.ShortReason)!;
        }

        var node = new JsonObject { ["shortReason"] = explanation.ShortReason };
        if (!string.IsNullOrWhiteSpace(explanation.CorrectAnswer))
            node["correctAnswer"] = explanation.CorrectAnswer;
        node["evidence"] = new JsonArray([.. explanation.Evidence.Select(e => JsonValue.Create(e))]);
        if (!string.IsNullOrWhiteSpace(explanation.CommonMistake))
            node["commonMistake"] = explanation.CommonMistake;
        return node;
    }

    private static JsonObject Group(QuestionGroup group)
    {
        var node = new JsonObject { ["id"] = group.Id };
        if (!string.IsNullOrWhiteSpace(group.Title)) node["title"] = group.Title;
        if (!string.IsNullOrWhiteSpace(group.Instruction)) node["instruction"] = group.Instruction;
        if (!string.IsNullOrWhiteSpace(group.Image)) node["image"] = group.Image;
        if (!string.IsNullOrWhiteSpace(group.Text)) node["text"] = group.Text;
        if (group.EachLetterOnce) node["eachLetterOnce"] = true;
        return node;
    }

    private static JsonObject AnswerKey(AnswerKey key)
    {
        var accepted = new JsonArray();
        foreach (var item in key.Accepted)
        {
            if (item.Single is { } single) accepted.Add(single);
            else if (item.All is { } all)
                accepted.Add(new JsonArray([.. all.Select(a => JsonValue.Create(a))]));
            else if (item.Pair is { } pair)
                accepted.Add(new JsonObject { ["left"] = pair.Left, ["right"] = pair.Right });
        }

        return new JsonObject { ["accepted"] = accepted };
    }

    private static JsonArray Slots(IReadOnlyList<ResponseSlot> slots)
    {
        var array = new JsonArray();
        foreach (var slot in slots.OrderBy(s => s.Number))
        {
            var node = new JsonObject
            {
                ["id"] = slot.Id,
                ["number"] = slot.Number,
            };
            if (slot.AnswerKey is { } key) node["answerKey"] = AnswerKey(key);
            array.Add(node);
        }

        return array;
    }

    private static string Module(ExamModule module) => module.ToString().ToLowerInvariant();

    private static string Type(QuestionType type) => type switch
    {
        QuestionType.MultipleChoice => "multiple-choice",
        QuestionType.MultipleSelect => "multiple-select",
        QuestionType.TrueFalseNotGiven => "true-false-notgiven",
        QuestionType.YesNoNotGiven => "yes-no-notgiven",
        QuestionType.Matching => "matching",
        QuestionType.Completion => "completion",
        QuestionType.ShortAnswer => "short-answer",
        QuestionType.Labelling => "labelling",
        QuestionType.EssayTask => "essay-task",
        QuestionType.SpeakingResponse => "speaking-response",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped question type."),
    };
}
