# Question Interactions

How each `QuestionType` is answered, where its answer bank comes from, what the stored
answer value looks like, and whether [`AnswerMatcher`](../../backend/src/Vni.Ielts.Domain/Exams/AnswerMatcher.cs)
already marks it.

The taxonomy of types is frozen at ten and lives in
[`ExamContent.cs`](../../backend/src/Vni.Ielts.Domain/Exams/ExamContent.cs); the wire spellings
below are the ones in `exam.schema.json` and `QuestionType.ToWire`, and there is deliberately only
one vocabulary across authoring, storage and the client.

> **Verified against content, not against comments.** Every count and every claim about where a
> bank lives was read out of `fixtures/exams/exam-1.json` — the file the development seeder
> actually loads — on 2026-08-27. Where the code's own comments disagree with the content, that is
> called out rather than smoothed over.

---

## The one thing to know first: a bank is `Question.Options`, never `QuestionGroup.Text`

This is the most likely thing for a reader to get backwards, because `QuestionGroup` sounds like
the place a shared bank would live, and it carries a `Text` field that sounds like it could hold
one.

It does not.

| Field | What it actually holds |
|---|---|
| `Question.Options` | **The bank.** Repeated, identically, on every member of the group |
| `QuestionGroup.Text` | A **summary-completion body** carrying `[n]` markers where question *n*'s gap falls. A frame to type into, not a list to choose from |
| `QuestionGroup.Image` | A map or diagram the whole group refers to |
| `QuestionGroup.Instruction` | The rubric, verbatim — itself a scoring rule (`NO MORE THAN THREE WORDS`) |

The schema states this directly for the two bank-bearing types: *"Matching and labelling are
answered by picking from a shared bank, so each carries that bank. Without options the renderer
has nothing to offer and falls back to a free text box."*

