using MultiFrameViewer;

const string pool = @"C:\__sanjing_random_cycle_test__";
var candidates = new[] { @"C:\photos\A", @"C:\photos\B", @"C:\photos\C" };
var cycle = new RandomFolderCycle();

cycle.RecordDisplayed(pool, candidates[0]);
var first = cycle.Pick(pool, candidates, candidates[0]);
Assert(first is not null && first != candidates[0], "First pick repeated the current folder.");
cycle.RecordDisplayed(pool, first!);

var second = cycle.Pick(pool, candidates, first!);
Assert(second is not null && second != candidates[0] && second != first, "Second pick repeated a displayed folder.");
cycle.RecordDisplayed(pool, second!);

var afterCycle = cycle.Pick(pool, candidates, second!);
Assert(afterCycle is not null && afterCycle != second, "Cycle did not reset after every folder was displayed.");

var single = new[] { @"C:\photos\Only" };
var singleFolderCycle = new RandomFolderCycle();
singleFolderCycle.RecordDisplayed(pool, single[0]);
Assert(
    singleFolderCycle.Pick(pool, single, single[0]) is null,
    "A pool with only the current folder should not select anything.");

for (var poolNumber = 0; poolNumber < 50; poolNumber++)
{
    var folders = Enumerable.Range(0, 6)
        .Select(number => $@"C:\photos\pool-{poolNumber}\{number}")
        .ToArray();
    var noRepeatCycle = new RandomFolderCycle();
    var displayed = new HashSet<string> { folders[0] };
    noRepeatCycle.RecordDisplayed($@"C:\photos\pool-{poolNumber}", folders[0]);

    var current = folders[0];
    for (var pickNumber = 1; pickNumber < folders.Length; pickNumber++)
    {
        var selected = noRepeatCycle.Pick(
            $@"C:\photos\pool-{poolNumber}",
            folders,
            current);
        Assert(selected is not null, "The cycle returned no folder before the pool was exhausted.");
        Assert(selected != current, "The cycle immediately repeated the current folder.");
        Assert(displayed.Add(selected!), "The cycle repeated a folder before the pool was exhausted.");
        current = selected!;
    }

    var nextCycle = noRepeatCycle.Pick(
        $@"C:\photos\pool-{poolNumber}",
        folders,
        current);
    Assert(nextCycle is not null, "The cycle did not restart after the pool was exhausted.");
    Assert(nextCycle != current, "The restarted cycle repeated the current folder.");
}

Console.WriteLine("PASS: folders are sampled without repetition and the cycle resets.");
return;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
