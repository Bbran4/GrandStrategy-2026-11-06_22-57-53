using System;
using System.Globalization;

namespace GrandStrategy.WorldGeneration
{
    internal enum ExpansionApproach { None, Diplomacy, Trade, Force }

    [Flags]
    internal enum SettlementBuilding { Farm = 1, LumberCamp = 2, IronMine = 4 }

    internal enum TradeResource { Coin, Food, Wood, Iron }

    internal readonly struct TradeOffer
    {
        public readonly TradeResource Give, Want;
        public readonly int GiveAmount, WantAmount;

        public TradeOffer(TradeResource give, int giveAmount, TradeResource want, int wantAmount)
        {
            Give = give;
            GiveAmount = giveAmount;
            Want = want;
            WantAmount = wantAmount;
        }
    }

    [Serializable]
    internal struct NeighbourMarket
    {
        public int Coin, Food, Wood, Iron;
        public int Period;
        public bool Traded;
    }

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
        public int Iron;
        public int[] Buildings;
        public bool Granary;
        public bool Warband;
        public int TradePartner; // Legacy single partner for saves through version 3.
        public NeighbourMarket[] Markets;
        public long LastPopulationChange;
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
        public const int TaxInterval = 15;
        public const int ProductionInterval = 5;
        public const int FoodInterval = 15;
        private const int PeoplePerFood = 125;
        private const double MaxGrowthRate = .02;
        private const double MaxStarvationRate = .10;
        private const int SaveVersion = 4;
        private const SettlementBuilding AllBuildings =
            SettlementBuilding.Farm | SettlementBuilding.LumberCamp | SettlementBuilding.IronMine;
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
        public int Iron => data.Iron;
        public long OwnedPopulation
        {
            get
            {
                long total = 0;
                for (var i = 0; i < data.Pop.Length; i++)
                    if (Owns(i)) total += data.Pop[i];
                return total;
            }
        }
        public long TaxIncome => (OwnedPopulation + 99) / 100;
        public bool HasGranary => data.Granary;
        public bool HasWarband => data.Warband;
        public long FoodUpkeep => (OwnedPopulation + PeoplePerFood - 1) / PeoplePerFood;
        public long LastPopulationChange => data.LastPopulationChange;
        public int WinterShortfall => data.WinterShortfall;
        public long WinterFoodRemaining
        {
            get
            {
                var from = Math.Max(Day, WinterStart);
                if (from >= WinterEnd) return 0;
                var meals = WinterEnd / FoodInterval - from / FoodInterval;
                var production = WinterEnd / ProductionInterval - from / ProductionInterval;
                return Math.Max(0, meals * FoodUpkeep - (long)production * WinterFoodOutput);
            }
        }
        private int WinterFoodOutput => FarmFood - (HasGranary ? 0 : 2) - (HasWarband ? 1 : 0);
        private int FarmFood => BuildingCount(SettlementBuilding.Farm) * BuildingInfo(SettlementBuilding.Farm).Yield;
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
                Buildings = new int[owned.Length],
                Markets = new NeighbourMarket[owned.Length],
                Pop = pop,
                IntegrationTarget = -1,
                TradePartner = -1,
                Food = StartingFood,
                Wood = StartingWood
            });
        }

        public static CampaignState Restore(ProvinceMap map, string mapKey, CampaignSave save, int colourCount)
        {
            if (save == null || (save.Version < 1 || save.Version > SaveVersion) || save.MapKey != mapKey)
                throw new ArgumentException("The save version or authored map does not match.");
            var capital = ProvinceAt(map, save.CapitalCell);
            if (!IsLand(map, capital) || !IsValidName(save.TribeName) || save.Colour < 0 || save.Colour >= colourCount
                || save.Day < 1 || !IsValidSpeed(save.Speed) || save.Coin < 0 || save.Coin > (save.Version < 3 ? StartingCoin : ResourceLimit)
                || save.Owned?.Length != map.Provinces.Length || save.Pop?.Length != map.Provinces.Length
                || !save.Owned[capital])
                throw new ArgumentException("The save contains invalid campaign data.");

            for (var i = 0; i < save.Pop.Length; i++)
                if (save.Pop[i] < 0 || (!IsLand(map, i) && (save.Pop[i] != 0 || save.Owned[i])))
                    throw new ArgumentException("The save contains invalid province population or ownership.");

            if (save.Version >= 3)
            {
                if (save.Iron < 0 || save.Iron > ResourceLimit || save.Buildings?.Length != map.Provinces.Length)
                    throw new ArgumentException("The save contains invalid building or iron data.");
                for (var i = 0; i < save.Buildings.Length; i++)
                    if ((save.Buildings[i] & ~(int)AllBuildings) != 0 || (save.Buildings[i] != 0 && !save.Owned[i]))
                        throw new ArgumentException("The save contains invalid province buildings.");
            }

            if (save.Version >= 4)
            {
                if (save.Markets?.Length != map.Provinces.Length)
                    throw new ArgumentException("The save contains invalid neighbour markets.");
                foreach (var market in save.Markets)
                    if (market.Period < 0 || market.Period > save.Day / FoodInterval + 1
                        || (market.Traded && market.Period == 0)
                        || !ValidStock(market.Coin) || !ValidStock(market.Food)
                        || !ValidStock(market.Wood) || !ValidStock(market.Iron))
                        throw new ArgumentException("The save contains invalid neighbour stock.");
            }

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
                save.Food = StartingFood;
                save.Wood = StartingWood;
                save.TradePartner = -1;
                save.PendingApproach = pending;
            }
            else if (save.Food < 0 || save.Food > ResourceLimit || save.Wood < 0 || save.Wood > ResourceLimit
                || save.WinterShortfall < 0
                || (save.Version < 4 && save.TradePartner != -1 && !IsLand(map, save.TradePartner))
                || (pending == ExpansionApproach.Trade && (save.Version >= 4
                    ? !state.HasTradedWith(target) : save.TradePartner != target))
                || (pending == ExpansionApproach.Force && !save.Warband)
                || !Enum.IsDefined(typeof(ExpansionApproach), save.FirstExpansion))
                throw new ArgumentException("The save contains invalid settlement data.");

            if (save.Version < 3)
            {
                save.Buildings = new int[map.Provinces.Length];
                save.Iron = 0;
            }
            if (save.Version < 4)
            {
                save.Markets = new NeighbourMarket[map.Provinces.Length];
                if (save.TradePartner >= 0)
                {
                    var market = state.MarketAt(save.TradePartner);
                    market.Traded = true;
                    save.Markets[save.TradePartner] = market;
                }
                save.TradePartner = -1;
                save.Version = SaveVersion;
            }
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

        public static (string Name, int Coin, int Wood, int Yield, string Resource) BuildingInfo(SettlementBuilding building)
            => building switch
            {
                SettlementBuilding.Farm => ("Farm", 25, 15, 3, "food"),
                SettlementBuilding.LumberCamp => ("Lumber camp", 20, 10, 2, "wood"),
                SettlementBuilding.IronMine => ("Iron mine", 35, 20, 1, "iron"),
                _ => throw new ArgumentOutOfRangeException(nameof(building))
            };

        public bool HasBuilding(int province, SettlementBuilding building)
            => Owns(province) && (data.Buildings[province] & (int)building) != 0;

        public bool CanBuild(int province, SettlementBuilding building)
        {
            var info = BuildingInfo(building);
            return Owns(province) && !HasBuilding(province, building) && Coin >= info.Coin && Wood >= info.Wood;
        }

        public bool TryBuild(int province, SettlementBuilding building)
        {
            if (!CanBuild(province, building)) return false;
            var info = BuildingInfo(building);
            data.Coin -= info.Coin;
            data.Wood -= info.Wood;
            data.Buildings[province] |= (int)building;
            return true;
        }

        private int BuildingCount(SettlementBuilding building)
        {
            var count = 0;
            for (var province = 0; province < data.Buildings.Length; province++)
                if (HasBuilding(province, building)) count++;
            return count;
        }

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

        public bool HasTradedWith(int province) => province >= 0 && province < data.Markets.Length
            && data.Markets[province].Traded;

        public (int Willingness, string Status, int Stock) EvaluateTrade(int province, TradeOffer offer)
        {
            if (!CanReachIndependentProvince(province)) return (0, "Choose an independent neighbour.", 0);
            if ((uint)offer.Give > (uint)TradeResource.Iron || (uint)offer.Want > (uint)TradeResource.Iron
                || offer.Give == offer.Want || !ValidStock(offer.GiveAmount) || !ValidStock(offer.WantAmount)
                || offer.GiveAmount == 0 || offer.WantAmount == 0)
                return (0, "Choose different goods and enter positive amounts.", 0);

            var market = MarketAt(province);
            var stock = MarketBalance(market, offer.Want);
            var info = map.Provinces[province];
            var offered = offer.GiveAmount * TradeValue(info, offer.Give) * 100;
            var requested = offer.WantAmount * TradeValue(info, offer.Want)
                * (HasTradeAccess(info) ? 110 : 120);
            var willingness = Math.Min(100, offered * 100 / requested);

            if (offer.WantAmount > stock) return (0, "This neighbour cannot supply that amount.", stock);
            if (offer.GiveAmount > ResourceLimit - MarketBalance(market, offer.Give))
                return (0, "This neighbour has no room for that offer.", stock);
            if (offer.GiveAmount > ResourceBalance(offer.Give))
                return (willingness, "You cannot afford this offer.", stock);
            if (offer.WantAmount > ResourceLimit - ResourceBalance(offer.Want))
                return (willingness, "You have no room for those goods.", stock);
            return (willingness, willingness == 100 ? null : "Offer more or request less.", stock);
        }

        public bool TryTrade(int province, TradeOffer offer)
        {
            var evaluation = EvaluateTrade(province, offer);
            if (evaluation.Status != null) return false;
            var market = MarketAt(province);
            SetMarketBalance(ref market, offer.Give, MarketBalance(market, offer.Give) + offer.GiveAmount);
            SetMarketBalance(ref market, offer.Want, MarketBalance(market, offer.Want) - offer.WantAmount);
            market.Traded = true;
            data.Markets[province] = market;
            AdjustResource(offer.Give, -offer.GiveAmount);
            AdjustResource(offer.Want, offer.WantAmount);
            return true;
        }

        private NeighbourMarket MarketAt(int province)
        {
            var market = data.Markets[province];
            var period = Day / FoodInterval + 1;
            if (market.Period == period) return market;
            var info = map.Provinces[province];
            var coin = HasTradeAccess(info) ? 36 : 24;
            var food = 8 + 8 * Fertility(info);
            var wood = HasTimber(info) ? 28 : 12;
            var iron = IsDefensible(info) ? 16 : 6;
            var elapsed = period - market.Period;
            return new NeighbourMarket
            {
                Period = period,
                Traded = market.Traded,
                Coin = Replenish(market.Coin, coin, elapsed),
                Food = Replenish(market.Food, food, elapsed),
                Wood = Replenish(market.Wood, wood, elapsed),
                Iron = Replenish(market.Iron, iron, elapsed)
            };

            int Replenish(int stock, int target, int periods)
            {
                if (market.Period == 0) return target;
                var change = Math.Max(1, target / 4) * periods;
                return stock + Math.Max(-change, Math.Min(change, target - stock));
            }
        }

        private static bool HasTimber(ProvinceInfo info)
            => Count(info, BiomeKind.TemperateForest) + Count(info, BiomeKind.TropicalForest)
                + Count(info, BiomeKind.BorealForest) > info.PixelCount / 4;

        private static int TradeValue(ProvinceInfo info, TradeResource resource) => resource switch
        {
            TradeResource.Coin => 2,
            TradeResource.Food => 5 - Fertility(info),
            TradeResource.Wood => HasTimber(info) ? 2 : 4,
            TradeResource.Iron => IsDefensible(info) ? 4 : 6,
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };

        private static int MarketBalance(NeighbourMarket market, TradeResource resource) => resource switch
        {
            TradeResource.Coin => market.Coin,
            TradeResource.Food => market.Food,
            TradeResource.Wood => market.Wood,
            TradeResource.Iron => market.Iron,
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };

        private static void SetMarketBalance(ref NeighbourMarket market, TradeResource resource, int amount)
        {
            switch (resource)
            {
                case TradeResource.Coin: market.Coin = amount; break;
                case TradeResource.Food: market.Food = amount; break;
                case TradeResource.Wood: market.Wood = amount; break;
                case TradeResource.Iron: market.Iron = amount; break;
            }
        }

        private int ResourceBalance(TradeResource resource) => resource switch
        {
            TradeResource.Coin => Coin,
            TradeResource.Food => Food,
            TradeResource.Wood => Wood,
            TradeResource.Iron => Iron,
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };

        private void AdjustResource(TradeResource resource, int change)
        {
            switch (resource)
            {
                case TradeResource.Coin: data.Coin += change; break;
                case TradeResource.Food: data.Food += change; break;
                case TradeResource.Wood: data.Wood += change; break;
                case TradeResource.Iron: data.Iron += change; break;
            }
        }

        private static bool ValidStock(int amount) => amount >= 0 && amount <= ResourceLimit;

        public bool CanStartIntegration(int province, ExpansionApproach approach)
        {
            if (IntegrationTarget != -1 || !CanReachIndependentProvince(province)) return false;
            return approach switch
            {
                ExpansionApproach.Diplomacy => Coin >= IntegrationCost,
                ExpansionApproach.Trade => HasTradedWith(province) && Coin >= PactCost,
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
            var destination = Day + days;
            var expanded = false;
            var harvest = Fertility(map.Provinces[Capital]) - (HasWarband ? 1 : 0);

            // Settle production and population upkeep in calendar order, including during fast-forward.
            while (Day < destination)
            {
                var previousDay = Day;
                var interval = Day >= WinterStart && Day < WinterEnd ? ProductionInterval : FoodInterval;
                var span = Math.Min(destination - Day, interval - Day % interval);
                if (IntegrationTarget >= 0 && IntegrationDaysLeft <= span)
                    span = Math.Max(1, IntegrationDaysLeft - 1);
                data.Day += span;
                expanded |= AdvanceIntegration(span);
                ApplyEconomy(previousDay, harvest);
            }

            if (days != 0 && (Day == WinterStart || Day == WinterEnd)) Paused = true;
            return expanded;
        }

        private void ApplyEconomy(int previousDay, int harvest)
        {
            var periods = Day / ProductionInterval - previousDay / ProductionInterval;
            var taxes = Day / TaxInterval - previousDay / TaxInterval;
            if (periods > 0)
            {
                var output = Day > WinterStart && Day <= WinterEnd ? WinterFoodOutput : FarmFood + harvest;
                if (Day == WinterStart) AddFood((long)(periods - 1) * output + FarmFood);
                else AddFood((long)periods * output);

                var lumber = BuildingCount(SettlementBuilding.LumberCamp) * BuildingInfo(SettlementBuilding.LumberCamp).Yield;
                var iron = BuildingCount(SettlementBuilding.IronMine) * BuildingInfo(SettlementBuilding.IronMine).Yield;
                data.Wood = (int)Math.Min(ResourceLimit, Wood + (long)periods * lumber + taxes);
                data.Iron = (int)Math.Min(ResourceLimit, Iron + (long)periods * iron);
            }
            if (Day / FoodInterval > previousDay / FoodInterval)
                FeedPopulation();
            if (taxes > 0)
                data.Coin = (int)Math.Min(ResourceLimit, Coin + taxes * Math.Min(ResourceLimit, TaxIncome));
        }

        private void FeedPopulation()
        {
            var upkeep = FoodUpkeep;
            data.LastPopulationChange = 0;
            if (upkeep == 0) return;

            var balance = Food - upkeep;
            AddFood(-upkeep);
            if (balance == 0) return;

            var starving = balance < 0;
            if (starving && Day > WinterStart && Day <= WinterEnd)
                data.WinterShortfall = (int)Math.Min(int.MaxValue, data.WinterShortfall - balance);
            var rate = (starving ? MaxStarvationRate : MaxGrowthRate)
                * Math.Min(1d, Math.Abs((double)balance) / upkeep);
            for (var province = 0; province < data.Pop.Length; province++)
            {
                var population = data.Pop[province];
                if (!Owns(province) || population == 0) continue;
                var change = (int)Math.Ceiling(population * rate);
                change = starving ? -change : Math.Min(change, int.MaxValue - population);
                data.Pop[province] += change;
                data.LastPopulationChange += change;
            }
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
