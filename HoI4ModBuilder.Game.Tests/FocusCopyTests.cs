using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Game.Tests;

public sealed class FocusCopyTests
{
    [Fact]
    public void FindsTheCommonPrefix()
    {
        Assert.Equal("UKR_", FocusCopy.CommonPrefix(["UKR_army", "UKR_industry"]));
        Assert.Null(FocusCopy.CommonPrefix(["UKR_army", "POL_industry"]));
        Assert.Null(FocusCopy.CommonPrefix(["army"]));
    }

    [Fact]
    public void GivesFreshIds()
    {
        var ids = FocusCopy.NewIds(["UKR_army", "UKR_tanks"], "UKR_", "POL_", ["POL_army"]);
        Assert.Equal("POL_army_2", ids["UKR_army"]);
        Assert.Equal("POL_tanks", ids["UKR_tanks"]);

        var same = FocusCopy.NewIds(["UKR_army"], null, null, ["UKR_army", "UKR_army_2"]);
        Assert.Equal("UKR_army_3", same["UKR_army"]);
    }

    [Fact]
    public void RenamesLinksAmongCopiesAndShiftsPositions()
    {
        var army = new CopiedFocus("UKR_army", "focus = {\n\t\tid = UKR_army\n\t\tx = 3\n\t\ty = 1\n\t\tprerequisite = { focus = UKR_start }\n\t}", "\t", 3, 1, null);
        var tanks = new CopiedFocus("UKR_tanks",
            "focus = {\n\t\tid = UKR_tanks\n\t\trelative_position_id = UKR_army\n\t\tx = 0\n\t\ty = 1\n\t\tprerequisite = { focus = UKR_army }\n\t}",
            "\t", 3, 2, "UKR_army");
        var renames = FocusCopy.NewIds(["UKR_army", "UKR_tanks"], "UKR_", "POL_", []);

        var pastedArmy = FocusCopy.Rewrite(army, renames, origin: (3, 1), target: (10, 5), indent: "\t");
        var pastedTanks = FocusCopy.Rewrite(tanks, renames, origin: (3, 1), target: (10, 5), indent: "\t\t");

        // Outside links stay; the position becomes the target cell.
        Assert.Equal("focus = {\n\t\tid = POL_army\n\t\tx = 10\n\t\ty = 5\n\t\tprerequisite = { focus = UKR_start }\n\t}", pastedArmy);
        // Relative to another copy: offsets kept, links follow the new ids, lines re-indented.
        Assert.Equal("focus = {\n\t\t\tid = POL_tanks\n\t\t\trelative_position_id = POL_army\n\t\t\tx = 0\n\t\t\ty = 1\n\t\t\tprerequisite = { focus = POL_army }\n\t\t}",
            pastedTanks);
    }

    [Fact]
    public void KeepsOnlyExclusionsAmongTheCopies()
    {
        var focus = new CopiedFocus("A",
            "focus = {\n\tid = A\n\tx = 0\n\ty = 0\n\tmutually_exclusive = { focus = B focus = C }\n\tmutually_exclusive = { focus = D }\n}", "", 0, 0, null);

        var pasted = FocusCopy.Rewrite(focus, new Dictionary<string, string> { ["A"] = "A2", ["B"] = "B2" }, (0, 0), (0, 0), "");

        Assert.Equal("focus = {\n\tid = A2\n\tx = 0\n\ty = 0\n\tmutually_exclusive = { focus = B2 }\n}", pasted);
    }

    [Fact]
    public void ARelativeAnchorOutsideTheCopyBecomesAbsolute()
    {
        var focus = new CopiedFocus("UKR_tanks", "focus = {\n\tid = UKR_tanks\n\trelative_position_id = UKR_army\n\tx = 1\n\ty = 1\n}", "", 4, 2, "UKR_army");

        var pasted = FocusCopy.Rewrite(focus, FocusCopy.NewIds(["UKR_tanks"], null, null, ["UKR_tanks"]), (4, 2), (0, 7), "");

        Assert.Equal("focus = {\n\tid = UKR_tanks_2\n\tx = 0\n\ty = 7\n}", pasted);
    }
}
