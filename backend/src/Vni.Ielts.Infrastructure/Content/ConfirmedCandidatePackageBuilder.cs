using System.Text.Json;
using System.Text.Json.Nodes;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

public sealed class ConfirmedCandidatePackageBuilder : IConfirmedCandidatePackageBuilder
{
    public string BuildCanonicalJson(
        ParsedExamCandidate candidate,
        CandidateCompletionData completion,
        ExamDefinitionId definitionId,
        int versionNumber)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(completion);

        var root = new JsonObject
        {
            ["formatVersion"] = "1.0",
            ["title"] = string.IsNullOrWhiteSpace(candidate.Title) ? "Untitled Exam" : candidate.Title.Trim(),
            ["variant"] = completion.Variant == ExamVariant.General ? "general" : "academic",
            ["timingProfile"] = BuildTimingProfile(completion.TimingProfile),
            ["scoringProfile"] = BuildScoringProfile(completion.ScoringProfile),
            ["sections"] = BuildSections(candidate, completion),
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject BuildTimingProfile(CandidateTimingProfile timing)
    {
        var sectionsObj = new JsonObject();

        foreach (var (module, sectionTiming) in timing.Sections)
        {
            var moduleKey = ToWireModule(module);
            if (module == ExamModule.Speaking && timing.SpeakingParts is { Count: > 0 })
            {
                var partsArr = new JsonArray();
                foreach (var sp in timing.SpeakingParts)
                {
                    partsArr.Add(new JsonObject
                    {
                        ["part"] = sp.Part,
                        ["prepSeconds"] = sp.PrepSeconds,
                        ["responseSeconds"] = sp.ResponseSeconds,
                    });
                }
                sectionsObj[moduleKey] = new JsonObject { ["parts"] = partsArr };
            }
            else
            {
                var secObj = new JsonObject
                {
                    ["durationSeconds"] = sectionTiming.DurationSeconds,
                };
                if (module == ExamModule.Listening && sectionTiming.TransferTimeSeconds.HasValue)
                {
                    secObj["transferTimeSeconds"] = sectionTiming.TransferTimeSeconds.Value;
                }
                sectionsObj[moduleKey] = secObj;
            }
        }

        return new JsonObject { ["sections"] = sectionsObj };
    }

    private static JsonObject BuildScoringProfile(CandidateScoringProfile scoring)
    {
        var scoringObj = new JsonObject();

        if (scoring.RawToBand is not null && scoring.RawToBand.Count > 0)
        {
            var rawToBandObj = new JsonObject();
            foreach (var (module, boundaries) in scoring.RawToBand)
            {
                var moduleKey = ToWireModule(module);
                var boundariesArr = new JsonArray();
                foreach (var b in boundaries.OrderBy(b => b.MinRaw))
                {
                    boundariesArr.Add(new JsonObject
                    {
                        ["minRaw"] = b.MinRaw,
                        ["band"] = b.Band,
                    });
                }
                rawToBandObj[moduleKey] = boundariesArr;
            }
            scoringObj["rawToBand"] = rawToBandObj;
        }
        else
        {
            // Empty rawToBand container to let schema / reader validate
            scoringObj["rawToBand"] = new JsonObject();
        }

        if (!string.IsNullOrWhiteSpace(scoring.ScoringProfileRef))
        {
            scoringObj["scoringProfileRef"] = scoring.ScoringProfileRef.Trim();
        }

        if (scoring.CriterionWeights is not null)
        {
            scoringObj["criterionWeights"] = new JsonObject
            {
                ["writing"] = new JsonObject
                {
                    ["task1"] = scoring.CriterionWeights.Task1,
                    ["task2"] = scoring.CriterionWeights.Task2,
                },
            };
        }

        return scoringObj;
    }

    private static JsonArray BuildSections(
        ParsedExamCandidate candidate,
        CandidateCompletionData completion)
    {
        var sectionsArr = new JsonArray();
        var partCompletions = completion.PartDetails?.ToDictionary(p => p.PartOrder)
                              ?? new Dictionary<int, CandidatePartCompletion>();

        var sectionOrder = 1;
        foreach (var mod in candidate.Modules)
        {
            var moduleEnum = mod.Module ?? ResolveModule(mod.Classification);
            var moduleKey = ToWireModule(moduleEnum);

            var partsArr = new JsonArray();
            var partOrder = 1;
            foreach (var part in mod.Parts)
            {
                var order = part.Order > 0 ? part.Order : partOrder;
                partCompletions.TryGetValue(order, out var partMeta);

                var kind = partMeta?.Kind
                           ?? (moduleEnum switch
                           {
                               ExamModule.Reading => "passage",
                               ExamModule.Listening => "recording",
                               ExamModule.Writing => "task",
                               ExamModule.Speaking => "speaking-part",
                               _ => "passage",
                           });

                var partObj = new JsonObject
                {
                    ["order"] = order,
                    ["kind"] = kind,
                };

                if (!string.IsNullOrWhiteSpace(part.Title))
                    partObj["title"] = part.Title.Trim();

                if (!string.IsNullOrWhiteSpace(part.Body))
                    partObj["body"] = part.Body.Trim();

                if (partMeta?.TaskNumber.HasValue == true)
                    partObj["taskNumber"] = partMeta.TaskNumber.Value;
                else if (moduleEnum == ExamModule.Writing && order is >= 1 and <= 2)
                    partObj["taskNumber"] = order;

                if (partMeta?.PartNumber.HasValue == true)
                    partObj["partNumber"] = partMeta.PartNumber.Value;
                else if (moduleEnum == ExamModule.Speaking && order is >= 1 and <= 3)
                    partObj["partNumber"] = order;

                if (!string.IsNullOrWhiteSpace(partMeta?.AudioAssetRef))
                    partObj["audio"] = partMeta.AudioAssetRef.Trim();

                if (!string.IsNullOrWhiteSpace(partMeta?.ImageAssetRef))
                    partObj["image"] = partMeta.ImageAssetRef.Trim();

                var questionsArr = new JsonArray();
                var questionOrder = 1;
                foreach (var q in part.Questions)
                {
                    var qOrder = q.Order > 0 ? q.Order : questionOrder;
                    var qType = q.Type ?? QuestionType.ShortAnswer;
                    var qTypeKey = ToKebabQuestionType(qType);

                    var qObj = new JsonObject
                    {
                        ["id"] = string.IsNullOrWhiteSpace(q.Id) ? $"q-{order}-{qOrder}" : q.Id.Trim().ToLowerInvariant(),
                        ["order"] = qOrder,
                        ["type"] = qTypeKey,
                    };

                    if (!string.IsNullOrWhiteSpace(q.Prompt))
                        qObj["prompt"] = q.Prompt.Trim();

                    if (q.Options.Count > 0)
                    {
                        var optsArr = new JsonArray();
                        foreach (var opt in q.Options)
                        {
                            optsArr.Add(new JsonObject
                            {
                                ["key"] = opt.Key.Trim(),
                                ["text"] = string.IsNullOrWhiteSpace(opt.Text) ? opt.Key.Trim() : opt.Text.Trim(),
                            });
                        }
                        qObj["options"] = optsArr;
                    }

                    if (q.Type == QuestionType.MultipleSelect && q.AnswerKey?.Accepted is { Count: > 1 })
                    {
                        qObj["marks"] = q.AnswerKey.Accepted.Count;
                    }

                    if (q.AnswerKey is { Accepted.Count: > 0 })
                    {
                        var acceptedArr = new JsonArray();
                        if (q.Type == QuestionType.MultipleSelect)
                        {
                            var inner = new JsonArray();
                            foreach (var acc in q.AnswerKey.Accepted)
                                inner.Add(acc.Trim());
                            acceptedArr.Add(inner);
                        }
                        else
                        {
                            foreach (var acc in q.AnswerKey.Accepted)
                                acceptedArr.Add(acc.Trim());
                        }

                        qObj["answerKey"] = new JsonObject
                        {
                            ["accepted"] = acceptedArr,
                        };
                    }

                    questionsArr.Add(qObj);
                    questionOrder++;
                }

                partObj["questions"] = questionsArr;
                partsArr.Add(partObj);
                partOrder++;
            }

            sectionsArr.Add(new JsonObject
            {
                ["module"] = moduleKey,
                ["order"] = sectionOrder++,
                ["parts"] = partsArr,
            });
        }

        return sectionsArr;
    }

    private static ExamModule ResolveModule(ParsedExamClassification classification) => classification switch
    {
        ParsedExamClassification.Reading => ExamModule.Reading,
        ParsedExamClassification.Listening => ExamModule.Listening,
        ParsedExamClassification.Writing => ExamModule.Writing,
        ParsedExamClassification.Speaking => ExamModule.Speaking,
        _ => ExamModule.Reading,
    };

    private static string ToWireModule(ExamModule module) => module switch
    {
        ExamModule.Reading => "reading",
        ExamModule.Listening => "listening",
        ExamModule.Writing => "writing",
        ExamModule.Speaking => "speaking",
        _ => module.ToString().ToLowerInvariant(),
    };

    private static string ToKebabQuestionType(QuestionType type) => type switch
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
        _ => type.ToString().ToLowerInvariant(),
    };
}
