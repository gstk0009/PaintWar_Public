using System;

public static class AiNicknamePool
{
    private static readonly string[] Names =
    {
        "Ace", "Amber", "Archer", "Atlas", "Aurora", "Axel", "Blake", "Blaze", "Bolt", "Breeze",
        "Brook", "Cedar", "Chase", "Cinder", "Cleo", "Cloud", "Cobalt", "Comet", "Coral", "Cosmo",
        "Crimson", "Dash", "Dawn", "Delta", "Drake", "Echo", "Ember", "Falcon", "Fern", "Finn",
        "Flare", "Flint", "Forest", "Frost", "Gale", "Gem", "Ghost", "Glint", "Hazel", "Hunter",
        "Indigo", "Iris", "Ivy", "Jade", "Jasper", "Jet", "Kai", "Kite", "Leo", "Lime",
        "Luna", "Maple", "Mars", "Mica", "Milo", "Mint", "Mist", "Neo", "Noble", "Nova",
        "Ocean", "Olive", "Onyx", "Opal", "Orion", "Pearl", "Pepper", "Phoenix", "Pine", "Pixel",
        "Pluto", "Quartz", "Raven", "Reef", "River", "Robin", "Rocket", "Rose", "Rowan", "Ruby",
        "Sage", "Scout", "Shadow", "Sky", "Slate", "Solar", "Sparrow", "Spark", "Storm", "Sunny",
        "Swift", "Terra", "Tiger", "Vega", "Violet", "Wave", "Willow", "Wolf", "Zephyr", "Zion"
    };

    public static int Count { get { return Names.Length; } }

    public static string GetName(int index)
    {
        if (Names.Length == 0)
        {
            return "Bot";
        }

        int normalized = index % Names.Length;
        if (normalized < 0)
        {
            normalized += Names.Length;
        }

        return Names[normalized];
    }

    public static int GetRandomStartIndex(int seed)
    {
        return new Random(seed).Next(Names.Length);
    }
}
