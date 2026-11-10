using System;
using System.Globalization;

namespace GrandStrategy.WorldGeneration
{
    internal enum ExpansionApproach { None, Diplomacy, Trade, Force }

    [Serializable]
    internal sealed class CampaignSave
    {
        public int Version;
        public string MapKey;
        public string TribeName;
        public int Colour;
        public int CapitalCell;
        public int Day;
        public int Speed;
        public int Coin;
        public bool[] Owned;
        public int[] Pop;
        public int IntegrationTarget;
        public int IntegrationDaysLeft;
        public int Food;
        public int Wood;
        public bool Granary;
        public bool Warband;
        public int TradePartner;
        public int WinterShortfall;
        public ExpansionApproach PendingApproach;
        public ExpansionApproach FirstExpansion;
    }

    internal sealed class CampaignState
    {
        public const int StartingCoin = 100;
        public const int IntegrationCost = 25;
        public const int IntegrationDuration = 30;
        public const int MaxNameLength = 32;
        public const int WinterStart = 90;
        public const int WinterEnd = 120;
        public const int GranaryWood = 35;
        public const int GranaryCoin = 35;
        public const int WarbandWood = 35;
        public const int WarbandCoin = 45;
        public const int ForceFood = 8;
        private const int SaveVersion = 2;
        private const int StartingFood = 40;
        private const int StartingWood = 45;
        private const int ResourceLimit = 999;
        public const int PactCost = 12;
        private const int PactDuration = 20;
        private const int ForceDuration = 10;

        private readonly ProvinceMap map;
        private readonly CampaignSave data;
        private double fractionOfDay;

        private CampaignState(ProvinceMap map, CampaignSave data)
        {
            this.map = map;
            this.data = data;
        }

        public string TribeName => data.TribeName;
        public int Colour => data.Colour;
        public int CapitalCell => data.CapitalCell;
        public int Capital => map.ProvinceAt[CapitalCell];
        public int Day => data.Day;
        public int Speed => data.Speed;
        public int Coin => data.Coin;
        public int IntegrationTarget => data.IntegrationTarget;
        public int IntegrationDaysLeft => data.IntegrationDaysLeft;
        public bool Paused { get; set; } = true;
        public int Food => data.Food;
        public int Wood => data.Wood;
        public bool HasGranary => data.Granary;
        public bool HasWarband => data.Warband;
        public int TradePartner => data.TradePartner;
        public int WinterShortfall => data.WinterShortfall;
        public int WinterFoodRemaining => Math.Max(0, WinterEnd / 5 - Math.Max(Day, WinterStart) / 5) * WinterFoodPerPeriod;
        private int WinterFoodPerPeriod => (HasGranary ? 8 : 14) + (HasWarband ? 1 : 0);
        public ExpansionApproach FirstExpansion => data.FirstExpansion;
        public ExpansionApproach PendingApproach => data.PendingApproach;
        public bool CanBuildGranary => !HasGranary && Wood >= GranaryWood && Coin >= GranaryCoin;
        public bool CanRecruitWarband => !HasWarband && Wood >= WarbandWood && Coin >= WarbandCoin;

        public bool Owns(int province) => province >= 0 && province < data.Owned.Length && data.Owned[province];
        public int Population(int province) => data.Pop[province];
        public CampaignSave ToSave() => data;

        public static CampaignState Found(ProvinceMap map, string mapKey, int capitalCell, string name, int colour)
        {
            var capital = ProvinceAt(map, capitalCell);
            if (!IsLand(map, capital) || !IsValidName(name) || colour < 0)
                throw new ArgumentException("Founding requires a land province, a tribe name, and a colour.");
            var owned = new bool[map.Provinces.Length];
            var pop = new int[owned.Length];
            owned[capital] = true;
            for (var i = 0; i < pop.Length; i++)
                if (IsLand(map, i))
                    pop[i] = 100 + Math.Min(900, map.Provinces[i].PixelCount / 4);
            return new CampaignState(map, new CampaignSave
            {
                Version = SaveVersion,
                MapKey = mapKey,
                TribeName = name.Trim(),
                Colour = colour,
                CapitalCell = capitalCell,
                Day = 1,
                Speed = 1,
                Coin = StartingCoin,
                Owned = owned,
                Pop = pop,
                IntegrationTarget = -1,
                TradePartner = -1,
                Food = StartingFood,
                Wood = StartingWood
            });
        }

