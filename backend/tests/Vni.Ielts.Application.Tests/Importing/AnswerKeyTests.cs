using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

/// <summary>
/// Reading a supplier's answer key, and writing it onto a paper.
///
/// ── Measured, not imagined ────────────────────────────────────────────────
///
/// Every case below came from a real run against VOL 9 on 2026-09-02/03:
///
/// <list type="bullet">
/// <item>Given the paper alone, a model invented forty answers, five wrong.</item>
/// <item>Given the paper and the key together, it still put FALSE on question
/// 13 and A on question 36 — right answers, wrong questions. The key for that
/// paper prints no numbers, so alignment is positional, and one line covering
/// "24-26" shifts everything after it.</item>
/// <item>Read by a counter instead, 34 of 38 came back exactly right and the
/// remaining four were <b>refused</b> rather than guessed.</item>
/// </list>
///
/// The last line is the property worth protecting. An answer key is the one
/// artefact where being nearly right is worthless: it marks every learner who
/// ever sits the paper, and nothing about a wrong answer looks wrong.
/// </summary>
public sealed class AnswerKeyTests
{
    /// <summary>
    /// VOL 9 Test 2 Reading, as `KET TEST 2-R.docx` prints it: three passage
    /// headings in a block at the top, then forty marks in order with one line
    /// covering a range.
    /// </summary>
    private const string BareList = """
        VOL 9 TEST 2 KEY
        PASSAGE 1
        PASSAGE 2
        PASSAGE 3
        father
        music
        TRUE
        NOT GIVEN
        E
        24-26. A, B, D
        D wood
        YES
        VOL 9 TEST 2 EXPLANATION
        """;

    /// <summary>VOL 9 Test 1, which numbers every entry.</summary>
    private const string Numbered = """
        PASSAGE 1
        Câu số 1:
        FALSE
        ...
        Câu số 2:
        NOT GIVEN
        ...
        Câu số 21-22:
        A, C
        ...
        """;

    [Fact]
    public void A_bare_list_is_numbered_by_position()
    {
        var entries = AnswerKeyDocument.Parse(BareList);

        Assert.Equal(
            [(1, 1, "father"), (2, 2, "music"), (3, 3, "TRUE"), (4, 4, "NOT GIVEN"), (5, 5, "E")],
            entries.Take(5).Select(e => (e.First, e.Last, e.Raw)));
    }

