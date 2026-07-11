using GameDisplaySwitcher;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var sequence = new SequenceSettings
{
    MaxGapMs = 100,
    Steps =
    [
        new SequenceStep { Buttons = ["View", "Menu"] },
        new SequenceStep { Buttons = ["A"] }
    ]
};
var matcher = new SequenceMatcher(() => sequence);

Assert(!matcher.Feed(0, new HashSet<string> { "View" }, new HashSet<string> { "View" }), "Partial chord triggered.");
Assert(!matcher.Feed(0, new HashSet<string> { "View", "Menu" }, new HashSet<string> { "Menu" }), "First step triggered action.");
Assert(matcher.Feed(0, new HashSet<string> { "A" }, new HashSet<string> { "A" }), "Valid sequence did not trigger.");
Assert(!matcher.Feed(0, new HashSet<string> { "A" }, new HashSet<string> { "A" }), "Cooldown did not suppress retrigger.");

var timeoutSequence = new SequenceSettings
{
    MaxGapMs = 30,
    Steps = [new SequenceStep { Buttons = ["X"] }, new SequenceStep { Buttons = ["Y"] }]
};
var timeoutMatcher = new SequenceMatcher(() => timeoutSequence);
Assert(!timeoutMatcher.Feed(0, new HashSet<string> { "X" }, new HashSet<string> { "X" }), "First timeout step triggered.");
Thread.Sleep(45);
Assert(!timeoutMatcher.Feed(0, new HashSet<string> { "Y" }, new HashSet<string> { "Y" }), "Expired sequence triggered.");

var controllerMatcher = new SequenceMatcher(() => timeoutSequence);
Assert(!controllerMatcher.Feed(0, new HashSet<string> { "X" }, new HashSet<string> { "X" }), "First controller step triggered.");
Assert(!controllerMatcher.Feed(1, new HashSet<string> { "Y" }, new HashSet<string> { "Y" }), "Sequence crossed controllers.");

Console.WriteLine("SequenceMatcher tests passed.");