        public static CampaignState Restore(ProvinceMap map, string mapKey, CampaignSave save, int colourCount)
        {
            if (save == null || (save.Version != 1 && save.Version != SaveVersion) || save.MapKey != mapKey)
                throw new ArgumentException("The save version or authored map does not match.");
            var capital = ProvinceAt(map, save.CapitalCell);
            if (!IsLand(map, capital) || !IsValidName(save.TribeName) || save.Colour < 0 || save.Colour >= colourCount
                || save.Day < 1 || !IsValidSpeed(save.Speed) || save.Coin < 0 || save.Coin > StartingCoin
                || save.Owned?.Length != map.Provinces.Length || save.Pop?.Length != map.Provinces.Length
                || !save.Owned[capital])
                throw new ArgumentException("The save contains invalid campaign data.");

            for (var i = 0; i < save.Pop.Length; i++)
                if (save.Pop[i] < 0 || (!IsLand(map, i) && (save.Pop[i] != 0 || save.Owned[i])))
                    throw new ArgumentException("The save contains invalid province population or ownership.");

            var state = new CampaignState(map, save);
            var target = save.IntegrationTarget;
            var legacy = save.Version == 1;
            var pending = legacy && target >= 0 ? ExpansionApproach.Diplomacy : save.PendingApproach;
            if (target == -1 ? save.IntegrationDaysLeft != 0 || pending != ExpansionApproach.None
                : pending == ExpansionApproach.None || !Enum.IsDefined(typeof(ExpansionApproach), pending)
                    || save.IntegrationDaysLeft < 1 || save.IntegrationDaysLeft > DaysFor(pending)
                    || !state.CanReachIndependentProvince(target))
                throw new ArgumentException("The save contains invalid integration progress.");

            if (legacy)
            {
                save.Version = SaveVersion;
                save.Food = StartingFood;
                save.Wood = StartingWood;
                save.TradePartner = -1;
                save.PendingApproach = pending;
            }
            else if (save.Food < 0 || save.Food > ResourceLimit || save.Wood < 0 || save.Wood > ResourceLimit
                || save.WinterShortfall < 0 || save.WinterShortfall > 105
                || (save.TradePartner != -1 && !IsLand(map, save.TradePartner))
                || (pending == ExpansionApproach.Trade && save.TradePartner != target)
                || (pending == ExpansionApproach.Force && !save.Warband)
                || !Enum.IsDefined(typeof(ExpansionApproach), save.FirstExpansion))
                throw new ArgumentException("The save contains invalid settlement data.");
            return state;
        }