    /// <summary>
    /// VOL 9 Listening keys print the transcript after the forty answers. In a
    /// bare list the forty-first line is not question 41.
    /// </summary>
    [Fact]
    public void A_bare_list_stops_at_forty_and_ignores_the_transcript_after_it()
    {
        var forty = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"answer{i}"));
        var entries = AnswerKeyDocument.Parse(
            forty + "\nSection 1\n(0:01 - 0:19) You will hear a number of different recordings\nmore prose\n");

        Assert.Equal(40, entries.Count);
        Assert.Equal(40, entries.Max(e => e.Last));
    }

    /// <summary>
    /// <b>The rule the model got wrong.</b> After a line covering 24–26 the next
    /// answer belongs to question 27, not 25. Advancing by one instead of by the
    /// entry's width is precisely how Q36 ended up holding Q34's answer.
    /// </summary>
    [Fact]
    public void A_range_entry_advances_the_counter_by_its_own_width()
    {
        var entries = AnswerKeyDocument.Parse(BareList);

        var range = Assert.Single(entries, e => e.Last > e.First);
        Assert.Equal((24, 26, 3), (range.First, range.Last, range.Marks));

        var after = entries.SkipWhile(e => e != range).Skip(1).First();
        Assert.Equal(27, after.First);
    }

    [Fact]
    public void Headings_and_the_explanation_footer_are_not_answers()
    {
        var entries = AnswerKeyDocument.Parse(BareList);

        Assert.DoesNotContain(entries, e => e.Raw.Contains("PASSAGE", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.Raw.Contains("EXPLANATION", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.Raw == "...");
    }

    [Fact]
    public void A_numbered_key_takes_its_numbers_from_the_page()
    {
        var entries = AnswerKeyDocument.Parse(Numbered);

        Assert.Equal([(1, 1, "FALSE"), (2, 2, "NOT GIVEN"), (21, 22, "A, C")],
            entries.Select(e => (e.First, e.Last, e.Raw)));
    }

    // ── Injection ─────────────────────────────────────────────────────────

    private static string Paper(string type, string? options = null, int order = 1) => $$"""
        {
          "sections": [{
            "module": "reading",
            "parts": [{
              "questions": [
                { "id": "r-{{order}}", "order": {{order}}, "type": "{{type}}"
                  {{(options is null ? "" : ", \"options\": " + options)}} }
              ]
            }]
          }]
        }
        """;

    private const string Letters = """
        [{"key":"A","text":"one"},{"key":"B","text":"two"},{"key":"C","text":"three"},{"key":"D","text":"four"}]
        """;

    [Fact]
    public void A_true_false_answer_lands_on_a_true_false_question()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("true-false-notgiven"), [new AnswerKeyEntry(1, 1, "TRUE")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains("\"TRUE\"", result.PackageJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The check that turns a silent shift into a loud one.</b> A key that
    /// has slipped out of step lands a word where a verdict belongs, and a
    /// True/False question can only be answered three ways.
    /// </summary>
    /// <summary>
    /// YES on a TRUE/FALSE question is the paper mis-typed, not the key
    /// slipped: the families differ only by rubric, and the key knows which
    /// rubric was printed. Cam 17 Test 3 Q32–35, 2026-09-04.
    /// </summary>
    [Fact]
    public void A_yes_on_a_true_false_question_retypes_it_to_yes_no_and_warns()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("true-false-notgiven"), [new AnswerKeyEntry(1, 1, "YES")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains("\"yes-no-notgiven\"", result.PackageJson, StringComparison.Ordinal);
        Assert.Contains("\"YES\"", result.PackageJson, StringComparison.Ordinal);
        Assert.Contains(result.Findings, f => f.Code == AnswerKeyInjection.TypeRetypedCode && f.Severity == "warning");
    }

    /// <summary>
    /// NOT GIVEN fits both vocabularies, so a question answered NOT GIVEN says
    /// nothing about its own rubric. It follows the siblings the key settled.
    /// </summary>
    [Fact]
    public void A_not_given_sibling_follows_the_group_it_was_retyped_with()
    {
        const string paper = """
            {"sections":[{"module":"reading","parts":[{"questions":[
              {"id":"r-1","order":1,"type":"true-false-notgiven","group":{"id":"r-1-2","instruction":"YES NO NOT GIVEN"}},
              {"id":"r-2","order":2,"type":"true-false-notgiven","group":{"id":"r-1-2","instruction":"YES NO NOT GIVEN"}}
            ]}]}]}
            """;

        var result = AnswerKeyInjection.Apply(
            paper, [new AnswerKeyEntry(1, 1, "YES"), new AnswerKeyEntry(2, 2, "NOT GIVEN")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.DoesNotContain("true-false-notgiven", result.PackageJson, StringComparison.Ordinal);
        Assert.Equal(2, result.Findings.Count(f => f.Code == AnswerKeyInjection.TypeRetypedCode));
    }

    [Theory]
    [InlineData("NOTGIVEN")]
    [InlineData("NOT-GIVEN")]
    [InlineData("not  given")]
    public void Not_given_survives_the_ways_extraction_mangles_it(string asExtracted)
    {
        var result = AnswerKeyInjection.Apply(
            Paper("true-false-notgiven"), [new AnswerKeyEntry(1, 1, asExtracted)]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains("\"NOT GIVEN\"", result.PackageJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// tesseract on a Cambridge key page (Cam 18 T1): a lone capital arrives
    /// doubled or with a glyph glued on. One printed key among the letters is
    /// the answer; two different printed keys is left refused.
    /// </summary>
    [Theory]
    [InlineData("Cc", "C")]
    [InlineData("8B", "B")]
    [InlineData("OD", "D")]
    [InlineData("=F", "F")]
    public void An_ocr_smudged_letter_resolves_to_the_one_printed_key_it_contains(string smudged, string key)
    {
        const string bank = """
            [{"key":"A","text":"a"},{"key":"B","text":"b"},{"key":"C","text":"c"},{"key":"D","text":"d"},{"key":"F","text":"f"}]
            """;
        var result = AnswerKeyInjection.Apply(
            Paper("matching", bank), [new AnswerKeyEntry(1, 1, smudged)]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains($"\"{key}\"", result.PackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public void A_roman_numeral_with_a_glued_glyph_resolves_to_the_printed_heading()
    {
        const string bank = """
            [{"key":"i","text":"one"},{"key":"vi","text":"six"},{"key":"vii","text":"seven"}]
            """;
        var result = AnswerKeyInjection.Apply(Paper("matching", bank), [new AnswerKeyEntry(1, 1, "Ovi")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains("\"vi\"", result.PackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_different_printed_keys_in_one_token_stay_refused()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("matching", Letters), [new AnswerKeyEntry(1, 1, "AB")]);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Findings, f => f.Code == AnswerKeyInjection.TypeMismatchCode);
    }

    /// <summary>VOL 9 T2 Q15: rubric says A–H, the model listed A–F, the key says H.</summary>
    [Fact]
    public void A_letter_inside_the_rubric_range_but_missing_from_the_options_is_added()
    {
        const string paper = """
            {"sections":[{"module":"reading","parts":[{"questions":[
              {"id":"r-15","order":15,"type":"matching",
               "options":[{"key":"A","text":"A"},{"key":"B","text":"B"},{"key":"C","text":"C"}],
               "group":{"id":"r-m14-15","instruction":"Choose the correct letter, A-H, in boxes 14-15."}}
            ]}]}]}
            """;

        var result = AnswerKeyInjection.Apply(paper, [new AnswerKeyEntry(15, 15, "H")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains(result.Findings, f => f.Code == AnswerKeyInjection.OptionAddedCode);
        Assert.Contains("{\"key\":\"H\",\"text\":\"H\"}", result.PackageJson.Replace(" ", "").Replace("\n", ""), StringComparison.Ordinal);
    }

    /// <summary>VOL 9 T2 Q27–30: a summary with an A–D word bank left as prose; key "D wood".</summary>
    [Fact]
    public void A_bank_label_on_a_completion_question_accepts_the_label_and_the_word()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("completion"), [new AnswerKeyEntry(1, 1, "D wood")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains(result.Findings, f => f.Code == AnswerKeyInjection.BankLabelAlternativesCode);
        Assert.Contains("\"D\"", result.PackageJson, StringComparison.Ordinal);
        Assert.Contains("\"wood\"", result.PackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_written_answer_keys_are_stripped_and_counted()
    {
        const string paper = """
            {"sections":[{"module":"reading","parts":[{"questions":[
              {"id":"r-1","order":1,"type":"completion","answerKey":{"accepted":["guess"]}},
              {"id":"r-2","order":2,"type":"completion"}
            ]}]}]}
            """;

        var (json, removed) = FabricatedAnswerKeyGuard.Strip(paper);

        Assert.Equal(1, removed);
        Assert.DoesNotContain("answerKey", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_word_on_a_true_false_question_is_refused()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("true-false-notgiven"), [new AnswerKeyEntry(1, 1, "secretary")]);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Findings, f => f.Code == AnswerKeyInjection.TypeMismatchCode);
    }

    [Fact]
    public void A_letter_answer_is_checked_against_the_options_the_question_prints()
    {
        var good = AnswerKeyInjection.Apply(
            Paper("multiple-choice", Letters), [new AnswerKeyEntry(1, 1, "C")]);

        Assert.True(good.IsSuccess, Summarise(good));

        var bad = AnswerKeyInjection.Apply(
            Paper("multiple-choice", Letters), [new AnswerKeyEntry(1, 1, "Z")]);

        Assert.False(bad.IsSuccess);
    }

    /// <summary>
    /// VOL 9 prints "D wood" — the letter with its word beside it. The letter is
    /// the answer when the question offers it.
    /// </summary>
    [Fact]
    public void A_letter_with_a_gloss_reads_as_the_letter()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("matching", Letters), [new AnswerKeyEntry(1, 1, "D wood")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains("\"D\"", result.PackageJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The same line on a question with no bank is not written as the
    /// literal "D wood".</b> VOL 9 Test 2 questions 27–30 are a summary with an
    /// A–D word bank; the transcription put the bank inside the group's prose,
    /// so the question offered no options. Until 2026-09-04 this refused; it now
    /// accepts the label and the word separately (see
    /// <see cref="A_bank_label_on_a_completion_question_accepts_the_label_and_the_word"/>),
    /// and what this test still guards is that the joined string never becomes
    /// the answer and that the paper defect is still reported.
    /// </summary>
    [Fact]
    public void A_bank_label_with_no_bank_never_becomes_a_literal_answer()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("completion"), [new AnswerKeyEntry(1, 1, "D wood")]);

        Assert.DoesNotContain("\"D wood\"", result.PackageJson, StringComparison.Ordinal);

        var finding = Assert.Single(result.Findings, f => f.Code == AnswerKeyInjection.BankLabelAlternativesCode);
        Assert.Contains("bank", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_ordinary_completion_answer_is_taken_verbatim()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("completion"), [new AnswerKeyEntry(1, 1, "semantic equivalence")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains("\"semantic equivalence\"", result.PackageJson, StringComparison.Ordinal);
    }

    /// <summary>A printed alternative is two spellings of one answer, not two answers.</summary>
    [Fact]
    public void A_slash_separates_accepted_spellings()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("completion"), [new AnswerKeyEntry(1, 1, "flavour / flavor")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains("\"flavour\"", result.PackageJson, StringComparison.Ordinal);
        Assert.Contains("\"flavor\"", result.PackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_choice_questions_under_a_choose_two_key_fold_into_one_multiple_select()
    {
        const string paper = """
            {
              "sections": [{
                "module": "reading",
                "parts": [{
                  "questions": [
                    {
                      "id": "r-23", "order": 23, "type": "multiple-choice",
                      "options": [{"key":"A","text":"a"},{"key":"B","text":"b"},{"key":"C","text":"c"},{"key":"D","text":"d"},{"key":"E","text":"e"}],
                      "group": { "id": "r-two-23-24", "instruction": "Choose TWO letters, A-E." }
                    },
                    {
                      "id": "r-24", "order": 24, "type": "multiple-choice",
                      "options": [{"key":"A","text":"a"},{"key":"B","text":"b"},{"key":"C","text":"c"},{"key":"D","text":"d"},{"key":"E","text":"e"}],
                      "group": { "id": "r-two-23-24", "instruction": "Choose TWO letters, A-E." }
                    }
                  ]
                }]
              }]
            }
            """;

        var result = AnswerKeyInjection.Apply(paper, [new AnswerKeyEntry(23, 24, "C, D")]);

        Assert.True(result.IsSuccess, Summarise(result));
        Assert.Contains(result.Findings, f => f.Code == AnswerKeyInjection.FoldedChoiceCode);
        Assert.DoesNotContain("\"r-24\"", result.PackageJson, StringComparison.Ordinal);
        Assert.Contains("\"multiple-select\"", result.PackageJson, StringComparison.Ordinal);
        Assert.Contains("\"C\"", result.PackageJson, StringComparison.Ordinal);
        Assert.Contains("\"D\"", result.PackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public void A_question_the_key_does_not_answer_is_refused()
    {
        var result = AnswerKeyInjection.Apply(Paper("completion"), []);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Findings, f => f.Code == AnswerKeyInjection.CoverageCode);
    }

    [Fact]
    public void An_answer_for_a_question_the_paper_lacks_is_refused()
    {
        var result = AnswerKeyInjection.Apply(
            Paper("completion"),
            [new AnswerKeyEntry(1, 1, "ok"), new AnswerKeyEntry(2, 2, "stray")]);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Findings, f => f.Message.Contains("which the paper does not contain"));
    }

    /// <summary>
    /// A group id with a space in it (Cam 21 T1: <c>"r-y nng-37-40"</c>) is
    /// folded into an identifier, the same way for every member, so the group
    /// survives and the schema does not fail the whole paper over a label.
    /// </summary>
    [Fact]
    public void A_group_id_that_is_not_an_identifier_is_folded_consistently()
    {
        const string paper = """
            {"sections":[{"module":"reading","parts":[{"questions":[
              {"id":"r-37","order":37,"type":"yes-no-notgiven","group":{"id":"r-y nng-37-40"}},
              {"id":"r-38","order":38,"type":"yes-no-notgiven","group":{"id":"r-y nng-37-40"}}
            ]}]}]}
            """;

        var result = QuestionGroupRepair.SplitDivergentBanks(paper);

        Assert.Single(result.Findings, f => f.Code == QuestionGroupRepair.IdNormalisedCode);
        Assert.DoesNotContain("r-y nng-37-40", result.PackageJson, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(result.PackageJson, "\"r-y-nng-37-40\"").Count);
    }

    /// <summary>
    /// Cam 21 T3: the model filed Writing Task 1 and 2 as parts 4–5 of the
    /// Reading section, numbered 1 and 2. They are dropped, the passages stay.
    /// </summary>
    [Fact]
    public void A_writing_task_filed_inside_reading_is_dropped_and_the_passages_stay()
    {
        const string paper = """
            {"sections":[{"module":"reading","parts":[
              {"order":1,"kind":"passage","questions":[{"id":"r-1","order":1,"type":"completion"}]},
              {"order":2,"kind":"task","questions":[{"id":"w-1","order":1,"type":"essay-task"}]}
            ]}]}
            """;

        var result = SectionPartRepair.DropForeignParts(paper);

        Assert.Single(result.Findings, f => f.Code == SectionPartRepair.DroppedCode);
        Assert.Contains("\"passage\"", result.PackageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("essay-task", result.PackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Independent_multiple_choice_questions_that_share_a_group_id_are_split()
    {
        const string paper = """
            {
              "sections": [{
                "module": "reading",
                "parts": [{
                  "questions": [
                    {
                      "id": "r-36", "order": 36, "type": "multiple-choice",
                      "options": [{"key":"A","text":"one"},{"key":"B","text":"two"}],
                      "group": { "id": "r-mc-36-40" }
                    },
                    {
                      "id": "r-37", "order": 37, "type": "multiple-choice",
                      "options": [{"key":"A","text":"alpha"},{"key":"B","text":"beta"}],
                      "group": { "id": "r-mc-36-40" }
                    }
                  ]
                }]
              }]
            }
            """;

        var result = QuestionGroupRepair.SplitDivergentBanks(paper);

        Assert.Contains(result.Findings, f => f.Code == QuestionGroupRepair.SplitCode);
        Assert.Contains("r-mc-36-40-36", result.PackageJson, StringComparison.Ordinal);
        Assert.Contains("r-mc-36-40-37", result.PackageJson, StringComparison.Ordinal);
    }

    private static string Summarise(AnswerKeyInjection.Result result) =>
        string.Join(" | ", result.Findings.Select(f => $"{f.Severity} {f.Code}: {f.Message}"));
}
