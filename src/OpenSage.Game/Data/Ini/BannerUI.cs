﻿using System.Collections.Generic;
using OpenSage.Data.Ini.Parser;

namespace OpenSage.Data.Ini
{
    [AddedIn(SageGame.Bfme)]
    public sealed class BannerUI
    {
        internal static BannerUI Parse(IniParser parser) => parser.ParseTopLevelBlock(FieldParseTable);

        private static readonly IniParseTable<BannerUI> FieldParseTable = new IniParseTable<BannerUI>
        {
            { "HeroFilter", (parser, x) => x.HeroFilter = ObjectFilter.Parse(parser) },
            { "UnitCategory", (parser, x) => x.UnitCategories.Add(BannerUnitCategory.Parse(parser)) }
        };

        public ObjectFilter HeroFilter { get; private set; }
        public List<BannerUnitCategory> UnitCategories { get; } = new List<BannerUnitCategory>();
    }

    [AddedIn(SageGame.Bfme)]
    public sealed class BannerUnitCategory
    {
        internal static BannerUnitCategory Parse(IniParser parser) => parser.ParseTopLevelBlock(FieldParseTable);

        private static readonly IniParseTable<BannerUnitCategory> FieldParseTable = new IniParseTable<BannerUnitCategory>
        {
            { "Name", (parser, x) => x.Name = parser.ParseLocalizedStringKey() },
            { "PluralName", (parser, x) => x.PluralName = parser.ParseLocalizedStringKey() },
            { "Filter", (parser, x) => x.Filter = ObjectFilter.Parse(parser) },
        };

        public string Name { get; private set; }
        public string PluralName { get; private set; }
        public ObjectFilter Filter { get; private set; }
    }
}