        public void SetSpeed(int speed)
        {
            if (!IsValidSpeed(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
            data.Speed = speed;
        }

        public bool CanReachIndependentProvince(int province)
        {
            if (!IsLand(map, province) || Owns(province)) return false;
            var neighbours = map.Provinces[province].Neighbours;
            if (neighbours != null)
                foreach (var neighbour in neighbours)
                    if (Owns(neighbour)) return true;
            return false;
        }

        public static int Fertility(ProvinceInfo province)
        {
            var fertile = Count(province, BiomeKind.Grassland) + Count(province, BiomeKind.TemperateForest)
                + Count(province, BiomeKind.TropicalForest);
            return fertile > 0 && fertile * 2L >= province.PixelCount ? 3
                : Count(province, BiomeKind.Desert) + Count(province, BiomeKind.Tundra)
                    + Count(province, BiomeKind.Alpine) > province.PixelCount / 2 ? 1 : 2;
        }

        public static bool HasTradeAccess(ProvinceInfo province)
            => province.RiverBankEdges > 0 || province.RiverSamples > 0 || province.TouchesOpenSea;

        public static bool IsDefensible(ProvinceInfo province)
            => Count(province, BiomeKind.Alpine) > province.PixelCount / 5
                || string.Equals(province.Terrain, "mountain", StringComparison.OrdinalIgnoreCase)
                || string.Equals(province.Terrain, "mountains", StringComparison.OrdinalIgnoreCase);

        private static int Count(ProvinceInfo province, BiomeKind biome)
            => province.BiomeCounts != null && (int)biome < province.BiomeCounts.Length
                ? province.BiomeCounts[(int)biome] : 0;

        public bool TryBuildGranary()
        {
            if (!CanBuildGranary) return false;
            data.Wood -= GranaryWood;
            data.Coin -= GranaryCoin;
            data.Granary = true;
            return true;
        }

        public bool TryRecruitWarband()
        {
            if (!CanRecruitWarband) return false;
            data.Wood -= WarbandWood;
            data.Coin -= WarbandCoin;
            data.Warband = true;
            return true;
        }

        public int TradeCost => HasTradeAccess(map.Provinces[Capital]) ? 20 : 30;

        public bool CanTradeWith(int province)
            => TradePartner == -1 && Coin >= TradeCost && CanReachIndependentProvince(province);

        public bool TryTradeForFood(int province)
        {
            if (!CanTradeWith(province)) return false;
            data.Coin -= TradeCost;
            data.TradePartner = province;
            AddFood(36);
            return true;
        }

        public bool CanStartIntegration(int province, ExpansionApproach approach)
        {
            if (IntegrationTarget != -1 || !CanReachIndependentProvince(province)) return false;
            return approach switch
            {
                ExpansionApproach.Diplomacy => Coin >= IntegrationCost,
                ExpansionApproach.Trade => TradePartner == province && Coin >= PactCost,
                ExpansionApproach.Force => HasWarband && Food >= ForceFood,
                _ => false
            };
        }

        public bool TryStartIntegration(int province, ExpansionApproach approach = ExpansionApproach.Diplomacy)
        {
            if (!CanStartIntegration(province, approach)) return false;
            if (approach == ExpansionApproach.Force) data.Food -= ForceFood;
            else data.Coin -= approach == ExpansionApproach.Trade ? PactCost : IntegrationCost;
            data.IntegrationTarget = province;
            data.IntegrationDaysLeft = DaysFor(approach);
            data.PendingApproach = approach;
            return true;
        }

        private static int DaysFor(ExpansionApproach approach) => approach switch
        {
            ExpansionApproach.Force => ForceDuration,
            ExpansionApproach.Trade => PactDuration,
            _ => IntegrationDuration
        };

        public int DaysToNextDecision
        {
            get
            {
                var days = Day < WinterStart ? WinterStart - Day : Day < WinterEnd ? WinterEnd - Day : 0;
                if (IntegrationTarget < 0) return days;
                return days == 0 ? IntegrationDaysLeft : Math.Min(days, IntegrationDaysLeft);
            }
        }

        public bool AdvanceToNextDecision()
        {
            var days = DaysToNextDecision;
            if (!Paused || days == 0) return false;
            Paused = false;
            var expanded = Advance(days / (double)Speed);
            Paused = true;
            return expanded;
        }

        // Returns true when a new province joins the tribe.
        public bool Advance(double seconds)
        {
            if (Paused || seconds <= 0d || double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
            var stop = Day < WinterStart ? WinterStart : Day < WinterEnd ? WinterEnd : int.MaxValue;
            var elapsed = Math.Min(fractionOfDay + seconds * Speed, stop - Day);
            var days = (int)elapsed;
            fractionOfDay = elapsed - days;
            var previousDay = Day;
            var destination = Day + days;
            var expanded = false;
            var harvest = Fertility(map.Provinces[Capital]);

            // Only the opening winter needs day-by-day ordering. After thaw, batch stable production.
            while (Day < destination && Day < WinterEnd)
            {
                data.Day++;
                expanded |= AdvanceIntegration(1);
                if (Day % 5 == 0)
                {
                    if (Day < WinterStart) AddFood(harvest - (HasWarband ? 1 : 0));
                    else if (Day > WinterStart) ConsumeWinterFood(WinterFoodPerPeriod);
                }
            }

            if (Day < destination)
            {
                expanded |= AdvanceIntegration(destination - Day);
                AddFood((long)(destination / 5 - Day / 5) * (harvest - (HasWarband ? 1 : 0)));
                data.Day = destination;
            }

            // Wood arrives slowly enough that the first building and a warband compete for it.
            data.Wood = Math.Min(ResourceLimit, Wood + Day / 15 - previousDay / 15);
            if (days != 0 && (Day == WinterStart || Day == WinterEnd)) Paused = true;
            return expanded;
        }

        private bool AdvanceIntegration(int days)
        {
            if (IntegrationTarget == -1) return false;
            data.IntegrationDaysLeft = Math.Max(0, IntegrationDaysLeft - days);
            if (IntegrationDaysLeft > 0) return false;

            var target = IntegrationTarget;
            if (PendingApproach == ExpansionApproach.Force)
            {
                LosePopulation(Capital, IsDefensible(map.Provinces[target]) ? 18 : 8);
                LosePopulation(target, 12);
                AddFood(38);
            }
            if (FirstExpansion == ExpansionApproach.None) data.FirstExpansion = PendingApproach;
            data.Owned[target] = true;
            data.IntegrationTarget = -1;
            data.PendingApproach = ExpansionApproach.None;
            return true;
        }

        private void ConsumeWinterFood(int amount)
        {
            var missing = Math.Max(0, amount - Food);
            AddFood(-amount);
            data.WinterShortfall += missing;
            if (missing > 0) LosePopulation(Capital, missing * 2);
        }

        private void AddFood(long amount) => data.Food = (int)Math.Min(ResourceLimit, Math.Max(0, Food + amount));

        private void LosePopulation(int province, int amount)
            => data.Pop[province] = Math.Max(Math.Min(1, data.Pop[province]), data.Pop[province] - amount);

        public static bool IsLand(ProvinceMap map, int province)
            => province >= 0 && province < map.Provinces.Length
                && string.Equals(map.Provinces[province].Type, "land", StringComparison.OrdinalIgnoreCase);

        private static int ProvinceAt(ProvinceMap map, int cell)
            => cell >= 0 && cell < map.ProvinceAt.Length ? map.ProvinceAt[cell] : -1;

        private static bool IsValidName(string name)
            => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxNameLength;

        private static bool IsValidSpeed(int speed) => speed == 1 || speed == 2 || speed == 4;

        // Include ordered province identities and the raster so reindexing or redrawing invalidates a save.
        public static string MapKey(ProvinceMap map, int seed)
        {
            ulong hash = 14695981039346656037UL;
            Mix(map.Width);
            Mix(map.Height);
            Mix(seed);
            Mix(map.Provinces.Length);
            foreach (var province in map.Provinces)
            {
                MixText(province.Id);
                MixText(province.Type);
            }
            foreach (var index in map.ProvinceAt) Mix(index);
            return hash.ToString("x16", CultureInfo.InvariantCulture);

            void Mix(int value)
            {
                unchecked { hash = (hash ^ (uint)value) * 1099511628211UL; }
            }

            void MixText(string value)
            {
                Mix(value?.Length ?? -1);
                if (value != null)
                    foreach (var character in value) Mix(character);
            }
        }
    }
}
