using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Map;

namespace HoI4ModBuilder.Game.Tests;

public sealed class MapTests
{
    [Fact]
    public void ReadsProvincesFromTheBitmap()
    {
        var folder = Directory.CreateTempSubdirectory("hoi4map");
        try
        {
            // 3×2, bottom-up, rows padded to 4 bytes: bottom row red red blue, top row blue blue unknown.
            var bmp = new List<byte>();
            byte[] Row(params (byte R, byte G, byte B)[] pixels) =>
                pixels.SelectMany(p => new[] { p.B, p.G, p.R }).Concat(new byte[(4 - pixels.Length * 3 % 4) % 4]).ToArray();
            var rows = Row((255, 0, 0), (255, 0, 0), (0, 0, 255)).Concat(Row((0, 0, 255), (0, 0, 255), (1, 2, 3))).ToArray();
            bmp.AddRange("BM"u8.ToArray());
            bmp.AddRange(BitConverter.GetBytes(54 + rows.Length));
            bmp.AddRange(new byte[4]);
            bmp.AddRange(BitConverter.GetBytes(54));
            bmp.AddRange(BitConverter.GetBytes(40));
            bmp.AddRange(BitConverter.GetBytes(3));
            bmp.AddRange(BitConverter.GetBytes(2));
            bmp.AddRange(BitConverter.GetBytes((short)1));
            bmp.AddRange(BitConverter.GetBytes((short)24));
            bmp.AddRange(new byte[24]);
            bmp.AddRange(rows);
            File.WriteAllBytes(Path.Combine(folder.FullName, "provinces.bmp"), bmp.ToArray());
            File.WriteAllText(Path.Combine(folder.FullName, "definition.csv"),
                "0;0;0;0;land;false;unknown;0\n7;255;0;0;land;false;plains;1\n8;0;0;255;sea;true;ocean;0\n");

            var map = ProvinceMap.Load(Path.Combine(folder.FullName, "provinces.bmp"), Path.Combine(folder.FullName, "definition.csv"));

            Assert.Equal((3, 2), (map.Width, map.Height));
            Assert.Equal(8, map.At(0, 0)?.Id); // top row is blue
            Assert.Equal(ProvinceType.Sea, map.At(1, 0)?.Type);
            Assert.Null(map.At(2, 0)); // a colour not in definition.csv
            Assert.Equal(7, map.At(1, 1)?.Id);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadsAStateAtTheStart()
    {
        var state = MapData.ReadStates("""
            state = {
                id = 64
                name = "STATE_64"
                manpower = 7265029
                state_category = megalopolis
                resources = { steel = 8 oil = 2 }
                history = {
                    owner = GER
                    add_core_of = GER
                    add_core_of = PRE
                    victory_points = { 6521 50 }
                    buildings = {
                        infrastructure = 4
                        arms_factory = 5
                        6521 = { naval_base = 3 }
                    }
                    1939.1.1 = { owner = SOV }
                }
                provinces = { 375 478 6521 }
            }
            """, "64-Brandenburg.txt", isMod: false).Single();

        Assert.Equal((64, "STATE_64", "GER", "megalopolis", 7265029L), (state.Id, state.NameKey, state.Owner, state.Category, state.Manpower));
        Assert.Equal(["GER", "PRE"], state.Cores);
        Assert.Equal([375, 478, 6521], state.Provinces);
        Assert.Equal(new Dictionary<string, int> { ["infrastructure"] = 4, ["arms_factory"] = 5 }, state.Buildings);
        Assert.Equal(8m, state.Resources["steel"]);
        Assert.Equal((6521, 50m), state.VictoryPoints.Single());
    }

    [Theory]
    [InlineData("color = { 106 119 89 }", 0x6A7759)]
    [InlineData("color = rgb { 201 56 93 }", 0xC9385D)]
    [InlineData("color = HSV { 0 1 1 }", 0xFF0000)]
    public void ReadsCountryColours(string script, int expected)
    {
        var value = SyntaxTree.Parse(script).Root.Properties().First().Value;
        Assert.Equal(expected, MapData.ColourOf(value));
    }

    [Fact]
    public void FindsWhatScriptsDoWithStates()
    {
        var tree = SyntaxTree.Parse("""
            focus_tree = {
                id = ukraine_focus
                focus = {
                    id = UKR_claim_kuban
                    available = { owns_state = 202 }
                    completion_reward = {
                        transfer_state = 219
                        add_state_claim = 237
                        202 = { add_core_of = UKR add_building_construction = { type = arms_factory level = 2 } }
                        random_list = { 30 = { add_political_power = 10 } }
                    }
                }
            }
            """);

        var found = StateReferences.Find(tree, "common/national_focus/ukraine.txt")
            .Select(r => (r.State, r.Action, r.Key, r.Country, r.Usage.Owner?.Id, r.Usage.Context)).ToList();

        Assert.Equal(
        [
            (202, StateAction.OwnerCheck, "owns_state", null, "UKR_claim_kuban", "available › owns_state"),
            (219, StateAction.Transfer, "transfer_state", null, "UKR_claim_kuban", "completion_reward › transfer_state"),
            (237, StateAction.Claim, "add_state_claim", null, "UKR_claim_kuban", "completion_reward › add_state_claim"),
            (202, StateAction.Core, "add_core_of", "UKR", "UKR_claim_kuban", "completion_reward › 202 › add_core_of"),
            (202, StateAction.Building, "add_building_construction", null, "UKR_claim_kuban", "completion_reward › 202 › add_building_construction"),
        ], found);
    }
}
