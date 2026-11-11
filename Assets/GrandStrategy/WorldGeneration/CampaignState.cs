// Runs the campaign independently of Unity rendering and input. The HUD queries this state and calls its
// Try methods to spend resources or change the map.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GrandStrategy.WorldGeneration
{
    internal enum ExpansionApproach { None, Diplomacy, Trade, Force }

    [Flags]
    internal enum SettlementBuilding { Farm = 1, LumberCamp = 2, IronMine = 4, Docks = 8 }

    internal enum TradeResource { Coin, Food, Wood, Iron }

    [Serializable]
    internal struct TradeOffer
    {
        public TradeResource Give, Want;
        public int GiveAmount, WantAmount;

        public TradeOffer(TradeResource give, int giveAmount, TradeResource want, int wantAmount)
        {
            Give = give;
            GiveAmount = giveAmount;
            Want = want;
            WantAmount = wantAmount;
        }
    }

    [Serializable]
    internal struct TradeAgreement
    {
        public int Province;
        public TradeOffer Offer;
        public int LastAttemptDay;
        public string LastIssue;
    }

    [Serializable]
    internal struct NeighbourMarket
    {
        public int Coin, Food, Wood, Iron;
        public int Period;
        public bool Traded;
    }

    [Serializable]
    internal enum ClanKind { Legacy, Hearthkeepers }

    internal sealed class CampaignSave
    {
        public int Version;
        public string MapKey;
        public string TribeName;
        public ClanKind Clan;
        public int TaxBonusRemainder;
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
        public bool IronWarband;
        public int FoodReserve;
        public List<TradeAgreement> Agreements;
        public long TaxCollected, TradeReceipts, TradeSpending;
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
        public const int IronWarbandIron = 10;
        public const int IronWarbandWood = 15;
        public const int IronWarbandCoin = 25;
        private const int DefaultFoodReserve = 10;
        public const int TaxInterval = 15;
        public const int ProductionInterval = 5;
        public const int FoodInterval = 15;
        private const int PeoplePerFood = 125;
        private const double MaxGrowthRate = .02;
        private const double MaxStarvationRate = .10;
        private const int SaveVersion = 6;
        private const int HearthkeeperTaxBonus = 5;
        private const SettlementBuilding AllBuildings =
            SettlementBuilding.Farm | SettlementBuilding.LumberCamp | SettlementBuilding.IronMine | SettlementBuilding.Docks;
        private const int StartingFood = 40;
        private const int StartingWood = 45;
        private const int ResourceLimit = 999;
        public const int PactCost = 12;
        private const int PactDuration = 20;
        private const int ForceDuration = 10;

        private readonly ProvinceMap map;
        private readonly CampaignSave data;
        private double fractionOfDay;
        private HashSet<int> dockWaterways;

        private CampaignState(ProvinceMap map, CampaignSave data)
        {
            this.map = map;
            this.data = data;
        }

        public string TribeName => data.TribeName;
        public ClanKind Clan => data.Clan;
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
        private long BaseTaxIncome => Math.Min(ResourceLimit, (OwnedPopulation + 99) / 100);
        public long TaxIncome => BaseTaxIncome + TaxBonusNumerator(BaseTaxIncome) / 100;

        private long TaxBonusNumerator(long tax)
            => Clan == ClanKind.Hearthkeepers ? tax * HearthkeeperTaxBonus + data.TaxBonusRemainder : 0;
        public long TaxCollected => data.TaxCollected;
        public long TradeReceipts => data.TradeReceipts;
        public long TradeSpending => data.TradeSpending;
        public int FoodReserve => data.FoodReserve;
        public IReadOnlyList<TradeAgreement> TradeAgreements => data.Agreements;
        public int TradeCapacity => 1 + BuildingTotal(SettlementBuilding.Docks);
        public bool HasGranary => data.Granary;
        public bool HasWarband => data.Warband;
        public bool HasIronWarband => data.IronWarband;
        public bool CanUpgradeWarband => HasWarband && !HasIronWarband
            && Iron >= IronWarbandIron && Wood >= IronWarbandWood && Coin >= IronWarbandCoin;
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
        private int FarmFood => BuildingTotal(SettlementBuilding.Farm);
        public int SubsistenceFood(int province) => province == Capital ? Fertility(map.Provinces[province]) : 1;

        private int WarmFoodOutput
        {
            get
            {
                var food = FarmFood - (HasWarband ? 1 : 0);
                for (var province = 0; province < data.Owned.Length; province++)
                    if (Owns(province)) food += SubsistenceFood(province);
                return food;
            }
        }
        public ExpansionApproach FirstExpansion => data.FirstExpansion;
        public ExpansionApproach PendingApproach => data.PendingApproach;
        public bool CanBuildGranary => !HasGranary && Wood >= GranaryWood && Coin >= GranaryCoin;
        public bool CanRecruitWarband => !HasWarband && Wood >= WarbandWood && Coin >= WarbandCoin;

        public bool Owns(int province) => province >= 0 && province < data.Owned.Length && data.Owned[province];
        public int Population(int province) => data.Pop[province];
        public CampaignSave ToSave() => data;

        // Seed the capital and initial resources, while also creating population entries for independent
        // land provinces.
        public static CampaignState Found(ProvinceMap map, string mapKey, int capitalCell, string name, int colour,
            ClanKind clan = ClanKind.Legacy)
        {
            if (clan != ClanKind.Legacy && clan != ClanKind.Hearthkeepers) throw new ArgumentOutOfRangeException(nameof(clan));
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
                Clan = clan,
                Colour = colour,
                CapitalCell = capitalCell,
                Day = 1,
                Speed = 1,
                Coin = clan == ClanKind.Hearthkeepers ? StartingCoin * 6 / 5 : StartingCoin,
                Owned = owned,
                Buildings = new int[owned.Length],
                Markets = new NeighbourMarket[owned.Length],
                Agreements = new List<TradeAgreement>(),
                FoodReserve = DefaultFoodReserve,
                Pop = pop,
                IntegrationTarget = -1,
                TradePartner = -1,
                Food = clan == ClanKind.Hearthkeepers ? StartingFood * 6 / 5 : StartingFood,
                Wood = clan == ClanKind.Hearthkeepers ? StartingWood * 6 / 5 : StartingWood
            });
        }

        // Validate the saved arrays and references against the current map before restoring or upgrading
        // the save format.
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

            if (save.Version < 6)
            {
                save.Clan = ClanKind.Legacy;
                save.TaxBonusRemainder = 0;
            }
            else if ((save.Clan != ClanKind.Legacy && save.Clan != ClanKind.Hearthkeepers)
                || save.TaxBonusRemainder < 0 || save.TaxBonusRemainder >= 100
                || (save.Clan == ClanKind.Legacy && save.TaxBonusRemainder != 0))
                throw new ArgumentException("The save contains invalid clan data.");

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
            }
            if (save.Version < 5)
            {
                save.Agreements = new List<TradeAgreement>();
                save.FoodReserve = DefaultFoodReserve;
            }
            else
                state.ValidateTradeAgreements();
            save.Version = SaveVersion;
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

        // Turn a province's biome mix into a small production rating shared by farming and local trade.
        public static int Fertility(ProvinceInfo province)
        {
            var fertile = Count(province, BiomeKind.Grassland) + Count(province, BiomeKind.TemperateForest)
                + Count(province, BiomeKind.TropicalForest);
            return fertile > 0 && fertile * 2L >= province.PixelCount ? 3
                : Count(province, BiomeKind.Desert) + Count(province, BiomeKind.Tundra)
                    + Count(province, BiomeKind.Alpine) > province.PixelCount / 2 ? 1 : 2;
        }

        public static bool HasTradeAccess(ProvinceInfo province)
            => province.TradeWaterways != null ? province.TradeWaterways.Length > 0
                : province.RiverBankEdges > 0 || province.RiverSamples > 0 || province.TouchesOpenSea;

        private static bool IsMountain(ProvinceInfo province)
            => Count(province, BiomeKind.Alpine) > province.PixelCount / 5
                || string.Equals(province.Terrain, "mountain", StringComparison.OrdinalIgnoreCase)
                || string.Equals(province.Terrain, "mountains", StringComparison.OrdinalIgnoreCase);

        private static bool IsTundra(ProvinceInfo province)
        {
            var tundra = Count(province, BiomeKind.Tundra);
            if (tundra == 0) return string.Equals(province.Terrain, "tundra", StringComparison.OrdinalIgnoreCase);
            for (var biome = (int)BiomeKind.Wetland; biome <= (int)BiomeKind.Alpine; biome++)
                if (Count(province, (BiomeKind)biome) > tundra) return false;
            return true;
        }

        public static bool IsDefensible(ProvinceInfo province) => IsMountain(province);

        private static int Count(ProvinceInfo province, BiomeKind biome)
            => province.BiomeCounts != null && (int)biome < province.BiomeCounts.Length
                ? province.BiomeCounts[(int)biome] : 0;

        // Keep costs, output and terrain restrictions in one place so the HUD and build validation agree.
        public (string Name, int Coin, int Wood, int Yield, string Resource, string SiteIssue) BuildingInfo(
            int province, SettlementBuilding building)
            => BuildingInfo(map.Provinces[province], building);

        internal static (string Name, int Coin, int Wood, int Yield, string Resource, string SiteIssue) BuildingInfo(
            ProvinceInfo site, SettlementBuilding building)
        {
            return building switch
            {
                SettlementBuilding.Farm => ("Farm", 25, 15, Fertility(site), "food",
                    IsTundra(site) ? "Farms cannot be built in tundra." : null),
                SettlementBuilding.LumberCamp => ("Lumber camp", 20, 10, TimberCoverage(site) * 2L >= site.PixelCount ? 3 : 2, "wood",
                    HasTimber(site) ? null : "Requires more than 25% forest cover."),
                SettlementBuilding.IronMine => ("Iron mine", 35, 20, Count(site, BiomeKind.Alpine) * 2L >= site.PixelCount ? 2 : 1, "iron",
                    IsMountain(site) ? null : "Requires mountainous terrain."),
                SettlementBuilding.Docks => ("Docks", 25, 25, 1, null,
                    HasTradeAccess(site) ? null : "Requires an ocean coast or river bank."),
                _ => throw new ArgumentOutOfRangeException(nameof(building))
            };
        }

        public bool HasBuilding(int province, SettlementBuilding building)
            => Owns(province) && (data.Buildings[province] & (int)building) != 0;

        public bool CanBuild(int province, SettlementBuilding building)
        {
            if (!Owns(province) || HasBuilding(province, building)) return false;
            var info = BuildingInfo(province, building);
            return info.SiteIssue == null && Coin >= info.Coin && Wood >= info.Wood;
        }

        public bool TryBuild(int province, SettlementBuilding building)
        {
            if (!CanBuild(province, building)) return false;
            var info = BuildingInfo(province, building);
            data.Coin -= info.Coin;
            data.Wood -= info.Wood;
            data.Buildings[province] |= (int)building;
            if (building == SettlementBuilding.Docks) dockWaterways = null;
            return true;
        }

        private int BuildingTotal(SettlementBuilding building)
        {
            var total = 0;
            for (var province = 0; province < data.Buildings.Length; province++)
                if (HasBuilding(province, building)) total += BuildingInfo(province, building).Yield;
            return total;
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

        public bool TryUpgradeWarband()
        {
            if (!CanUpgradeWarband) return false;
            data.Iron -= IronWarbandIron;
            data.Wood -= IronWarbandWood;
            data.Coin -= IronWarbandCoin;
            data.IronWarband = true;
            return true;
        }

        // Direct neighbours qualify over land. Docks extend access to independent provinces sharing a
        // waterway.
        public bool CanTradeWith(int province)
        {
            if (CanReachIndependentProvince(province)) return true;
            if (!IsLand(map, province) || Owns(province)) return false;
            var waterways = map.Provinces[province].TradeWaterways;
            if (waterways == null || waterways.Length == 0) return false;

            if (dockWaterways == null)
            {
                dockWaterways = new HashSet<int>();
                for (var port = 0; port < map.Provinces.Length; port++)
                    if (HasBuilding(port, SettlementBuilding.Docks) && map.Provinces[port].TradeWaterways != null)
                        dockWaterways.UnionWith(map.Provinces[port].TradeWaterways);
            }
            foreach (var route in waterways)
                if (dockWaterways.Contains(route)) return true;
            return false;
        }

        public void SetFoodReserve(int reserve) => data.FoodReserve = Math.Max(0, Math.Min(ResourceLimit, reserve));

        public string AgreementIssue(int province, TradeOffer offer)
        {
            if (data.Agreements.Exists(agreement => agreement.Province == province))
                return "There is already an agreement with this neighbour.";
            if (data.Agreements.Count >= TradeCapacity)
                return "No agreement slots available. Build docks to add one.";
            return EvaluateTrade(province, offer).Status;
        }

        public bool TryStartTradeAgreement(int province, TradeOffer offer)
        {
            if (AgreementIssue(province, offer) != null) return false;
            data.Agreements.Add(new TradeAgreement { Province = province, Offer = offer });
            return true;
        }

        public void CancelTradeAgreement(int province)
            => data.Agreements.RemoveAll(agreement => agreement.Province == province);

        // Recheck each recurring offer at delivery time and record any issue for the HUD. Food exports must
        // leave enough for the reserve and next upkeep.
        private void ProcessTradeAgreements()
        {
            for (var i = 0; i < data.Agreements.Count; i++)
            {
                var agreement = data.Agreements[i];
                agreement.LastAttemptDay = Day;
                agreement.LastIssue = EvaluateTrade(agreement.Province, agreement.Offer).Status;
                if (agreement.LastIssue == null && agreement.Offer.Give == TradeResource.Food
                    && Food - agreement.Offer.GiveAmount < FoodUpkeep + FoodReserve)
                    agreement.LastIssue = "Protected food reserve.";
                if (agreement.LastIssue == null)
                    ExecuteTrade(agreement.Province, agreement.Offer);
                data.Agreements[i] = agreement;
            }
        }

        private void ValidateTradeAgreements()
        {
            if (!ValidStock(FoodReserve) || data.Agreements == null || data.Agreements.Count > TradeCapacity
                || TaxCollected < 0 || TradeReceipts < 0 || TradeSpending < 0 || (HasIronWarband && !HasWarband))
                throw new ArgumentException("The save contains invalid treasury or trade agreement data.");

            var partners = new HashSet<int>();
            foreach (var agreement in data.Agreements)
                if (!IsLand(map, agreement.Province) || Owns(agreement.Province) || !ValidTradeOffer(agreement.Offer)
                    || !partners.Add(agreement.Province) || agreement.LastAttemptDay < 0
                    || agreement.LastAttemptDay > Day)
                    throw new ArgumentException("The save contains an invalid trade agreement.");
        }

        public bool HasTradedWith(int province) => province >= 0 && province < data.Markets.Length
            && data.Markets[province].Traded;

        // Quote the deal without transferring goods. Willingness and available stock are separate checks.
        public (int Willingness, string Status, int Stock) EvaluateTrade(int province, TradeOffer offer)
        {
            if (!CanTradeWith(province)) return (0, "Choose an independent community within trade reach.", 0);
            if (!ValidTradeOffer(offer))
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
            if (EvaluateTrade(province, offer).Status != null) return false;
            ExecuteTrade(province, offer);
            return true;
        }

        // Update both inventories after validation and record coin flows separately from tax income.
        private void ExecuteTrade(int province, TradeOffer offer)
        {
            var market = MarketAt(province);
            SetMarketBalance(ref market, offer.Give, MarketBalance(market, offer.Give) + offer.GiveAmount);
            SetMarketBalance(ref market, offer.Want, MarketBalance(market, offer.Want) - offer.WantAmount);
            market.Traded = true;
            data.Markets[province] = market;
            AdjustResource(offer.Give, -offer.GiveAmount);
            AdjustResource(offer.Want, offer.WantAmount);
            if (offer.Want == TradeResource.Coin) RecordCoin(ref data.TradeReceipts, offer.WantAmount);
            if (offer.Give == TradeResource.Coin) RecordCoin(ref data.TradeSpending, offer.GiveAmount);
        }

        // Compute the province's replenished market for the current period. Looking at a quote alone does
        // not commit that snapshot.
        private NeighbourMarket MarketAt(int province)
        {
            var market = data.Markets[province];
            var period = Day / FoodInterval + 1;
            if (market.Period == period) return market;
            var info = map.Provinces[province];
            var coin = HasTradeAccess(info) ? 36 : 24;
            var food = 8 + 8 * Fertility(info);
            var wood = HasTimber(info) ? 28 : 12;
            var iron = IsMountain(info) ? 16 : 0;
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

        private static int TimberCoverage(ProvinceInfo info)
            => Count(info, BiomeKind.TemperateForest) + Count(info, BiomeKind.TropicalForest)
                + Count(info, BiomeKind.BorealForest);

        private static bool HasTimber(ProvinceInfo info) => TimberCoverage(info) > info.PixelCount / 4;

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

        private static bool ValidTradeOffer(TradeOffer offer)
            => (uint)offer.Give <= (uint)TradeResource.Iron && (uint)offer.Want <= (uint)TradeResource.Iron
                && offer.Give != offer.Want && offer.GiveAmount > 0 && offer.WantAmount > 0
                && ValidStock(offer.GiveAmount) && ValidStock(offer.WantAmount);

        private static bool ValidStock(int amount) => amount >= 0 && amount <= ResourceLimit;

        private static void RecordCoin(ref long total, int amount)
            => total += Math.Min(long.MaxValue - total, amount);

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

        // Pay the chosen expansion cost up front and schedule the ownership change for a later simulation
        // day.
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

        // Fast-forward to the next scheduled milestone, then return control to the player with time paused.
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
        // Carry fractional days between calls so frame rate does not determine the campaign clock.
        public bool Advance(double seconds)
        {
            if (Paused || seconds <= 0d || double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
            var stop = Day < WinterStart ? WinterStart : Day < WinterEnd ? WinterEnd : int.MaxValue;
            var elapsed = Math.Min(fractionOfDay + seconds * Speed, stop - Day);
            var days = (int)elapsed;
            fractionOfDay = elapsed - days;
            var destination = Day + days;
            var expanded = false;

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
                ApplyEconomy(previousDay);
            }

            if (days != 0 && (Day == WinterStart || Day == WinterEnd)) Paused = true;
            return expanded;
        }

        // Apply production, deliveries, upkeep and taxes in that order. Taxes use the population remaining
        // after food upkeep.
        private void ApplyEconomy(int previousDay)
        {
            var periods = Day / ProductionInterval - previousDay / ProductionInterval;
            var taxes = Day / TaxInterval - previousDay / TaxInterval;
            if (periods > 0)
            {
                var output = Day > WinterStart && Day <= WinterEnd ? WinterFoodOutput : WarmFoodOutput;
                if (Day == WinterStart) AddFood((long)(periods - 1) * output + FarmFood);
                else AddFood((long)periods * output);

                var lumber = BuildingTotal(SettlementBuilding.LumberCamp);
                var iron = BuildingTotal(SettlementBuilding.IronMine);
                data.Wood = (int)Math.Min(ResourceLimit, Wood + (long)periods * lumber + taxes);
                data.Iron = (int)Math.Min(ResourceLimit, Iron + (long)periods * iron);
            }
            if (Day / FoodInterval > previousDay / FoodInterval)
            {
                ProcessTradeAgreements();
                FeedPopulation();
            }
            if (taxes > 0)
            {
                var baseTax = taxes * BaseTaxIncome;
                var bonus = TaxBonusNumerator(baseTax);
                data.TaxBonusRemainder = (int)(bonus % 100);
                var collected = (int)Math.Min(ResourceLimit - Coin, baseTax + bonus / 100);
                data.Coin += collected;
                RecordCoin(ref data.TaxCollected, collected);
            }
        }

        // Scale growth or losses by how much food is left or missing after feeding the owned population.
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

        // Resolve the chosen approach, transfer the province and end any trade agreement with the former
        // independent market.
        private bool AdvanceIntegration(int days)
        {
            if (IntegrationTarget == -1) return false;
            data.IntegrationDaysLeft = Math.Max(0, IntegrationDaysLeft - days);
            if (IntegrationDaysLeft > 0) return false;

            var target = IntegrationTarget;
            if (PendingApproach == ExpansionApproach.Force)
            {
                var losses = IsDefensible(map.Provinces[target]) ? 18 : 8;
                LosePopulation(Capital, HasIronWarband ? losses / 2 : losses);
                LosePopulation(target, 12);
                AddFood(38);
            }
            if (FirstExpansion == ExpansionApproach.None) data.FirstExpansion = PendingApproach;
            data.Owned[target] = true;
            CancelTradeAgreement(target);
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
        // Include the seed, dimensions, province identities and raster so saves bind to the same campaign
        // geography.
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
