# LearnX

A place to learn, as a **content app** in ImmersiveX ([CONTENT_GUIDE.md](../../../CONTENT_GUIDE.md#15-content-apps-an-app-in-the-content-menu)): it's a row in the content menu, and picking it turns the menu to LearnX's own pages.

- **Choose your class.** Nursery and Years 1 to 12, a row for each stage of education in England.
- **Each class has its own lessons.** A lesson is a few short pages, then a question to check what you know.
- **Some lessons happen in the room.** Nursery's **Counting to 10** is a 30-second 4D mesh that plays in the room as soon as you choose it: the numbers 1 to 10 come in one at a time, a young child's voice says each one, and you say it with them.
- **Written for its age**, in GOV.UK's plain English style, to the national curriculum in England (or the early years, GCSE or A level content).

LearnX is also a standalone app built on ImmersiveX as bare metal content, in the ImmersiveX_PROJECTS repository. This is the same LearnX, as content.

## The classes
| Stage | Classes | Ages | What the lessons follow |
|---|---|---|---|
| Early years | Nursery | 3 to 4 | The EYFS statutory framework and Development Matters (non-statutory guidance) |
| Key stage 1 | Year 1, Year 2 | 5 to 7 | The national curriculum in England: primary |
| Key stage 2 | Years 3 to 6 | 7 to 11 | The national curriculum in England: primary |
| Key stage 3 | Years 7 to 9 | 11 to 14 | The national curriculum in England: secondary |
| Key stage 4 | Year 10, Year 11 | 14 to 16 | GCSE subject content |
| Sixth form | Year 12 | 16 to 17 | A level subject content |

Nursery has one lesson, Counting to 10, and every other class has 4 sample lessons, one subject each: 49 in all.

## Using it
- In the content menu, choose **LearnX**. The menu turns to its classes; **Content** at the bottom goes back to the menu's list, with LearnX still showing.
- Choose a **class** to see its lessons. **All classes** at the bottom goes back.
- Choose a **lesson** to read it a page at a time with **Previous** and **Next**. On the last page, **Check what you know** asks a question. **All lessons** goes back to the class.
- A lesson with immersive media is the media, with no pages to read. Choosing it puts the menu away and plays it in the room as Immersive Media: in front of you, anchored, on the mapped room's floor, with its controls (and **Replay**). When it ends, the menu comes back with **Watch again** and **Check what you know**. Leaving the lesson takes the media away; showing other content hides and pauses it.
- Choose an answer. A right answer turns green and says why, then **Finish** goes back to the class. A wrong answer turns red without giving the answer away, and **Try again** clears it.
- At the bottom of each lesson is the part of the curriculum it teaches, for teachers and parents.
- While LearnX is showing, the content menu's opening swipe opens LearnX's page where you left it.

## Writing lessons
Each class's file has its `stage`, `ages`, an optional `note` and its `lessons`. A lesson has an `id`, `subject`, `title`, `summary`, `curriculum` (the part of the curriculum it teaches), `pages`, and a `check` with its `question`, `options` (2 to 4), `answer` (counted from 0) and `explanation`.

A lesson can have `media` in place of its pages: the `file` to play (an ImmersiveX `.sp4m`, relative to `StreamingAssets`), its `height` in metres (0.3 to 4) and the `text` the menu shows when it ends, which says what to do while it plays. A lesson with media has no `pages`, so it plays as soon as it opens.

Lessons follow GOV.UK's [writing guidelines](https://guidance.publishing.service.gov.uk/writing-to-gov-uk-standards/writing-guidelines/) and [A to Z style guide](https://guidance.publishing.service.gov.uk/writing-to-gov-uk-standards/style-guides/a-to-z-style-guide/). `ContentStyle` checks every lesson, and the tests fail if one breaks a rule:

- **Short sentences:** at most 12 words for early years, 14 for key stage 1, 18 for key stage 2 and 22 for key stage 3. Key stage 4 and the sixth form keep to GOV.UK's 25.
- **Plain words:** "for example" or "such as", not eg, ie or etc; "and", not &; no "please"; no exclamation marks.
- **No negative contractions:** "cannot" and "do not", not can't or don't. A lesson on apostrophes quotes them instead: ‘don't’.
- **Numbers:** numerals, except "one"; first to ninth written out; no ordinals in dates (2 June); "to" in date ranges (1914 to 1918); "per cent", or % after a number.
- **Quotes:** single curly quotes for words, terms, titles and examples (‘sh’, ‘A Christmas Carol’), and double curly quotes only for what someone said or wrote (“Two households, both alike in dignity”). Apostrophes in words are straight (it's, Scrooge's).
- **Titles:** 65 characters or fewer, not a question, no full stop, dash or slash.
- **Summaries:** 160 characters or fewer, ending with a full stop.
- **Curriculum line:** 140 characters or fewer, so it fits its 2 lines above the buttons.
- **Pages:** at least 2, or none for a lesson with media.
- **Media:** the file isn't named `.gz` (Android's packer drops the extension), and its `text` follows the rules above.

What each year learns comes from the Department for Education's published content:

- the [national curriculum](https://www.gov.uk/government/collections/national-curriculum) ([primary](https://www.gov.uk/government/publications/national-curriculum-in-england-primary-curriculum) and [secondary](https://www.gov.uk/government/publications/national-curriculum-in-england-secondary-curriculum));
- for Nursery, the [EYFS framework](https://www.gov.uk/government/publications/early-years-foundation-stage-framework--2) and [Development Matters](https://www.gov.uk/government/publications/development-matters--2);
- the [GCSE](https://www.gov.uk/government/collections/gcse-subject-content) and [A level](https://www.gov.uk/government/collections/gce-as-and-a-level-subject-content) subject content.

Each lesson's `curriculum` names the part it teaches.

## Immersive media
A lesson's media is ImmersiveX Immersive Media (a 4D mesh, `.sp4m`, with its `soundtrack.m4a` beside it), played by ImmersiveX's `ImmersiveMedia` component. The files are large, so `Assets/StreamingAssets/ImmersiveXContent/` is kept out of git.

| Lesson | Folder | Files |
|---|---|---|
| Nursery: Counting to 10 | `ImmersiveXContent/Counting_to_10/` | `counting_to_10.sp4m` (25.6 MB), `soundtrack.m4a` (0.4 MB) |

Put each lesson's files in its folder before you build. The tests fail and name the missing file if one isn't there.

**Counting to 10** follows Development Matters for 3 and 4 year olds: saying the numbers past 5 in order, and saying one number for each thing counted. Each number comes into a garden with that many stars, at the same spot near the front and in the middle, then hops back into a row. A young child's voice says each number, with a pause after it so children can say it back. It is 30 seconds long, 1.8 m tall, and made of 174 objects. It was made with a 4D pipeline outside this repo, and the scene was built in three.js and baked to a mesh.

The voice is [OmniVoice](https://github.com/k2-fsa/OmniVoice) (k2-fsa, Apache 2.0), designed from the description `child, high pitch, british accent`, so no real child was recorded or copied. The numbers were counted in one take and cut apart, so they sound like a child counting. Every line was checked by speech recognition ([Whisper](https://huggingface.co/Systran/faster-whisper-small.en) small.en) and its pitch was measured. The voice speaks at a median of about 300 Hz, which is in a young child's range.

## Adding it and testing it
- **ImmersiveX ▸ Content ▸ Add LearnX** (automation: `learnx add`) puts LearnX at the top of the open scene's content and saves the scene.
- Tests (`LearnX.Tests`, run with the rest): the classes, their order, stages and ages; every lesson against the style rules; moving through a lesson, its watch step and its check; that every lesson's media is in `StreamingAssets`.
- Automation in Play mode (see [CONTRIBUTING](../../../CONTRIBUTING.md#driving-the-open-editor-automation)):
  - `contentmenu select <n>` picks LearnX's row; `contentmenu back` returns to the list.
  - `learnx status` · `learnx open|close` (the menu on LearnX's page) · `learnx home` · `learnx class <name>` · `learnx lesson <n>` · `learnx next|previous` · `learnx answer <n>` (from 1) · `learnx again` · `learnx watch` · `learnx media [seek <s>|end]` · `learnx fit` · `learnx tour` (every view: what doesn't fit, and every answer marked as it should be) · `learnx check` (the style rules, in either mode).
  - `host press <label>` presses LearnX's buttons as a ray or finger does: `host press Nursery`, `host press Counting to 10`.
