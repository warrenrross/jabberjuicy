namespace WebAppWithDB_Starter_v2
{
    internal static class DrinkQuotes
    {
        private static readonly Random _rng = new();

        public static readonly Dictionary<string, string> All =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["Brillig Blend"] =
                "In the brillig of the afternoon, when the sun dips golden and the slithy toves " +
                "grow thirsty, only the Brillig Blend can quench the wabe.",

            ["Slithy Smoothie"] =
                "Oh, slithy and lithe it slips down the throat — all mango and moon and coconut dreams. " +
                "The toves themselves would gyre no more, but sit and sip in the mimsy shade.",

            ["Borogove Green"] =
                "The borogoves may be all mimsy, but even they stand upright after a sip of this " +
                "emerald elixir. Kale and cucumber, ginger and glee — the wabe has never tasted so alive.",

            ["Mimsy Morning"] =
                "Before the Jabberwock stirs in the tulgey wood, there is a moment — mimsy and still — " +
                "when hibiscus blooms on your tongue and the whole morning tastes of rose and lychee.",

            ["Uffish Berry"] =
                "He stood in uffish thought, manxome foes looming at every turn. But then the Uffish Berry " +
                "found his lips, and all that dark acai fury swept every shadow clean from mind.",

            ["Tulgey Greens"] =
                "Deep in the tulgey wood, where the tumtum tree grows thick and the spirulina runs wild, " +
                "this is the drink the borogoves brew when no one is watching. Sip it. " +
                "You will outgrabe with delight.",

            ["Frabjous Signature"] =
                "'O frabjous day! Callooh! Callay!' He chortled in his joy, for the Frabjous Signature " +
                "awaited — the most celebrated sip in all the land, pressed by beamish hands " +
                "at the break of every brilliant dawn.",

            ["Galumphing Gulp"] =
                "He came galumphing back, oat-stained and triumphant, banana honey thick upon his beard, " +
                "and declared to the Jubjub bird with great solemnity: 'This one. Always this one.'",

            ["Beamish Citrus"] =
                "'And hast thou slain the Jabberwock? Come to my arms, my beamish boy!' — and he came, " +
                "radiant as turmeric, bright as orange rind, smelling of pineapple and hard-won victory.",

            ["Vorpal Zing"] =
                "The vorpal blade went snicker-snack, yes — but first it was steeped in ginger and " +
                "cayenne and lemon, for a blade that sharp deserves a sip this fierce. " +
                "Take heed, O wanderer of the wabe.",

            ["Burble Fizz"] =
                "The Jabberwock came burbling through the tulgey wood, and where his breath met the " +
                "kombucha air, the Burble Fizz was born — sparkling, untamed, and absolutely " +
                "not safe for mome raths.",

            ["Whiffling Mint"] =
                "Through the tulgey wood it came whiffling — cool as cucumber, bright as lime, " +
                "carrying coconut breezes from shores the mome raths have never dreamed of. " +
                "It outgribes all expectations.",

            ["Snicker-Snack Apple"] =
                "One, two! One, two! And through and through — the vorpal blade went snicker-snack! " +
                "Crisp apple, sharp ginger, a celery strike so clean it would make " +
                "the Jabberwock himself weep into his tulgey tea.",
        };

        public static readonly string Fallback =
            "'Twas brillig, and the slithy toves did gyre and gimble in the wabe — " +
            "but first, they ordered juice. May your sip be frabjous.";

        public static string GetRandom(IEnumerable<string> drinkNames)
        {
            var matches = drinkNames
                .Where(n => All.ContainsKey(n))
                .Select(n => All[n])
                .ToList();
            return matches.Count == 0 ? Fallback : matches[_rng.Next(matches.Count)];
        }
    }
}