**There is no `QuestionGroup.Options` field.** A group does not own its bank — the bank is
*reconstructed at render time* by checking that every member offers the same options array. That
is a real modelling property with consequences, and it is set out in
[§ Can the content model express a bank today?](#can-the-content-model-express-a-bank-today).

---

## The interaction table

`ELO` = whether `QuestionGroup.EachLetterOnce` is meaningful for the type. **Meaningful is not the
same as enforced** — see [§ EachLetterOnce is enforced nowhere](#eachletteronce-is-enforced-nowhere).

Every stored answer is a **string**, because the answer sheet is `IReadOnlyDictionary<string, string?>`.
There is no richer value type and none is needed.

| Type | Interaction | Bank source | ELO | Stored answer value | `AnswerMatcher` |
|---|---|---|---|---|---|
| `multiple-choice` | Pick one | `Question.Options`, **per question** — not shared | n/a | Option key — `"A"` | `Single` ✅ |
| `multiple-select` | Pick many | `Question.Options`, **per question** | n/a | Keys joined — `"A\|D"` | `All` ✅ |
| `true-false-notgiven` | Pick one of three | **None in content.** The client supplies the triad | n/a | `"TRUE"` · `"FALSE"` · `"NOT GIVEN"` | `Single` ✅ |
| `yes-no-notgiven` | Pick one of three | **None in content.** The client supplies the triad | n/a | `"YES"` · `"NO"` · `"NOT GIVEN"` | `Single` ✅ — **unexercised** |
| `matching` | **Drag from bank** / pick one | `Question.Options`, repeated across the group | ✅ **applies** | Option key — `"i"` | `Single` ✅ · `Pair` reachable, unused |
| `completion` | Type a word into a gap | None | n/a | Free text — `"10,500 years ago"` | `Single` ✅ + word limit |
| `short-answer` | Type a word or three | None | n/a | Free text | `Single` ✅ + word limit |
| `labelling` | **Drag letter onto a map** / pick one | `Question.Options` + `QuestionGroup.Image` | ✅ **applies** | Option key — `"C"` | `Single` ✅ |
| `essay-task` | Long-form typing | None | n/a | The essay text | **N/A** — not auto-scored |
| `speaking-response` | Record audio | None | n/a | A recording id **the server wrote** | **N/A** — not auto-scored |

Two rows deserve their reasoning rather than a cell.

**`true-false-notgiven` and `yes-no-notgiven` carry no options in the package, and that is correct.**
The three responses are what the question type *is*, not optionality an author chose, so the client
supplies them when a package omits them. Anything with real optionality comes from content. Note the
stored value is the **word**, not a letter: the fixture's accepted values are `TRUE` / `FALSE` /
`NOT GIVEN`.

**`speaking-response` is the one type whose stored value the learner cannot write.** The sheet holds
a recording id the server generated, and `SaveAnswers` refuses Speaking outright. An id a client can
choose is an id it can borrow from another sitting.

---

## Two bank flavours that need different rendering

Both `matching` and `labelling` carry a bank in the same field. They are not the same widget, and a
renderer that treats them alike produces something unusable in one of the two cases.

| | `matching` — a **labelled** bank | `labelling` — a **degenerate** bank |
|---|---|---|
| Example option | `{ key: "iii", text: "The Leatherback's contribution" }` | `{ key: "A", text: "A" }` |
| Does `text` carry information? | **Yes** — the whole heading | **No** — it repeats the key |
| Where the meaning lives | In the bank list itself | In `QuestionGroup.Image` — the map |
| Correct rendering | The bank listed **once above the group**, scannable; drag source | The **map**, with letters positioned on it; the list is worthless |
| What goes wrong if confused | Options buried in ten dropdowns — comparing two headings means opening two menus | Ten rows reading `A. A`, and no map |

The learner app already draws this distinction, and derives it structurally rather than from the
type: it renders a bank list only when `option.text !== option.key` for at least one option
(`QuestionList.tsx`). That test is the right one — it keys on whether the bank carries information,
not on a type name — and it is worth keeping when the drag-and-drop interaction is built.

**So the owner's drag-and-drop answer bank applies to 16 of 72 auto-scored questions in Exam 1**,
in three groups:

| Group | Module | Type | n | Bank | ELO |
|---|---|---|---|---|---|
| `r-headings-1-6` | Reading | `matching` | 6 | 10 headings, `i`–`x` | `false` |
| `r-matching-36-39` | Reading | `matching` | 4 | 4 continents, `A`–`D` | `true` |
| `l-map-15-20` | Listening | `labelling` | 6 | 10 letters `A`–`J` **+ map image** | `false` |

Listening's other 30 auto-scored questions are typing (`completion` ×22, `short-answer` ×5) or
per-question choice (`multiple-select` ×3). **No Listening group carries a shared *labelled* bank
at all** — the one Listening bank is the map's letters. A drag-and-drop bank on Listening therefore
means *drag a letter onto a map*, which is a different build from *drag a heading onto a paragraph*.

---

## Can the content model express a bank today?

**Yes for the two types that need one, but only by repetition, and the group does not own it.**

What exists:

- `matching` and `labelling` are **required by schema** to carry `options`.
- Every member of a bank group carries the **same** options array, byte for byte.
- `QuestionGroup` carries the shared frame — instruction, title, image, summary text — and
  `EachLetterOnce`, which is a rule *about* a bank the group has no field for.

What this costs:

1. **The bank is derived, not declared.** A client reconstructs it by comparing every member's
   options for equality. Two matching sets sitting in one part with different banks are handled
   correctly by that comparison — but a group whose members legitimately differ **silently loses
   its bank** and falls back to per-question dropdowns. Nothing reports that; it just renders
   plainer.
2. **The bank is duplicated N times on the wire.** `r-headings-1-6` sends ten headings six times.
   Small at this size, and it is the price of the deliberate decision that a question stays
   self-describing when it travels alone.
3. **`EachLetterOnce` governs something its own record cannot see.** The flag is on the group; the
   options are on the questions.

`[OPEN QUESTION]` **Should `QuestionGroup` own an explicit `Options` bank?** It would make the bank
declared rather than derived, put `EachLetterOnce` next to the thing it constrains, and stop the
duplication. Against it: a question would stop being self-describing on its own, which is the
property `QuestionGroup`-repeated-on-every-member was chosen for; and it is a **schema change**, so
it is a minor version bump and a migration of authored content. **Do not make this change to satisfy
the drag-and-drop feature** — the derived bank is sufficient to build it. This needs an ID in
[`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md).

---

## `EachLetterOnce` is enforced nowhere

This is a defect, not a design, and it is the clearest case in this document of a confident comment
that the code does not honour.

- **The content sets it.** `r-matching-36-39` carries `eachLetterOnce: true`.
- **It reaches the client.** Domain → `QuestionGroupView` → wire, intact.
- **The client shows it.** `QuestionList` builds a `takenBy` map naming which question already used
  each letter, and `QuestionInput` renders it — deliberately **shown, not enforced**, so a candidate
  can move a letter from one line to another without getting stuck. That reasoning is sound.
- **The client defers enforcement to the scorer:** *"the rubric is a scoring rule, and the scorer is
  what applies it."*
- **The scorer does not apply it.** `DeterministicScorer.Score` iterates question by question, and
  `AnswerMatcher.IsCorrect` takes one question and one submitted string. Neither has any
  cross-question view. A learner who uses `C` on all four questions of `r-matching-36-39` has each
  answer marked on its own merits, and whichever one is genuinely `C` is marked correct.

So the rubric says *"NB Use each letter once only"*, the interface says which letters are taken, and
nothing anywhere applies the rule.

`[OPEN QUESTION]` **What is the mark for a group whose "use each letter once" rubric was broken?**
Three defensible answers — mark every duplicated response wrong; mark only the later ones wrong;
keep marking each independently and treat the rubric as advice. This is a **scoring policy** and
therefore not a technical choice: it changes bands. Needs an ID in
[`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md).

`[NEEDS VALIDATION]` **Exam 1's flag looks wrong on two groups.** `r-headings-1-6` says *"there are
more headings than paragraphs, so you will not use them all"* — which is a statement that each
heading is used at most once — and carries `eachLetterOnce: false`. `l-map-15-20` labels six rooms
from ten distinct map positions, and also carries `false`. Both look like authoring errors rather
than modelling truths. This is content to be corrected, not code.

---

## The `Pair` shape is reachable and unused

`AcceptedAnswer` has exactly three shapes, and `AnswerMatcher` routes on **which one the key
carries**, never on the question type. That is the correct dispatch and it was arrived at the hard
way: routing on `Type` meant guessing whether a `matching` question was *choose a heading* (one key)
or *pair two lists* (a pair), and the guess marked every matching question in Reading wrong.

| Shape | Written in the package as | Submitted as | Used in Exam 1 |
|---|---|---|---|
| `Single` | `"i"` | the value | ✅ 73 questions |
| `All` | `["A", "D"]` | `"A\|D"`, order-insensitive | ✅ 3 questions |
| `Pair` | `{ "left": …, "right": … }` | `"left:right"` or `"left=right"` | ❌ **none** |

`Pair` is fully plumbed — schema, reader, persistence mappers, matcher — and no authored content
uses it, so no renderer has ever had to draw it. A true two-column drag-to-pair interaction would be
the first caller. Until content exists, treat the `"left:right"` submission format as **unproven**:
it is a string convention with no test content behind it, and the pipe was chosen as the set
separator precisely because commas and spaces occur inside real answers.

---

## What a drag-and-drop bank does **not** change

Stated explicitly, because a drag interaction looks like it should need a new answer format and it
does not.

- **The stored value stays the option key.** Dropping heading `iii` onto paragraph B stores `"iii"`
  for that question — exactly what the dropdown stores today. `AnswerMatcher` needs no change.
- **The autosave path stays.** Each drop is one question's value changing, which is one entry in a
  patch. That is already the unit of write.
- **The answer key never crosses to the client.** `QuestionView` has no field an `AnswerKey` could
  travel in, and the bank is `Options`, which is not the key.
- **`marks` stays.** A two-mark `multiple-select` is two marks however it is drawn.

The change is a renderer and an input affordance. It is not a domain change, and it must not become
one.

---

→ Entity model and the practice/mock split: [`domain-model.md`](domain-model.md)
→ Package format and the frozen taxonomy: [`../architecture/exam-package-format.md`](../architecture/exam-package-format.md)
→ How marks become bands: [`band-scoring.md`](band-scoring.md)
