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

    internal enum IronSupplyRoute { None, Local, Overland, Waterway }

    internal enum WarbandSupplyPolicy { Standard, Forage, Provisioned }

    [Flags]
    internal enum CampaignTechnology { Agronomy = 1, MarchLogistics = 2 }

    [Flags]
    internal enum CampaignPerk { Stewardship = 1, TownPlanning = 2, Envoys = 4 }

    internal enum ClanTradition { Standard, River, Highland }

    internal enum TaxPolicy { Standard, Relief, Heavy }

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
    internal struct SettlementConstruction
    {
        public int Cell;
        public int Buildings;
        public int IronStock;
        public int IronInTransit;
        public int IronArrivalDay;
        public int IronDepartureDay;
        public int IronHoldDay;
        public int IronTravelEndDay;
        public IronSupplyRoute IronTransport;
        public int[] IronTravelPath;
        public int FarmSite, LumberSite, MineSite, DockSite; // Cell index plus one.

        public int Placement(SettlementBuilding building) => building switch
        {
            SettlementBuilding.Farm => FarmSite,
            SettlementBuilding.LumberCamp => LumberSite,
            SettlementBuilding.IronMine => MineSite,
            SettlementBuilding.Docks => DockSite,
            _ => 0
        };

        public void SetPlacement(SettlementBuilding building, int cell)
        {
            switch (building)
            {
                case SettlementBuilding.Farm: FarmSite = cell + 1; break;
                case SettlementBuilding.LumberCamp: LumberSite = cell + 1; break;
                case SettlementBuilding.IronMine: MineSite = cell + 1; break;
                case SettlementBuilding.Docks: DockSite = cell + 1; break;
            }
        }
    }

    [Serializable]
    internal struct TownSpecialisation
    {
        public int Cell;
        public SettlementBuilding Building;
    }

    [Serializable]
    internal struct SettlementRoad
    {
        public int Site;
        public int[] Path;
    }

    [Serializable]
    internal struct SettlementFoodReserve
    {
        public int Cell;
        public int Food;
        public int LastShortfall;
    }

    [Serializable]
    internal sealed class CampaignSave
    {
        public int Version;
        public string MapKey;
        public string TribeName;
        public ClanTradition Tradition;
        public int Colour;
        public int CapitalCell;
        public List<int> SettlementCells;
        public List<int> SettlementProvinceRoots;
        public List<int> Towns;
        public List<int> Garrisons;
        public List<SettlementFoodReserve> SettlementFood;
        public List<TownSpecialisation> TownSpecialisations;
        public List<int> SettlementPopulations; // Capital first, then villages in founding order.
        public int SecondSettlementSite; // Cell index plus one. Zero means no second settlement.
        public int Day;
        public int Speed;
        public int Coin;
        public TaxPolicy Taxes;
        public WarbandSupplyPolicy WarbandSupply;
        public bool[] Owned;
        public int[] Pop;
        public int IntegrationTarget;
        public bool LocalIntegration;
        public int IntegrationSettlementSite;
        public int IndependentVillageSite;
        public int IndependentVillageFoundedDay;
        public int IndependentFarmSite;
        public bool IndependentVillageGuarded;
        public int IntegrationProvinceRoot;
        public int IntegrationDaysLeft;
        public bool OperationMissedObjective;
        public int ObjectiveOccupiedDays;
        public int Food;
        public int Wood;
        public int Iron;
        public int[] Buildings;
        public List<SettlementConstruction> Construction;
        public bool Granary;
        public int GranarySite; // Cell index plus one. Zero uses the original automatic placement.
        public bool Warband;
        public int WarbandRouteIndex;
        public int WarbandMarchCredit;
        public List<int> WarbandRoute;
        public List<int> SecuredFords;
        public List<int> ClosedFords;
        public List<int> BridgedFords;
        public List<SettlementRoad> Roads;
        public List<int> PassageFords;
        public List<int> RivalHeldFords;
        public List<int> PassagePartners; // Region index plus one. Zero represents older agreements.
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
        public int CompletedTradeDeliveries;
        public int NegotiatedIntegrations;
        public int SuccessfulOperations;
        public bool RegionalCouncil;
        public int ResearchPoints;
        public CampaignTechnology Technologies;
        public CampaignPerk Perks;
    }

    internal sealed class CampaignState
    {
        public const int ResearchCost = 2;
        private const int ResearchLimit = 99;
        public const int RoadWood = 25;
        public const int RoadCoin = 20;
        public const int BridgeWood = 20;
        public const int BridgeCoin = 15;
        public const int GarrisonRecruits = 4;
        public const int GarrisonCoin = 10;
        public const float VillageRadius = .008f;
        public const float TownRadius = .012f;
        public const int FordRetakeFood = 5;
        public const int FordPassageCoin = 15;
        public const int RegionalCouncilCoin = 60;
        public const int TradeMilestoneDeliveries = 3;
        public const int DiplomacyMilestoneIntegrations = 2;
        public const int MilitaryMilestoneOperations = 2;
        public const int TownPopulation = 150;
        public const int TownCoin = 40;
        public const int TownWood = 20;
        public const int SettlementCoin = 40;
        public const int SettlementWood = 25;
        public const int StartingCoin = 100;
        public const int IntegrationCost = 25;
        public int SettlementWoodCost => HasPerk(CampaignPerk.Stewardship) ? SettlementWood - 5 : SettlementWood;
        public int TownWoodCost => HasPerk(CampaignPerk.TownPlanning) ? TownWood - 5 : TownWood;
        public int NegotiationCoinCost => HasPerk(CampaignPerk.Envoys) ? IntegrationCost - 5 : IntegrationCost;
        public const int IntegrationDuration = 30;
        public const int MaxNameLength = 32;
        public const int WinterStart = 90;
        public const int WinterEnd = 120;
        public const int GranaryWood = 35;
        public const int GranaryCoin = 35;
        public const int WarbandWood = 35;
        public const int WarbandCoin = 45;
        public const int WarbandRecruits = 8;
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
        private const int SaveVersion = 5;
        private const SettlementBuilding AllBuildings =
            SettlementBuilding.Farm | SettlementBuilding.LumberCamp | SettlementBuilding.IronMine | SettlementBuilding.Docks;
        private const int StartingFood = 40;
        private const int StartingWood = 45;
        private const int ResourceLimit = 999;
        public const int PactCost = 12;
        private const int PactDuration = 20;
        private const int ForceDuration = 10;
        private const int RivalFordResponseDays = 3;

        private readonly ProvinceMap map;
        private readonly CampaignSave data;
        private double fractionOfDay;
        private HashSet<int> dockWaterways;
        private long remainingMarchCost;
        private readonly HashSet<long> roadEdges = new HashSet<long>();
        private readonly Dictionary<int, List<int>> ironWaterRoutes = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, List<int>> ironSupplyRoutes = new Dictionary<int, List<int>>();
        internal Func<int, bool> LandContactResolver { get; set; }
        internal Func<int, int> IndependentVillageSiteResolver { get; set; }
        internal Func<int, int> IndependentFarmSiteResolver { get; set; }
        internal Func<int, bool> IndependentTerritoryResolver { get; set; }
        internal Func<int, bool> MilitaryOccupationResolver { get; set; }
        public int InfluenceRevision { get; private set; }
        public int FordAccessRevision { get; private set; }

        private CampaignState(ProvinceMap map, CampaignSave data)
        {
            this.map = map;
            this.data = data;
            data.Roads ??= new List<SettlementRoad>();
            foreach (var road in data.Roads) AddRoadEdges(road.Path);
            data.LocalIntegration |= !data.Owned[ProvinceAt(map, data.CapitalCell)];
            if (data.WarbandRoute != null && (!data.Warband || data.WarbandRouteIndex < 0
                || data.WarbandRouteIndex >= data.WarbandRoute.Count || !IsWarbandRoute(data.WarbandRoute)))
                throw new ArgumentException("The save contains an invalid warband route.");
            if (data.WarbandRoute != null)
                remainingMarchCost = RouteCost(data.WarbandRoute, data.WarbandRouteIndex);
            if (data.WarbandMarchCredit < 0 || (data.WarbandMarchCredit != 0
                && (remainingMarchCost == 0 || data.WarbandMarchCredit >= MarchStepCost(
                    data.WarbandRoute[data.WarbandRouteIndex], data.WarbandRoute[data.WarbandRouteIndex + 1]))))
                throw new ArgumentException("The save contains invalid warband march progress.");
            data.SecuredFords ??= new List<int>();
            var uniqueFords = new HashSet<int>();
            foreach (var ford in data.SecuredFords)
                if (!map.IsFord(ford) || !uniqueFords.Add(ford))
                    throw new ArgumentException("The save contains an invalid secured ford.");

            data.ClosedFords ??= new List<int>();
            var closedFords = new HashSet<int>();
            foreach (var ford in data.ClosedFords)
                if (!uniqueFords.Contains(ford) || !closedFords.Add(ford))
                    throw new ArgumentException("The save contains a closed ford without a valid clan claim.");

            data.PassageFords ??= new List<int>();
            var passageFords = new HashSet<int>();
            foreach (var ford in data.PassageFords)
                if (!map.IsFord(ford) || !passageFords.Add(ford))
                    throw new ArgumentException("The save contains invalid ford passage rights.");

            data.PassagePartners ??= new List<int>(new int[data.PassageFords.Count]);
            if (data.PassagePartners.Count != data.PassageFords.Count)
                throw new ArgumentException("The save contains mismatched passage partners.");
            foreach (var partner in data.PassagePartners)
                if (partner < 0 || (partner > 0 && !IsLand(map, partner - 1)))
                    throw new ArgumentException("The save contains an invalid passage partner.");

            data.RivalHeldFords ??= new List<int>();
            var rivalFords = new HashSet<int>();
            foreach (var ford in data.RivalHeldFords)
            {
                var index = data.PassageFords.IndexOf(ford);
                if (index < 0 || data.PassagePartners[index] == 0 || IsFordSecured(ford) || !rivalFords.Add(ford))
                    throw new ArgumentException("The save contains invalid rival ford control.");
            }

            data.BridgedFords ??= new List<int>();
            var bridges = new HashSet<int>();
            foreach (var ford in data.BridgedFords)
                if (!uniqueFords.Contains(ford) || !bridges.Add(ford))
                    throw new ArgumentException("The save contains a bridge at an invalid ford.");

            data.Towns ??= new List<int>();
            data.Garrisons ??= new List<int>();
            data.SettlementFood ??= new List<SettlementFoodReserve>();
            data.TownSpecialisations ??= new List<TownSpecialisation>();
            data.Construction ??= new List<SettlementConstruction>();
            data.SettlementCells ??= new List<int>();
            if (data.SecondSettlementSite > 0)
            {
                data.SettlementCells.Add(data.SecondSettlementSite - 1);
                data.SecondSettlementSite = 0;
            }
            if (data.SettlementProvinceRoots == null)
            {
                data.SettlementProvinceRoots = new List<int>();
                var roots = new Dictionary<int, int> { [Capital] = CapitalCell };
                foreach (var site in data.SettlementCells)
                {
                    var region = SettlementProvince(site);
                    if (!roots.TryGetValue(region, out var root)) roots.Add(region, root = site);
                    data.SettlementProvinceRoots.Add(root);
                }
            }
            if (data.SettlementPopulations == null)
            {
                data.SettlementPopulations = new List<int> { data.Pop[Capital] };
                for (var i = 0; i < data.SettlementCells.Count; i++)
                    AllocateSettlers(data.SettlementCells[i]);
            }
        }

        public string TribeName => data.TribeName;
        public ClanTradition Tradition => data.Tradition;
        public int Colour => data.Colour;
        public int CapitalCell => data.CapitalCell;
        public int Capital => SettlementProvince(CapitalCell);
        public IReadOnlyList<int> SettlementCells => data.SettlementCells;
        public IReadOnlyList<int> SettlementProvinceRoots => data.SettlementProvinceRoots;
        public int SettlementProvince(int cell) => ProvinceAt(map, cell);

        public bool CanFoundSettlement(int cell)
        {
            var province = SettlementProvince(cell);
            return cell != CapitalCell && cell != IndependentVillageCell && IsLand(map, province) && province != IntegrationTarget
                && !data.SettlementCells.Contains(cell) && Coin >= SettlementCoin && Wood >= SettlementWoodCost
                && IndependentTerritoryResolver?.Invoke(cell) != true;
        }

        public bool TryFoundSettlement(int cell, int provinceRoot = -1)
        {
            if (!CanFoundSettlement(cell)
                || (provinceRoot != -1 && provinceRoot != CapitalCell
                    && !data.SettlementProvinceRoots.Contains(provinceRoot))) return false;
            var party = PlanFoundingParty(cell, provinceRoot);
            if (party.People == 0) return false;
            data.Coin -= SettlementCoin;
            data.Wood -= SettlementWoodCost;
            data.SettlementCells.Add(cell);
            data.SettlementProvinceRoots.Add(provinceRoot == -1 ? cell : provinceRoot);
            AllocateSettlers(cell, party.Donor, party.People, Owns(SettlementProvince(cell)));
            InfluenceRevision++;
            return true;
        }

        public bool HasGarrison(int cell) => data.Garrisons.Contains(cell);
        public bool CanRaiseGarrison(int cell) => !HasGarrison(cell) && Coin >= GarrisonCoin
            && SettlementPopulation(cell) > GarrisonRecruits;

        public bool TryRaiseGarrison(int cell)
        {
            if (!CanRaiseGarrison(cell)) return false;
            var index = cell == CapitalCell ? 0 : data.SettlementCells.IndexOf(cell) + 1;
            data.SettlementPopulations[index] -= GarrisonRecruits;
            data.Pop[SettlementProvince(cell)] -= GarrisonRecruits;
            data.Coin -= GarrisonCoin;
            data.Garrisons.Add(cell);
            InfluenceRevision++;
            return true;
        }

        public bool TryDisbandGarrison(int cell)
        {
            var region = SettlementProvince(cell);
            if (!HasGarrison(cell) || data.Pop[region] > int.MaxValue - GarrisonRecruits) return false;
            var index = cell == CapitalCell ? 0 : data.SettlementCells.IndexOf(cell) + 1;
            data.Garrisons.Remove(cell);
            data.SettlementPopulations[index] += GarrisonRecruits;
            data.Pop[region] += GarrisonRecruits;
            InfluenceRevision++;
            return true;
        }

        public bool IsTown(int cell) => data.Towns.Contains(cell);

        public SettlementBuilding TownSpeciality(int cell)
        {
            foreach (var entry in data.TownSpecialisations)
                if (entry.Cell == cell) return entry.Building;
            return 0;
        }

        public bool TrySpecialiseTown(int cell, SettlementBuilding building)
        {
            if (!IsTown(cell) || TownSpeciality(cell) != 0
                || !Enum.IsDefined(typeof(SettlementBuilding), building)
                || !HasSettlementBuilding(cell, building)) return false;
            data.TownSpecialisations.Add(new TownSpecialisation { Cell = cell, Building = building });
            return true;
        }

        public bool TryUpgradeTown(int cell)
        {
            if (IsTown(cell) || SettlementPopulation(cell) < TownPopulation || Coin < TownCoin || Wood < TownWoodCost)
                return false;
            data.Coin -= TownCoin;
            data.Wood -= TownWoodCost;
            data.Towns.Add(cell);
            return true;
        }

        public int SettlementPopulation(int cell)
        {
            if (cell == CapitalCell) return data.SettlementPopulations[0];
            var index = data.SettlementCells.IndexOf(cell);
            return index < 0 ? 0 : data.SettlementPopulations[index + 1];
        }

        private int SettlementCellAt(int index) => index == 0 ? CapitalCell : data.SettlementCells[index - 1];

        private int FindSettlerDonor(int cell, int provinceRoot)
        {
            var region = SettlementProvince(cell);
            var donor = -1;
            for (var i = 0; i < data.SettlementPopulations.Count; i++)
            {
                var matches = provinceRoot < 0
                    ? SettlementProvince(SettlementCellAt(i)) == region
                    : (i == 0 ? CapitalCell : data.SettlementProvinceRoots[i - 1]) == provinceRoot;
                if (matches && (donor < 0 || data.SettlementPopulations[i] > data.SettlementPopulations[donor]))
                    donor = i;
            }
            return donor;
        }

        public (int Origin, int People) FoundingParty(int cell, int provinceRoot)
        {
            var party = PlanFoundingParty(cell, provinceRoot);
            return party.People == 0 ? (-1, 0) : (SettlementCellAt(party.Donor), party.People);
        }

        private (int Donor, int People) PlanFoundingParty(int cell, int provinceRoot)
        {
            var region = SettlementProvince(cell);
            if (!IsLand(map, region)) return (-1, 0);

            var donor = provinceRoot >= 0 ? FindSettlerDonor(cell, provinceRoot) : -1;
            if (provinceRoot < 0)
            {
                var nearest = long.MaxValue;
                for (var i = 0; i < data.SettlementPopulations.Count; i++)
                {
                    if (data.SettlementPopulations[i] < 2) continue;
                    var origin = SettlementCellAt(i);
                    var x = (long)(origin % map.Width - cell % map.Width);
                    var y = (long)(origin / map.Width - cell / map.Width);
                    var distance = x * x + y * y;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    donor = i;
                }
            }
            if (donor < 0 || data.SettlementPopulations[donor] < 2) return (-1, 0);

            var population = data.SettlementPopulations[donor];
            var people = provinceRoot >= 0 ? population / 2 : Math.Min(50, Math.Max(1, population / 4));
            if (SettlementProvince(SettlementCellAt(donor)) != region)
                people = Math.Min(people, int.MaxValue - data.Pop[region]);
            return (donor, people);
        }

        private void AllocateSettlers(int cell, int donor = -1, int plannedPeople = -1, bool includeResidents = true)
        {
            var region = SettlementProvince(cell);
            var localDonor = FindSettlerDonor(cell, -1);
            if (donor < 0) donor = localDonor;
            var residents = localDonor < 0 && includeResidents ? data.Pop[region] : 0;
            var settlers = donor < 0 ? 0 : plannedPeople >= 0 ? plannedPeople : data.SettlementPopulations[donor] / 2;
            if (donor >= 0)
            {
                var source = SettlementProvince(SettlementCellAt(donor));
                if (source != region)
                {
                    settlers = Math.Min(settlers, int.MaxValue - data.Pop[region]);
                    data.Pop[source] -= settlers;
                    data.Pop[region] += settlers;
                }
                data.SettlementPopulations[donor] -= settlers;
            }
            data.SettlementPopulations.Add(residents + settlers);
        }

        public int Day => data.Day;
        public int Speed => data.Speed;
        public int Coin => data.Coin;
        public bool UsesLocalIntegration => data.LocalIntegration;
        public int IndependentVillageCell => data.IndependentVillageSite - 1;
        public bool IndependentVillageOccupied => IndependentVillageCell >= 0 && WarbandCommitted
            && MilitaryOccupationResolver?.Invoke(IndependentVillageCell) == true;
        public bool IndependentVillageGuarded => data.IndependentVillageGuarded;
        public int IndependentFarmCell => data.IndependentFarmSite - 1;
        public int IndependentVillageRegion => SettlementProvince(IndependentVillageCell);

        public int IntegrationCommunitySite(int region)
            => region >= 0 && region == IndependentVillageRegion ? IndependentVillageCell : SettlementInRegion(region);

        public int IntegrationSettlementCell => data.IntegrationSettlementSite - 1;

        public int SettlementInRegion(int region)
        {
            if (region == Capital) return CapitalCell;
            foreach (var cell in SettlementCells)
                if (SettlementProvince(cell) == region) return cell;
            return -1;
        }

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
                for (var i = 0; i < data.SettlementPopulations.Count; i++)
                    if (!Owns(SettlementProvince(SettlementCellAt(i)))) total += data.SettlementPopulations[i];
                return total;
            }
        }
        public TaxPolicy Taxes => data.Taxes;
        public long TaxIncome
        {
            get
            {
                var standard = (OwnedPopulation + 99) / 100;
                return Taxes switch
                {
                    TaxPolicy.Relief => standard / 2,
                    TaxPolicy.Heavy => (standard * 3 + 1) / 2,
                    _ => standard
                };
            }
        }

        public void SetTaxPolicy(TaxPolicy policy)
        {
            if (!Enum.IsDefined(typeof(TaxPolicy), policy))
                throw new ArgumentOutOfRangeException(nameof(policy));
            data.Taxes = policy;
        }

        public int CapitalHappiness => SettlementHappiness(CapitalCell);
        public int SettlementHappiness(int cell) => Happiness(cell, HasFoodForUpkeep(cell));

        public (int Base, int Tax, int Food, int Farm, int Granary) SettlementHappinessBreakdown(int cell)
            => HappinessFactors(cell, HasFoodForUpkeep(cell));

        private (int Base, int Tax, int Food, int Farm, int Granary) HappinessFactors(int cell, bool fed)
        {
            var tax = Taxes switch { TaxPolicy.Relief => 15, TaxPolicy.Heavy => -30, _ => 0 };
            var farm = HasSettlementBuilding(cell, SettlementBuilding.Farm) ? 5 : 0;
            var granary = cell == CapitalCell && HasGranary ? 5 : 0;
            return (60, tax, fed ? 0 : -25, farm, granary);
        }

        private int Happiness(int cell, bool fed)
        {
            var factors = HappinessFactors(cell, fed);
            return factors.Base + factors.Tax + factors.Food + factors.Farm + factors.Granary;
        }

        public long TaxCollected => data.TaxCollected;
        public long TradeReceipts => data.TradeReceipts;
        public long TradeSpending => data.TradeSpending;
        public int FoodReserve => data.FoodReserve;
        public IReadOnlyList<TradeAgreement> TradeAgreements => data.Agreements;
        public int TradeCapacity => 1 + (HasRegionalCouncil ? 1 : 0) + BuildingTotal(SettlementBuilding.Docks);
        public bool HasGranary => data.Granary;
        public int GranaryCell => data.GranarySite - 1;
        public bool HasWarband => data.Warband;
        public IReadOnlyList<int> BridgedFords => data.BridgedFords;
        public bool HasBridge(int ford) => data.BridgedFords?.Contains(ford) == true;
        public bool CanBuildBridge(int ford) => CanChangeFordAccess(ford) && !HasBridge(ford)
            && Wood >= BridgeWood && Coin >= BridgeCoin;

        public bool TryBuildBridge(int ford)
        {
            if (!CanBuildBridge(ford)) return false;
            data.Wood -= BridgeWood;
            data.Coin -= BridgeCoin;
            data.BridgedFords.Add(ford);
            RepriceMarch();
            RefreshFordAccess();
            return true;
        }

        public IReadOnlyList<SettlementRoad> Roads => data.Roads;

        public bool HasRoadToCapital(int site)
        {
            foreach (var road in data.Roads)
                if (road.Site == site) return true;
            return false;
        }

        public bool TryBuildRoad(int site)
        {
            if (!data.SettlementCells.Contains(site) || HasRoadToCapital(site) || Wood < RoadWood || Coin < RoadCoin)
                return false;
            var path = GetOverlandIronRoute(site);
            if (path == null || path.Count < 2) return false;

            var road = new SettlementRoad { Site = site, Path = new List<int>(path).ToArray() };
            data.Coin -= RoadCoin;
            data.Wood -= RoadWood;
            data.Roads.Add(road);
            AddRoadEdges(road.Path);
            RepriceMarch();
            ironSupplyRoutes.Clear();
            InfluenceRevision++;
            return true;
        }

        private static long RoadEdge(int first, int second)
            => ((long)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);

        internal bool HasRoadEdge(int first, int second) => roadEdges.Contains(RoadEdge(first, second));

        internal float? InfluenceInfrastructureCost(int from, int to)
        {
            if (map.IsFord(from) || map.IsFord(to))
            {
                var ford = map.FordForBridgeEdge(from, to);
                return ford >= 0 && HasBridge(ford) && CanUseSupplyFord(ford) ? .55f : (float?)null;
            }
            return HasRoadEdge(from, to) ? .65f : (float?)null;
        }

        private void AddRoadEdges(IReadOnlyList<int> path)
        {
            for (var i = 1; i < path.Count; i++) roadEdges.Add(RoadEdge(path[i - 1], path[i]));
        }

        private void RepriceMarch()
        {
            if (data.WarbandRoute == null || remainingMarchCost == 0) return;
            remainingMarchCost = RouteCost(data.WarbandRoute, data.WarbandRouteIndex);
            var index = data.WarbandRouteIndex;
            data.WarbandMarchCredit = Math.Min(data.WarbandMarchCredit,
                MarchStepCost(data.WarbandRoute[index], data.WarbandRoute[index + 1]) - 1);
        }

        public bool IsFordSecured(int cell) => data.SecuredFords.Contains(cell);

        public IReadOnlyList<int> PassageFords => data.PassageFords;
        public bool IsPassageSuspended(int cell)
        {
            if (!WarbandCommitted || IsFordSecured(cell)) return false;
            var index = data.PassageFords.IndexOf(cell);
            return index >= 0 && data.PassagePartners[index] == IntegrationTarget + 1;
        }

        public bool IsFordHeldByRival(int cell) => data.RivalHeldFords.Contains(cell);

        public int RivalFordHolder(int cell)
            => IsFordHeldByRival(cell) ? data.PassagePartners[data.PassageFords.IndexOf(cell)] - 1 : -1;
        public bool IsFordClosed(int cell)
            => data.ClosedFords.Contains(cell) || IsPassageSuspended(cell) || IsFordHeldByRival(cell);
        public bool HasFordPassage(int cell) => data.PassageFords.Contains(cell);
        public bool CanUseSupplyFord(int cell) => !IsFordClosed(cell) && (IsFordSecured(cell) || HasFordPassage(cell));

        public int FordPassagePartner(int ford)
        {
            if (!map.IsFord(ford) || IsFordSecured(ford)) return -1;
            var agreement = data.PassageFords.IndexOf(ford);
            if (agreement >= 0)
            {
                if (!IsFordHeldByRival(ford)) return -1;
                var holder = data.PassagePartners[agreement] - 1;
                return CanReachIndependentProvince(holder) && (!WarbandCommitted || IntegrationTarget != holder)
                    ? holder : -1;
            }
            var x = ford % map.Width;
            var candidates = new[]
            {
                x > 0 ? ford - 1 : -1, x + 1 < map.Width ? ford + 1 : -1,
                ford - map.Width, ford + map.Width
            };
            foreach (var bank in candidates)
            {
                if (!map.CanMarchBetween(bank, ford)) continue;
                var region = SettlementProvince(bank);
                if (CanReachIndependentProvince(region) && (!WarbandCommitted || IntegrationTarget != region))
                    return region;
            }
            return -1;
        }

        public bool TryNegotiateFordPassage(int ford)
        {
            var partner = FordPassagePartner(ford);
            if (Coin < FordPassageCoin || partner < 0) return false;
            data.Coin -= FordPassageCoin;
            if (!data.RivalHeldFords.Remove(ford))
            {
                data.PassageFords.Add(ford);
                data.PassagePartners.Add(partner + 1);
            }
            RefreshFordAccess();
            return true;
        }

        private bool CanGuardFord(int cell)
            => HasWarband && !WarbandCommitted && WarbandTravelDays == 0 && map.IsFord(cell)
                && map.CanMarchBetween(WarbandCell, cell);

        public bool CanSecureFord(int cell) => CanGuardFord(cell) && !IsFordSecured(cell)
            && (!IsFordHeldByRival(cell) || Food >= FordRetakeFood);
        public bool CanChangeFordAccess(int cell) => IsFordSecured(cell) && (CanGuardFord(cell) || HasGarrisonAtFord(cell));

        public bool HasGarrisonAtFord(int ford)
        {
            if (!map.IsFord(ford)) return false;
            foreach (var site in data.Garrisons)
            {
                var radius = map.Height * (IsTown(site) ? TownRadius : VillageRadius);
                if (ReachesBank(ford - 1) || ReachesBank(ford + 1)
                    || ReachesBank(ford - map.Width) || ReachesBank(ford + map.Width)) return true;

                bool ReachesBank(int bank)
                {
                    if (!map.CanMarchBetween(bank, ford)) return false;
                    var dx = bank % map.Width - site % map.Width;
                    var dy = bank / map.Width - site / map.Width;
                    return (long)dx * dx + (long)dy * dy <= radius * radius;
                }
            }
            return false;
        }

        public bool TrySecureFord(int cell)
        {
            if (!CanSecureFord(cell)) return false;
            data.SecuredFords.Add(cell);
            if (data.RivalHeldFords.Remove(cell)) data.Food -= FordRetakeFood;
            RefreshFordAccess();
            return true;
        }

        public bool TryChangeFordAccess(int cell)
        {
            if (!CanChangeFordAccess(cell)) return false;
            if (!data.ClosedFords.Remove(cell)) data.ClosedFords.Add(cell);
            RefreshFordAccess();
            return true;
        }

        private void UpdateHeldShipments()
        {
            for (var i = 0; i < data.Construction.Count; i++)
            {
                var shipment = data.Construction[i];
                var path = shipment.IronTravelPath;
                if (shipment.IronInTransit == 0 || shipment.IronTransport != IronSupplyRoute.Overland
                    || path == null || path.Length < 2) continue;

                var positionDay = shipment.IronHoldDay > 0 ? shipment.IronHoldDay : Day;
                if (positionDay >= shipment.IronTravelEndDay) continue;
                var elapsed = positionDay - shipment.IronDepartureDay;
                var duration = shipment.IronTravelEndDay - shipment.IronDepartureDay;
                var blocked = false;
                for (var step = 1; step < path.Length - 1; step++)
                    if ((long)step * duration >= (long)elapsed * (path.Length - 1) && IsFordClosed(path[step]))
                    {
                        blocked = true;
                        break;
                    }

                if (blocked && shipment.IronHoldDay == 0)
                    shipment.IronHoldDay = Day;
                else if (!blocked && shipment.IronHoldDay > 0)
                {
                    var delay = Day - shipment.IronHoldDay;
                    shipment.IronDepartureDay += delay;
                    shipment.IronTravelEndDay = (int)Math.Min(int.MaxValue, (long)shipment.IronTravelEndDay + delay);
                    shipment.IronArrivalDay = (int)Math.Min(int.MaxValue, (long)shipment.IronArrivalDay + delay);
                    shipment.IronHoldDay = 0;
                }
                data.Construction[i] = shipment;
            }
        }

        private void RefreshFordAccess()
        {
            ironSupplyRoutes.Clear();
            InfluenceRevision++;
            FordAccessRevision++;
            UpdateHeldShipments();
        }

        public int ObjectiveOccupiedDays => data.ObjectiveOccupiedDays;
        public int ObjectiveHoldDaysRequired => IndependentVillageGuarded ? 3 : 1;
        public bool OperationMissedObjective => data.OperationMissedObjective;
        public int MilitaryObjectiveCell
            => WarbandCommitted && IntegrationTarget == IndependentVillageRegion ? IndependentVillageCell : -1;

        public bool WarbandCommitted => PendingApproach == ExpansionApproach.Force;
        public bool TryWithdrawWarband()
        {
            if (!WarbandCommitted) return false;
            ClearIntegration();
            return true;
        }

        public float MilitaryPressure => WarbandCommitted ? (ForceDuration - IntegrationDaysLeft + 1f) / ForceDuration : 0f;
        public bool WarbandAt(int province) => HasWarband && WarbandTravelDays == 0
            && province >= 0 && ProvinceAt(map, WarbandCell) == province;
        public int WarbandCell => !HasWarband ? -1
            : data.WarbandRoute == null ? CapitalCell : data.WarbandRoute[data.WarbandRouteIndex];
        public IReadOnlyList<int> WarbandRoute => data.WarbandRoute;
        public int WarbandRouteIndex => data.WarbandRouteIndex;
        private int MarchBudgetPerDay => WarbandCellsPerDay * ProvinceMap.BaseMarchCost;
        public int WarbandTravelDays
            => (int)((remainingMarchCost - data.WarbandMarchCredit + MarchBudgetPerDay - 1) / MarchBudgetPerDay);
        public WarbandSupplyPolicy WarbandSupply => data.WarbandSupply;
        public int WarbandFoodCost => !HasWarband ? 0 : WarbandSupply switch
        {
            WarbandSupplyPolicy.Forage => 0,
            WarbandSupplyPolicy.Provisioned => 2,
            _ => 1
        };

        public void SetWarbandSupply(WarbandSupplyPolicy policy)
        {
            if (!Enum.IsDefined(typeof(WarbandSupplyPolicy), policy))
                throw new ArgumentOutOfRangeException(nameof(policy));
            data.WarbandSupply = policy;
        }

        private int WarbandCellsPerDay
        {
            get
            {
                var normal = Math.Max(1, map.Height / 128);
                if (HasTechnology(CampaignTechnology.MarchLogistics)) normal++;
                return WarbandSupply switch
                {
                    WarbandSupplyPolicy.Forage => Math.Max(1, normal / 2),
                    WarbandSupplyPolicy.Provisioned => normal + Math.Max(1, normal / 2),
                    _ => normal
                };
            }
        }

        public int EstimateMarchDays(IReadOnlyList<int> route)
            => route != null && route.Count > 1 && route[0] == WarbandCell
                ? (int)((RouteCost(route, 0) + MarchBudgetPerDay - 1) / MarchBudgetPerDay) : 0;

        public bool TryHaltWarband()
        {
            if (WarbandCommitted || WarbandTravelDays == 0 || map.IsFord(WarbandCell)) return false;
            var cell = WarbandCell;
            data.WarbandRoute = new List<int> { cell };
            data.WarbandRouteIndex = 0;
            data.WarbandMarchCredit = 0;
            remainingMarchCost = 0;
            return true;
        }

        public bool TryMarchWarband(List<int> route)
        {
            if (!HasWarband || WarbandCommitted || route == null || route.Count < 2 || route[0] != WarbandCell || !IsWarbandRoute(route))
                return false;
            SetWarbandRoute(route);
            return true;
        }

        public bool CanRepositionWarband(int cell)
            => WarbandCommitted && cell != WarbandCell && SettlementProvince(cell) == IntegrationTarget
                && map.IsMarchCell(cell) && !map.IsFord(cell);

        public bool TryRepositionWarband(int cell)
        {
            if (!CanRepositionWarband(cell)) return false;
            var route = map.FindWarbandRoute(WarbandCell, cell, canVisit: step => SettlementProvince(step) == IntegrationTarget,
                hasBridge: HasBridge, hasRoad: HasRoadEdge);
            if (route == null) return false;
            SetWarbandRoute(route);
            return true;
        }

        private void SetWarbandRoute(IReadOnlyList<int> route)
        {
            data.WarbandRoute = new List<int>(route);
            data.WarbandRouteIndex = 0;
            data.WarbandMarchCredit = 0;
            remainingMarchCost = RouteCost(route, 0);
        }

        private bool IsWarbandRoute(IReadOnlyList<int> route)
        {
            for (var i = 0; i < route.Count; i++)
            {
                var cell = route[i];
                if (!map.IsMarchCell(cell) || (i > 0 && !map.CanMarchBetween(route[i - 1], cell))) return false;
            }
            return true;
        }

        internal List<int> PlanWarbandRoute(int goal) => map.FindWarbandRoute(WarbandCell, goal, hasBridge: HasBridge, hasRoad: HasRoadEdge);

        private int MarchStepCost(int from, int to) => map.MarchStepCost(from, to, HasBridge, HasRoadEdge);

        private long RouteCost(IReadOnlyList<int> route, int start)
        {
            long total = 0;
            for (var i = start + 1; i < route.Count; i++)
                total += MarchStepCost(route[i - 1], route[i]);
            return total;
        }

        private void AdvanceWarband(int days)
        {
            if (remainingMarchCost == 0) return;
            var route = data.WarbandRoute;
            var budget = data.WarbandMarchCredit + (long)days * MarchBudgetPerDay;
            while (data.WarbandRouteIndex < route.Count - 1)
            {
                var index = data.WarbandRouteIndex;
                var step = MarchStepCost(route[index], route[index + 1]);
                if (budget < step) break;
                budget -= step;
                remainingMarchCost -= step;
                data.WarbandRouteIndex++;
            }

            if (remainingMarchCost == 0)
            {
                var destination = route[data.WarbandRouteIndex];
                route.Clear();
                route.Add(destination);
                data.WarbandRouteIndex = 0;
                data.WarbandMarchCredit = 0;
            }
            else data.WarbandMarchCredit = (int)budget;
        }

        public bool HasIronWarband => data.IronWarband;
        public bool CanUpgradeWarband => HasWarband && !HasIronWarband
            && Iron >= IronWarbandIron && Wood >= IronWarbandWood && Coin >= IronWarbandCoin;
        public bool IsFoodIsolated(int cell) => cell != CapitalCell && GetIronSupplyRoute(cell) == IronSupplyRoute.None;

        public int LocalFoodUpkeep(int cell)
            => (int)(((long)SettlementPopulation(cell) + PeoplePerFood - 1) / PeoplePerFood) + (HasGarrison(cell) ? 1 : 0);

        public int LocalFoodStock(int cell)
        {
            foreach (var reserve in data.SettlementFood)
                if (reserve.Cell == cell) return reserve.Food;
            return 0;
        }

        public int LocalFoodShortfall(int cell)
        {
            foreach (var reserve in data.SettlementFood)
                if (reserve.Cell == cell) return reserve.LastShortfall;
            return 0;
        }

        private bool HasFoodForUpkeep(int cell)
            => IsFoodIsolated(cell) ? LocalFoodStock(cell) >= LocalFoodUpkeep(cell) : Food >= FoodUpkeep;

        public long FoodUpkeep
        {
            get
            {
                var people = OwnedPopulation;
                var guards = data.Garrisons.Count;
                foreach (var cell in SettlementCells)
                {
                    if (!IsFoodIsolated(cell)) continue;
                    people -= SettlementPopulation(cell);
                    if (HasGarrison(cell)) guards--;
                }
                return (people + PeoplePerFood - 1) / PeoplePerFood + guards;
            }
        }

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
        private int WinterFoodOutput => FarmFood - (HasGranary ? 0 : 2) - WarbandFoodCost;
        private int FarmFood
        {
            get
            {
                var food = BuildingTotal(SettlementBuilding.Farm);
                foreach (var cell in SettlementCells)
                    if (IsFoodIsolated(cell)) food -= SettlementFarmYield(cell);
                return food;
            }
        }

        private int SettlementFarmYield(int cell)
        {
            if (!HasSettlementBuilding(cell, SettlementBuilding.Farm)) return 0;
            var yield = SettlementBuildingInfo(cell, SettlementBuilding.Farm, BuildingPlacement(cell, SettlementBuilding.Farm)).Yield;
            if (yield == 0) yield = BuildingInfo(SettlementProvince(cell), SettlementBuilding.Farm).Yield;
            return yield + ResearchProductionBonus(SettlementBuilding.Farm)
                + (TownSpeciality(cell) == SettlementBuilding.Farm ? 1 : 0);
        }
        public int SubsistenceFood(int province) => province == Capital ? Fertility(map.Provinces[province]) : 1;

        private int WarmFoodOutput
        {
            get
            {
                var food = FarmFood - WarbandFoodCost;
                var settled = new HashSet<int> { Capital };
                var connected = new HashSet<int> { Capital };
                foreach (var cell in SettlementCells)
                {
                    var region = SettlementProvince(cell);
                    settled.Add(region);
                    if (!IsFoodIsolated(cell)) connected.Add(region);
                }
                foreach (var region in connected) food += SubsistenceFood(region);
                for (var province = 0; province < data.Owned.Length; province++)
                    if (Owns(province) && !settled.Contains(province)) food += SubsistenceFood(province);
                return food;
            }
        }
        public ExpansionApproach FirstExpansion => data.FirstExpansion;
        public int CompletedTradeDeliveries => data.CompletedTradeDeliveries;
        public int NegotiatedIntegrations => Math.Max(data.NegotiatedIntegrations, FirstExpansion == ExpansionApproach.Diplomacy ? 1 : 0);
        public int SuccessfulOperations => Math.Max(data.SuccessfulOperations, FirstExpansion == ExpansionApproach.Force ? 1 : 0);
        public int SecuredFordCount => data.SecuredFords.Count;
        public bool TradeMilestoneComplete => CompletedTradeDeliveries >= TradeMilestoneDeliveries;
        public bool DiplomacyMilestoneComplete => NegotiatedIntegrations >= DiplomacyMilestoneIntegrations;
        public bool MilitaryMilestoneComplete => SuccessfulOperations >= MilitaryMilestoneOperations && SecuredFordCount > 0;
        private int EarnedSkillPoints => (TradeMilestoneComplete ? 1 : 0)
            + (DiplomacyMilestoneComplete ? 1 : 0) + (MilitaryMilestoneComplete ? 1 : 0);
        private int SpentSkillPoints => (HasPerk(CampaignPerk.Stewardship) ? 1 : 0)
            + (HasPerk(CampaignPerk.TownPlanning) ? 1 : 0) + (HasPerk(CampaignPerk.Envoys) ? 1 : 0);
        public int SkillPoints => EarnedSkillPoints - SpentSkillPoints;
        public bool HasPerk(CampaignPerk perk) => (data.Perks & perk) != 0;

        public bool CanUnlockPerk(CampaignPerk perk) => SkillPoints > 0
            && Enum.IsDefined(typeof(CampaignPerk), perk) && !HasPerk(perk)
            && (perk == CampaignPerk.Stewardship || HasPerk(CampaignPerk.Stewardship));

        public bool TryUnlockPerk(CampaignPerk perk)
        {
            if (!CanUnlockPerk(perk)) return false;
            data.Perks |= perk;
            return true;
        }

        public bool HasRegionalCouncil => data.RegionalCouncil;
        public int ResearchPoints => data.ResearchPoints;
        public bool HasTechnology(CampaignTechnology technology) => (data.Technologies & technology) != 0;
        public int ResearchProductionBonus(SettlementBuilding building)
            => building == SettlementBuilding.Farm && HasTechnology(CampaignTechnology.Agronomy) ? 1 : 0;

        public bool TryResearch(CampaignTechnology technology)
        {
            if (!HasRegionalCouncil || ResearchPoints < ResearchCost || !Enum.IsDefined(typeof(CampaignTechnology), technology)
                || HasTechnology(technology)) return false;
            data.ResearchPoints -= ResearchCost;
            data.Technologies |= technology;
            return true;
        }

        public string RegionalCouncilIssue
        {
            get
            {
                if (!IsTown(CapitalCell)) return "Upgrade the capital to a town.";
                if (SettlementCells.Count == 0) return "Establish a second settlement.";
                if (!TradeMilestoneComplete && !DiplomacyMilestoneComplete && !MilitaryMilestoneComplete)
                    return "Complete a campaign milestone.";
                if (Coin < RegionalCouncilCoin) return $"Requires {RegionalCouncilCoin} coin.";
                return null;
            }
        }

        public bool TryEstablishRegionalCouncil()
        {
            if (HasRegionalCouncil || RegionalCouncilIssue != null) return false;
            data.Coin -= RegionalCouncilCoin;
            data.RegionalCouncil = true;
            return true;
        }
        public ExpansionApproach PendingApproach => data.PendingApproach;
        public bool CanBuildGranary => !HasGranary && Wood >= GranaryWood && Coin >= GranaryCoin;
        public int WarbandRecruitWood => Tradition == ClanTradition.Highland ? WarbandWood - 10 : WarbandWood;
        public int WarbandRecruitCoin => Tradition == ClanTradition.Highland ? WarbandCoin - 10 : WarbandCoin;
        public bool CanRecruitWarband => !HasWarband && Wood >= WarbandRecruitWood && Coin >= WarbandRecruitCoin
            && SettlementPopulation(CapitalCell) > WarbandRecruits;

        private bool HasSettlementInRegion(int region)
        {
            if (region == Capital) return true;
            foreach (var cell in SettlementCells)
                if (SettlementProvince(cell) == region) return true;
            return false;
        }

        public int IndependentPopulation(int region)
        {
            if (!IsLand(map, region) || Owns(region)) return 0;
            var independent = Population(region);
            for (var i = 0; i < data.SettlementPopulations.Count; i++)
                if (SettlementProvince(SettlementCellAt(i)) == region) independent -= data.SettlementPopulations[i];
            return independent;
        }

        public bool Owns(int province) => province >= 0 && province < data.Owned.Length && data.Owned[province];
        public int Population(int province) => data.Pop[province];
        public CampaignSave ToSave() => data;

        // Seed the capital and initial resources, while also creating population entries for independent
        // land provinces.
        public static CampaignState Found(ProvinceMap map, string mapKey, int capitalCell, string name, int colour,
            bool localCapitalClaim = false, ClanTradition tradition = ClanTradition.Standard)
        {
            var capital = ProvinceAt(map, capitalCell);
            if (!IsLand(map, capital) || !IsValidName(name) || colour < 0
                || !Enum.IsDefined(typeof(ClanTradition), tradition))
                throw new ArgumentException("Founding requires a land province, a tribe name, and a colour.");
            var owned = new bool[map.Provinces.Length];
            var pop = new int[owned.Length];
            owned[capital] = !localCapitalClaim;
            for (var i = 0; i < pop.Length; i++)
                if (IsLand(map, i))
                    pop[i] = 100 + Math.Min(900, map.Provinces[i].PixelCount / 4);
            return new CampaignState(map, new CampaignSave
            {
                Version = SaveVersion,
                MapKey = mapKey,
                TribeName = name.Trim(),
                Tradition = tradition,
                Colour = colour,
                CapitalCell = capitalCell,
                LocalIntegration = localCapitalClaim,
                SettlementPopulations = localCapitalClaim ? new List<int> { 100 } : null,
                Day = 1,
                Speed = 1,
                Coin = tradition switch
                {
                    ClanTradition.River => StartingCoin - 20,
                    ClanTradition.Highland => StartingCoin + 20,
                    _ => StartingCoin
                },
                Owned = owned,
                Buildings = new int[owned.Length],
                Markets = new NeighbourMarket[owned.Length],
                Agreements = new List<TradeAgreement>(),
                FoodReserve = DefaultFoodReserve,
                Pop = pop,
                IntegrationTarget = -1,
                TradePartner = -1,
                Food = StartingFood,
                Wood = tradition switch
                {
                    ClanTradition.River => StartingWood + 10,
                    ClanTradition.Highland => StartingWood - 10,
                    _ => StartingWood
                }
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
                || save.SecondSettlementSite < 0 || save.SecondSettlementSite > map.ProvinceAt.Length
                || (save.SecondSettlementSite > 0 && (!IsLand(map, ProvinceAt(map, save.SecondSettlementSite - 1))
                    || !save.Owned[ProvinceAt(map, save.SecondSettlementSite - 1)]
                    || save.SecondSettlementSite - 1 == save.CapitalCell))
                || (!save.Owned[capital] && save.SettlementPopulations == null)
                || !Enum.IsDefined(typeof(TaxPolicy), save.Taxes)
                || !Enum.IsDefined(typeof(ClanTradition), save.Tradition)
                || save.ResearchPoints < 0 || save.ResearchPoints > ResearchLimit
                || (save.Technologies & ~(CampaignTechnology.Agronomy | CampaignTechnology.MarchLogistics)) != 0
                || (!save.RegionalCouncil && (save.ResearchPoints != 0 || save.Technologies != 0))
                || save.CompletedTradeDeliveries < 0 || save.NegotiatedIntegrations < 0 || save.SuccessfulOperations < 0
                || !Enum.IsDefined(typeof(WarbandSupplyPolicy), save.WarbandSupply))
                throw new ArgumentException("The save contains invalid campaign data.");

            if (save.SettlementProvinceRoots != null)
            {
                if (save.SecondSettlementSite != 0 || save.SettlementProvinceRoots.Count != (save.SettlementCells?.Count ?? 0))
                    throw new ArgumentException("The save contains mismatched settlement provinces.");
                var roots = new HashSet<int> { save.CapitalCell };
                for (var i = 0; i < save.SettlementProvinceRoots.Count; i++)
                {
                    var root = save.SettlementProvinceRoots[i];
                    if (root == save.SettlementCells[i]) roots.Add(root);
                    else if (!roots.Contains(root))
                        throw new ArgumentException("The save contains an unknown settlement province.");
                }
            }

            var settlementSites = new HashSet<int>();
            if (save.SecondSettlementSite > 0) settlementSites.Add(save.SecondSettlementSite - 1);
            if (save.SettlementCells != null)
                foreach (var site in save.SettlementCells)
                {
                    var province = ProvinceAt(map, site);
                    if (!IsLand(map, province) || site == save.CapitalCell
                        || !settlementSites.Add(site))
                        throw new ArgumentException("The save contains an invalid settlement site.");
                }

            if (save.Towns != null)
            {
                var towns = new HashSet<int>();
                foreach (var cell in save.Towns)
                    if ((cell != save.CapitalCell && !settlementSites.Contains(cell)) || !towns.Add(cell))
                        throw new ArgumentException("The save contains an invalid town.");
            }

            if (save.Roads != null)
            {
                var endpoints = new HashSet<int>();
                foreach (var road in save.Roads)
                {
                    if (!settlementSites.Contains(road.Site) || !endpoints.Add(road.Site) || road.Path == null
                        || road.Path.Length < 2 || road.Path[0] != road.Site || road.Path[road.Path.Length - 1] != save.CapitalCell)
                        throw new ArgumentException("The save contains an invalid settlement road.");
                    for (var i = 1; i < road.Path.Length; i++)
                        if (!map.CanMarchBetween(road.Path[i - 1], road.Path[i]))
                            throw new ArgumentException("The save contains an invalid road segment.");
                }
            }

            if (save.Garrisons != null)
            {
                var garrisonSites = new HashSet<int>();
                foreach (var site in save.Garrisons)
                    if ((site != save.CapitalCell && !settlementSites.Contains(site)) || !garrisonSites.Add(site))
                        throw new ArgumentException("The save contains an invalid settlement garrison.");
            }

            if (save.SettlementFood != null)
            {
                var foodSites = new HashSet<int>();
                foreach (var reserve in save.SettlementFood)
                    if (!settlementSites.Contains(reserve.Cell) || !foodSites.Add(reserve.Cell)
                        || !ValidStock(reserve.Food) || reserve.LastShortfall < 0)
                        throw new ArgumentException("The save contains an invalid local food reserve.");
            }

            if (save.IndependentVillageSite < 0 || (save.IndependentVillageSite > 0
                && (!IsLand(map, ProvinceAt(map, save.IndependentVillageSite - 1))
                    || save.IndependentVillageSite - 1 == save.CapitalCell
                    || settlementSites.Contains(save.IndependentVillageSite - 1)
                    || save.Owned[ProvinceAt(map, save.IndependentVillageSite - 1)])))
                throw new ArgumentException("The save contains an invalid independent village.");

            if (save.IndependentVillageFoundedDay < 0 || save.IndependentVillageFoundedDay > save.Day
                || save.IndependentFarmSite < 0 || (save.IndependentVillageSite == 0
                    && (save.IndependentVillageFoundedDay != 0 || save.IndependentFarmSite != 0 || save.IndependentVillageGuarded))
                || (save.IndependentFarmSite > 0 && !IsLand(map, ProvinceAt(map, save.IndependentFarmSite - 1))))
                throw new ArgumentException("The save contains invalid independent village development.");

            if (save.RegionalCouncil && (save.Towns?.Contains(save.CapitalCell) != true || settlementSites.Count == 0))
                throw new ArgumentException("The save contains an invalid regional council.");

            if (save.TownSpecialisations != null)
            {
                var specialised = new HashSet<int>();
                foreach (var entry in save.TownSpecialisations)
                    if (save.Towns?.Contains(entry.Cell) != true || !specialised.Add(entry.Cell)
                        || !Enum.IsDefined(typeof(SettlementBuilding), entry.Building))
                        throw new ArgumentException("The save contains an invalid town specialisation.");
            }

            if (save.SettlementPopulations == null && settlementSites.Count > 0)
                foreach (var site in settlementSites)
                    if (!save.Owned[ProvinceAt(map, site)])
                        throw new ArgumentException("Independent regions require a saved population for each settlement.");

            if (save.SettlementPopulations != null)
            {
                if (save.SecondSettlementSite != 0 || save.SettlementPopulations.Count != 1 + settlementSites.Count)
                    throw new ArgumentException("The save contains mismatched settlement populations.");
                var totals = new Dictionary<int, long>();
                for (var i = 0; i < save.SettlementPopulations.Count; i++)
                {
                    var population = save.SettlementPopulations[i];
                    var region = i == 0 ? capital : ProvinceAt(map, save.SettlementCells[i - 1]);
                    totals.TryGetValue(region, out var total);
                    totals[region] = total + population;
                    if (population < 0) throw new ArgumentException("The save contains negative settlement population.");
                }
                foreach (var total in totals)
                    if (total.Value > save.Pop[total.Key] || (save.Owned[total.Key] && total.Value != save.Pop[total.Key]))
                        throw new ArgumentException("Settlement populations do not match their region total.");
            }

            if (save.Construction != null)
            {
                var builtSites = new HashSet<int>();
                foreach (var site in save.Construction)
                {
                    if ((site.Cell != save.CapitalCell && !settlementSites.Contains(site.Cell))
                        || !builtSites.Add(site.Cell) || site.Buildings == 0 || (site.Buildings & ~(int)AllBuildings) != 0
                        || !ValidStock(site.IronStock) || !ValidStock(site.IronInTransit)
                        || (site.IronInTransit == 0 ? site.IronArrivalDay != 0
                            : site.IronArrivalDay <= (site.IronHoldDay > 0 ? site.IronHoldDay : save.Day))
                        || (site.IronHoldDay != 0 && (site.IronInTransit == 0 || site.IronHoldDay < site.IronDepartureDay
                            || site.IronHoldDay > save.Day || site.IronHoldDay >= site.IronTravelEndDay
                            || site.IronTransport != IronSupplyRoute.Overland || site.IronTravelPath == null
                            || site.IronTravelPath.Length < 2))
                        || ((site.IronStock > 0 || site.IronInTransit > 0)
                            && (site.Buildings & (int)SettlementBuilding.IronMine) == 0))
                        throw new ArgumentException("The save contains invalid settlement construction.");
                    if (site.IronTravelPath != null && site.IronTravelPath.Length != 0)
                    {
                        if (site.IronInTransit == 0 || site.IronTravelPath.Length < 2 || site.IronDepartureDay < 1
                            || site.IronDepartureDay > save.Day || site.IronTravelEndDay <= site.IronDepartureDay
                            || site.IronTravelEndDay > site.IronArrivalDay
                            || (site.IronTransport != IronSupplyRoute.Overland && site.IronTransport != IronSupplyRoute.Waterway))
                            throw new ArgumentException("The save contains invalid iron transport progress.");
                        foreach (var cell in site.IronTravelPath)
                            if (cell < 0 || cell >= map.ProvinceAt.Length)
                                throw new ArgumentException("The save contains an invalid iron transport path.");
                    }
                    foreach (var building in new[] { SettlementBuilding.Farm, SettlementBuilding.LumberCamp,
                        SettlementBuilding.IronMine, SettlementBuilding.Docks })
                    {
                        var placement = site.Placement(building);
                        if (placement < 0 || (placement > 0 && ((site.Buildings & (int)building) == 0
                            || !IsLand(map, ProvinceAt(map, placement - 1)))))
                            throw new ArgumentException("The save contains an invalid building location.");
                    }
                }
            }

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
            if ((save.Perks & ~(CampaignPerk.Stewardship | CampaignPerk.TownPlanning | CampaignPerk.Envoys)) != 0
                || (save.Perks != 0 && !state.HasPerk(CampaignPerk.Stewardship)) || state.SkillPoints < 0)
                throw new ArgumentException("The save contains invalid clan skills.");
            var target = save.IntegrationTarget;
            var legacy = save.Version == 1;
            var pending = legacy && target >= 0 ? ExpansionApproach.Diplomacy : save.PendingApproach;
            if (target == -1 ? save.IntegrationDaysLeft != 0 || pending != ExpansionApproach.None
                : pending == ExpansionApproach.None || !Enum.IsDefined(typeof(ExpansionApproach), pending)
                    || save.IntegrationDaysLeft < 1 || save.IntegrationDaysLeft > DaysFor(pending)
                    || !IsLand(map, target) || state.Owns(target))
                throw new ArgumentException("The save contains invalid integration progress.");

            if (save.ObjectiveOccupiedDays < 0 || save.ObjectiveOccupiedDays > ForceDuration
                || (save.ObjectiveOccupiedDays > 0 && (pending != ExpansionApproach.Force || target != state.IndependentVillageRegion)))
                throw new ArgumentException("The save contains invalid occupation progress.");

            if (save.IntegrationSettlementSite != 0 || save.IntegrationProvinceRoot != 0)
            {
                var site = save.IntegrationSettlementSite - 1;
                var root = save.IntegrationProvinceRoot - 1;
                if (!state.UsesLocalIntegration || target < 0 || site < 0 || state.SettlementProvince(site) != target
                    || (root != -1 && root != state.CapitalCell && !save.SettlementProvinceRoots.Contains(root)))
                    throw new ArgumentException("The save contains an invalid integration settlement.");
            }

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
                save.Version = SaveVersion;
            }
            else
                state.ValidateTradeAgreements();
            return state;
        }

        public void SetSpeed(int speed)
        {
            if (!IsValidSpeed(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
            data.Speed = speed;
        }

        public bool CanReachIndependentProvince(int province)
        {
            if (IndependentPopulation(province) == 0) return false;
            if (LandContactResolver != null) return LandContactResolver(province);
            if (HasSettlementInRegion(province)) return true;
            var neighbours = map.Provinces[province].Neighbours;
            if (neighbours != null)
                foreach (var neighbour in neighbours)
                    if (Owns(neighbour) || HasSettlementInRegion(neighbour)) return true;
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
        {
            var site = map.Provinces[province];
            return building switch
            {
                SettlementBuilding.Farm => ("Farm", 25, 15, Fertility(site), "food",
                    IsTundra(site) ? "Farms cannot be built in tundra." : null),
                SettlementBuilding.LumberCamp => ("Lumber camp", 20, 10, TimberCoverage(site) * 2L >= site.PixelCount ? 3 : 2, "wood",
                    HasTimber(site) ? null : "Requires more than 25% forest cover."),
                SettlementBuilding.IronMine => ("Iron mine", 35, 20, Count(site, BiomeKind.Alpine) * 2L >= site.PixelCount ? 2 : 1, "iron",
                    IsMountain(site) ? null : "Requires mountainous terrain."),
                SettlementBuilding.Docks => ("Docks", 25, Tradition == ClanTradition.River ? 15 : 25, 1, null,
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

        private int LegacyBuildingSite(int province)
        {
            if (province == Capital) return CapitalCell;
            foreach (var cell in SettlementCells)
                if (SettlementProvince(cell) == province) return cell;
            return -1;
        }

        public SettlementBuilding SettlementBuildings(int cell)
        {
            var province = SettlementProvince(cell);
            if (!IsLand(map, province)) return 0;
            var buildings = LegacyBuildingSite(province) == cell ? data.Buildings[province] : 0;
            foreach (var site in data.Construction)
                if (site.Cell == cell) return (SettlementBuilding)(buildings | site.Buildings);
            return (SettlementBuilding)buildings;
        }

        public bool HasSettlementBuilding(int cell, SettlementBuilding building)
            => (SettlementBuildings(cell) & building) != 0;

        public (string Name, int Coin, int Wood, int Yield, string Resource, string SiteIssue)
            SettlementBuildingInfo(int cell, SettlementBuilding building, int placement)
        {
            var regional = BuildingInfo(SettlementProvince(cell), building);
            if (building == SettlementBuilding.Docks && placement >= 0 && map.HasDockWaterwayData)
            {
                regional.SiteIssue = map.DockWaterwayAt(placement) > 0 ? null : "Select a site beside a navigable waterfront.";
                return regional;
            }
            var localYield = placement >= 0 ? map.LocalProductionYield?.Invoke(placement, building) : null;
            if (!localYield.HasValue) return regional;
            regional.Yield = localYield.Value;
            regional.SiteIssue = localYield.Value > 0 ? null : "Select terrain suitable for this building.";
            return regional;
        }

        public bool CanBuildAtSettlement(int cell, SettlementBuilding building, int placement = -1)
        {
            if (cell != CapitalCell && !data.SettlementCells.Contains(cell)) return false;
            if (HasSettlementBuilding(cell, building) || placement < -1
                || (placement >= 0 && !IsLand(map, ProvinceAt(map, placement)))) return false;
            var info = SettlementBuildingInfo(cell, building, placement);
            return info.SiteIssue == null && Coin >= info.Coin && Wood >= info.Wood;
        }

        public int BuildingPlacement(int cell, SettlementBuilding building)
        {
            foreach (var site in data.Construction)
                if (site.Cell == cell) return site.Placement(building) - 1;
            return -1;
        }

        public bool TryBuildAtSettlement(int cell, SettlementBuilding building, int placement = -1)
        {
            if (!CanBuildAtSettlement(cell, building, placement)) return false;
            var info = SettlementBuildingInfo(cell, building, placement);
            var index = data.Construction.FindIndex(site => site.Cell == cell);
            var entry = index >= 0 ? data.Construction[index] : new SettlementConstruction { Cell = cell };
            entry.Buildings |= (int)building;
            entry.SetPlacement(building, placement);
            if (index >= 0) data.Construction[index] = entry;
            else data.Construction.Add(entry);
            data.Coin -= info.Coin;
            data.Wood -= info.Wood;
            if (building == SettlementBuilding.Docks) dockWaterways = null;
            return true;
        }

        private int BuildingTotal(SettlementBuilding building)
        {
            var total = 0;
            for (var province = 0; province < data.Buildings.Length; province++)
                if (HasBuilding(province, building))
                    total += BuildingInfo(province, building).Yield + ResearchProductionBonus(building);
            foreach (var site in data.Construction)
                if ((site.Buildings & (int)building) != 0)
                {
                    var yield = SettlementBuildingInfo(site.Cell, building, site.Placement(building) - 1).Yield;
                    total += (yield > 0 ? yield : BuildingInfo(SettlementProvince(site.Cell), building).Yield)
                        + ResearchProductionBonus(building);
                }
            foreach (var entry in data.TownSpecialisations)
                if (entry.Building == building && HasSettlementBuilding(entry.Cell, building)) total++;
            return total;
        }

        public int SettlementIronStock(int cell)
        {
            foreach (var site in data.Construction)
                if (site.Cell == cell) return site.IronStock;
            return 0;
        }

        public IronSupplyRoute GetIronSupplyRoute(int cell)
        {
            if (cell == CapitalCell) return IronSupplyRoute.Local;

            var dock = map.DockWaterwayAt(BuildingPlacement(cell, SettlementBuilding.Docks));
            if (dock > 0 && dock == map.DockWaterwayAt(BuildingPlacement(CapitalCell, SettlementBuilding.Docks)))
                return IronSupplyRoute.Waterway;

            return !map.HasMarchTerrain || GetOverlandIronRoute(cell) != null
                ? IronSupplyRoute.Overland : IronSupplyRoute.None;
        }

        public IReadOnlyList<int> GetOverlandIronRoute(int cell)
        {
            if (cell == CapitalCell || !map.HasMarchTerrain) return null;
            if (!ironSupplyRoutes.TryGetValue(cell, out var route))
            {
                route = map.FindWarbandRoute(cell, CapitalCell, CanUseSupplyFord, hasBridge: HasBridge, hasRoad: HasRoadEdge);
                ironSupplyRoutes[cell] = route;
            }
            return route;
        }

        public IReadOnlyList<int> GetWaterwayIronRoute(int cell)
        {
            if (GetIronSupplyRoute(cell) != IronSupplyRoute.Waterway) return null;
            if (!ironWaterRoutes.TryGetValue(cell, out var route))
            {
                route = map.FindDockRoute(BuildingPlacement(cell, SettlementBuilding.Docks),
                    BuildingPlacement(CapitalCell, SettlementBuilding.Docks));
                ironWaterRoutes[cell] = route;
            }
            return route;
        }

        public (int Amount, int Days) IronShipment(int cell)
        {
            foreach (var site in data.Construction)
                if (site.Cell == cell && site.IronInTransit > 0)
                    return (site.IronInTransit, Math.Max(0, site.IronArrivalDay - (site.IronHoldDay > 0 ? site.IronHoldDay : Day)));
            return (0, 0);
        }

        public bool IsIronShipmentHeld(int cell)
        {
            foreach (var site in data.Construction)
                if (site.Cell == cell) return site.IronHoldDay > 0;
            return false;
        }

        public bool TryRerouteHeldIronShipment(int cell)
        {
            if (Day == int.MaxValue) return false;
            var index = data.Construction.FindIndex(site => site.Cell == cell);
            if (index < 0) return false;
            var shipment = data.Construction[index];
            var path = shipment.IronTravelPath;
            if (shipment.IronHoldDay <= 0 || path == null || path.Length < 2) return false;

            var elapsed = shipment.IronHoldDay - shipment.IronDepartureDay;
            var duration = shipment.IronTravelEndDay - shipment.IronDepartureDay;
            var step = (int)((long)elapsed * (path.Length - 1) / duration);
            if (step > 0 && IsFordClosed(path[step])) step--;
            var alternate = map.FindWarbandRoute(path[step], CapitalCell, CanUseSupplyFord, hasBridge: HasBridge, hasRoad: HasRoadEdge);
            if (alternate == null) return false;

            SetIronJourney(ref shipment, alternate, IronSupplyRoute.Overland);
            data.Construction[index] = shipment;
            return true;
        }

        private void SetIronJourney(ref SettlementConstruction shipment, IReadOnlyList<int> route, IronSupplyRoute mode)
        {
            var cellsPerDay = Math.Max(1, map.Height / (mode == IronSupplyRoute.Waterway ? 32 : 64));
            var travelDays = Math.Max(1, (route.Count - 2) / cellsPerDay + 1);
            shipment.IronArrivalDay = (int)Math.Min(int.MaxValue, (long)Day + travelDays);
            shipment.IronDepartureDay = Day;
            shipment.IronTravelEndDay = shipment.IronArrivalDay;
            shipment.IronTransport = mode;
            shipment.IronTravelPath = new List<int>(route).ToArray();
            shipment.IronHoldDay = 0;
        }

        public (IReadOnlyList<int> Path, IronSupplyRoute Transport, float Progress) IronShipmentJourney(int cell)
        {
            foreach (var site in data.Construction)
            {
                if (site.Cell != cell || site.IronInTransit == 0 || site.IronTravelPath == null
                    || site.IronTravelPath.Length < 2) continue;
                var elapsed = site.IronHoldDay > 0 ? site.IronHoldDay - site.IronDepartureDay
                    : (Day - site.IronDepartureDay) + fractionOfDay;
                var duration = site.IronTravelEndDay - site.IronDepartureDay;
                return (site.IronTravelPath, site.IronTransport, (float)Math.Clamp(elapsed / duration, 0d, 1d));
            }
            return (null, IronSupplyRoute.None, 0f);
        }

        public bool HasIronSupplyRoute(int cell) => GetIronSupplyRoute(cell) != IronSupplyRoute.None;

        private void ProduceIron(int periods)
        {
            for (var province = 0; province < data.Buildings.Length; province++)
            {
                if (!HasBuilding(province, SettlementBuilding.IronMine)) continue;
                var yield = BuildingInfo(province, SettlementBuilding.IronMine).Yield;
                if (TownSpeciality(LegacyBuildingSite(province)) == SettlementBuilding.IronMine) yield++;
                data.Iron = (int)Math.Min(ResourceLimit, Iron + (long)periods * yield);
            }

            for (var i = 0; i < data.Construction.Count; i++)
            {
                var site = data.Construction[i];
                if ((site.Buildings & (int)SettlementBuilding.IronMine) == 0) continue;
                var yield = SettlementBuildingInfo(site.Cell, SettlementBuilding.IronMine, site.MineSite - 1).Yield;
                if (yield == 0) yield = BuildingInfo(SettlementProvince(site.Cell), SettlementBuilding.IronMine).Yield;
                if (TownSpeciality(site.Cell) == SettlementBuilding.IronMine) yield++;
                var available = site.IronStock + (long)periods * yield;
                var routeType = GetIronSupplyRoute(site.Cell);
                if (routeType == IronSupplyRoute.Local || (!map.HasMarchTerrain && routeType == IronSupplyRoute.Overland))
                {
                    var shipped = (int)Math.Min(ResourceLimit - Iron, available);
                    data.Iron += shipped;
                    available -= shipped;
                }
                else if (site.IronInTransit == 0 && available > 0 && Day < int.MaxValue)
                {
                    var route = routeType == IronSupplyRoute.Waterway ? GetWaterwayIronRoute(site.Cell)
                        : routeType == IronSupplyRoute.Overland ? GetOverlandIronRoute(site.Cell) : null;
                    if (route != null)
                    {
                        site.IronInTransit = (int)Math.Min(ResourceLimit, available);
                        SetIronJourney(ref site, route, routeType);
                        available -= site.IronInTransit;
                    }
                }
                site.IronStock = (int)Math.Min(ResourceLimit, available);
                data.Construction[i] = site;
            }
        }

        private void ReceiveIronShipments()
        {
            for (var i = 0; i < data.Construction.Count; i++)
            {
                var site = data.Construction[i];
                if (site.IronInTransit == 0 || site.IronHoldDay > 0 || site.IronArrivalDay > Day) continue;
                var unloaded = Math.Min(ResourceLimit - Iron, site.IronInTransit);
                data.Iron += unloaded;
                site.IronInTransit -= unloaded;
                if (site.IronInTransit == 0)
                    site.IronArrivalDay = 0;
                else if (Day == int.MaxValue)
                {
                    site.IronStock = Math.Min(ResourceLimit, site.IronStock + site.IronInTransit);
                    site.IronInTransit = site.IronArrivalDay = 0;
                }
                else
                    site.IronArrivalDay = (int)Math.Min(int.MaxValue, (long)Day + ProductionInterval);
                if (site.IronInTransit == 0)
                {
                    site.IronDepartureDay = site.IronTravelEndDay = 0;
                    site.IronTravelPath = null;
                    site.IronTransport = IronSupplyRoute.None;
                }
                data.Construction[i] = site;
            }
        }

        public bool TryBuildGranary(int cell = -1)
        {
            if (!CanBuildGranary || cell < -1 || cell >= map.ProvinceAt.Length
                || (cell >= 0 && !IsLand(map, ProvinceAt(map, cell)))) return false;
            data.Wood -= GranaryWood;
            data.Coin -= GranaryCoin;
            data.Granary = true;
            data.GranarySite = cell + 1;
            return true;
        }

        public bool TryRecruitWarband()
        {
            if (!CanRecruitWarband) return false;
            data.Wood -= WarbandRecruitWood;
            data.Coin -= WarbandRecruitCoin;
            LoseCapitalPopulation(WarbandRecruits);
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
            if (IndependentPopulation(province) == 0) return false;
            var waterways = map.Provinces[province].TradeWaterways;
            if (waterways == null || waterways.Length == 0) return false;

            if (dockWaterways == null)
            {
                dockWaterways = new HashSet<int>();
                for (var port = 0; port < map.Provinces.Length; port++)
                    if (HasBuilding(port, SettlementBuilding.Docks) && map.Provinces[port].TradeWaterways != null)
                        dockWaterways.UnionWith(map.Provinces[port].TradeWaterways);
                foreach (var site in data.Construction)
                {
                    if ((site.Buildings & (int)SettlementBuilding.Docks) == 0) continue;
                    var placement = site.Placement(SettlementBuilding.Docks) - 1;
                    var localWaterway = map.DockWaterwayAt(placement);
                    if (localWaterway > 0) dockWaterways.Add(localWaterway);
                    else
                    {
                        var regionalWaterways = map.Provinces[SettlementProvince(site.Cell)].TradeWaterways;
                        if (regionalWaterways != null) dockWaterways.UnionWith(regionalWaterways);
                    }
                }
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
                {
                    ExecuteTrade(agreement.Province, agreement.Offer);
                    if (data.CompletedTradeDeliveries < int.MaxValue) data.CompletedTradeDeliveries++;
                }
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
            if (WarbandCommitted && province == IntegrationTarget)
                return (0, "Trade suspended during the military operation.", 0);
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
            if (province == IndependentVillageRegion && IndependentFarmCell >= 0) food += 8;
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
                ExpansionApproach.Diplomacy => Coin >= NegotiationCoinCost,
                ExpansionApproach.Trade => HasTradedWith(province) && Coin >= PactCost,
                ExpansionApproach.Force => WarbandAt(province) && Food >= ForceFood,
                _ => false
            };
        }

        // Pay the chosen expansion cost up front and schedule the ownership change for a later simulation
        // day.
        public bool TryStartIntegration(int province, ExpansionApproach approach = ExpansionApproach.Diplomacy,
            int settlementCell = -1, int provinceRoot = -1)
        {
            if (!CanStartIntegration(province, approach)) return false;
            if (UsesLocalIntegration)
            {
                var existing = IntegrationCommunitySite(province);
                if (existing >= 0)
                {
                    settlementCell = existing;
                    if (existing != IndependentVillageCell) provinceRoot = -1;
                }
                if (SettlementProvince(settlementCell) != province
                    || (provinceRoot != -1 && provinceRoot != CapitalCell && !data.SettlementProvinceRoots.Contains(provinceRoot)))
                    return false;
                data.IntegrationSettlementSite = settlementCell + 1;
                data.IntegrationProvinceRoot = provinceRoot + 1;
            }
            if (approach == ExpansionApproach.Force) data.Food -= ForceFood;
            else data.Coin -= approach == ExpansionApproach.Trade ? PactCost : NegotiationCoinCost;
            data.IntegrationTarget = province;
            data.IntegrationDaysLeft = DaysFor(approach);
            data.PendingApproach = approach;
            if (WarbandCommitted) data.OperationMissedObjective = false;
            if (WarbandCommitted && data.PassagePartners.Contains(province + 1)) RefreshFordAccess();
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
                if (WarbandTravelDays > 0) days = days == 0 ? WarbandTravelDays : Math.Min(days, WarbandTravelDays);
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
                foreach (var site in data.Construction)
                {
                    if (site.Cell == CapitalCell || (site.Buildings & (int)SettlementBuilding.IronMine) == 0) continue;
                    span = Math.Min(span, ProductionInterval - Day % ProductionInterval);
                    if (site.IronInTransit > 0 && site.IronHoldDay == 0) span = Math.Min(span, site.IronArrivalDay - Day);
                }
                if (MilitaryObjectiveCell >= 0) span = Math.Min(span, 1);
                if (WarbandCommitted && IntegrationDaysLeft > ForceDuration - RivalFordResponseDays
                    && data.PassagePartners.Contains(IntegrationTarget + 1))
                    span = Math.Min(span, IntegrationDaysLeft - (ForceDuration - RivalFordResponseDays));
                if (IntegrationTarget >= 0 && IntegrationDaysLeft <= span)
                    span = Math.Max(1, IntegrationDaysLeft - 1);
                data.Day += span;
                AdvanceWarband(span);
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
            ReceiveIronShipments();
            var periods = Day / ProductionInterval - previousDay / ProductionInterval;
            var taxes = Day / TaxInterval - previousDay / TaxInterval;
            if (periods > 0)
            {
                var output = Day > WinterStart && Day <= WinterEnd ? WinterFoodOutput : WarmFoodOutput;
                if (Day == WinterStart) AddFood((long)(periods - 1) * output + FarmFood);
                else AddFood((long)periods * output);
                ProduceLocalFood(periods);

                var lumber = BuildingTotal(SettlementBuilding.LumberCamp);

                data.Wood = (int)Math.Min(ResourceLimit, Wood + (long)periods * lumber + taxes);
                ProduceIron(periods);
            }
            if (Day / FoodInterval > previousDay / FoodInterval)
            {
                ProcessTradeAgreements();
                FeedPopulation();
                if (data.LastPopulationChange != 0) InfluenceRevision++;
                AdvanceIndependentVillage();
            }
            if (taxes > 0)
            {
                if (HasRegionalCouncil) data.ResearchPoints = Math.Min(ResearchLimit, ResearchPoints + taxes);
                var collected = (int)Math.Min(ResourceLimit - Coin, taxes * Math.Min(ResourceLimit, TaxIncome));
                data.Coin += collected;
                RecordCoin(ref data.TaxCollected, collected);
            }
        }

        private void AdvanceIndependentVillage()
        {
            if (IndependentVillageCell >= 0)
            {
                var region = IndependentVillageRegion;
                var population = IndependentPopulation(region);
                if (population <= 0 || IndependentVillageOccupied) return;
                var growth = Math.Min(int.MaxValue - data.Pop[region], Math.Max(1, population / 100));
                data.Pop[region] += growth;
                if (growth > 0) InfluenceRevision++;
                if (data.IndependentVillageFoundedDay == 0) data.IndependentVillageFoundedDay = Day;
                if (population >= 80 && Day - data.IndependentVillageFoundedDay >= FoodInterval * 2)
                    data.IndependentVillageGuarded = true;
                if (IndependentFarmCell < 0 && Day - data.IndependentVillageFoundedDay >= FoodInterval
                    && IndependentFarmSiteResolver != null)
                {
                    var farm = IndependentFarmSiteResolver(IndependentVillageCell);
                    if (IsLand(map, ProvinceAt(map, farm))) data.IndependentFarmSite = farm + 1;
                }
                return;
            }
            if (!UsesLocalIntegration || IndependentVillageSiteResolver == null) return;

            for (var region = 0; region < map.Provinces.Length; region++)
            {
                if (region == IntegrationTarget || !CanReachIndependentProvince(region)) continue;
                var cell = IndependentVillageSiteResolver(region);
                if (cell < 0 || SettlementProvince(cell) != region || cell == CapitalCell || data.SettlementCells.Contains(cell))
                    continue;
                data.IndependentVillageSite = cell + 1;
                data.IndependentVillageFoundedDay = Day;
                InfluenceRevision++;
                break;
            }
        }

        private void ProduceLocalFood(int periods)
        {
            foreach (var cell in SettlementCells)
            {
                var index = data.SettlementFood.FindIndex(reserve => reserve.Cell == cell);
                if (!IsFoodIsolated(cell))
                {
                    if (index < 0) continue;
                    var reserve = data.SettlementFood[index];
                    var transferred = Math.Min(ResourceLimit - Food, reserve.Food);
                    AddFood(transferred);
                    reserve.Food -= transferred;
                    reserve.LastShortfall = 0;
                    if (reserve.Food == 0) data.SettlementFood.RemoveAt(index);
                    else data.SettlementFood[index] = reserve;
                    continue;
                }

                var local = index >= 0 ? data.SettlementFood[index] : new SettlementFoodReserve { Cell = cell };
                var subsistencePeriods = Day == WinterStart ? periods - 1
                    : Day < WinterStart || Day > WinterEnd ? periods : 0;
                local.Food = (int)Math.Min(ResourceLimit,
                    local.Food + (long)periods * SettlementFarmYield(cell) + subsistencePeriods);
                if (index >= 0) data.SettlementFood[index] = local;
                else data.SettlementFood.Add(local);
            }
        }

        // Connected settlements share clan reserves. Isolated villages feed themselves from local harvests.
        private void FeedPopulation()
        {
            var upkeep = FoodUpkeep;
            var balance = Food - upkeep;
            AddFood(-upkeep);
            data.LastPopulationChange = 0;
            if (balance < 0 && Day > WinterStart && Day <= WinterEnd)
                data.WinterShortfall = (int)Math.Min(int.MaxValue, data.WinterShortfall - balance);

            var settledRegions = new HashSet<int>();
            for (var i = 0; i < data.SettlementPopulations.Count; i++)
            {
                var site = SettlementCellAt(i);
                var region = SettlementProvince(site);
                settledRegions.Add(region);
                var siteUpkeep = upkeep;
                var siteBalance = balance;
                if (IsFoodIsolated(site))
                {
                    var index = data.SettlementFood.FindIndex(reserve => reserve.Cell == site);
                    var local = index >= 0 ? data.SettlementFood[index] : new SettlementFoodReserve { Cell = site };
                    siteUpkeep = LocalFoodUpkeep(site);
                    siteBalance = local.Food - siteUpkeep;
                    local.Food = (int)Math.Max(0, siteBalance);
                    local.LastShortfall = (int)Math.Max(0, -siteBalance);
                    if (index >= 0) data.SettlementFood[index] = local;
                    else data.SettlementFood.Add(local);
                }

                data.SettlementPopulations[i] += ApplyPopulationChange(region, data.SettlementPopulations[i],
                    siteUpkeep, siteBalance, site);
            }

            for (var region = 0; region < data.Pop.Length; region++)
                if (Owns(region) && !settledRegions.Contains(region))
                    ApplyPopulationChange(region, data.Pop[region], upkeep, balance, -1);

            int ApplyPopulationChange(int region, int population, long need, long surplus, int site)
            {
                if (need == 0 || surplus == 0) return 0;
                var starving = surplus < 0;
                var growth = (starving ? MaxStarvationRate : MaxGrowthRate)
                    * Math.Min(1d, Math.Abs((double)surplus) / need);
                if (!starving && site >= 0) growth *= Happiness(site, true) / 60d;
                var change = (int)Math.Ceiling(population * growth);
                change = starving ? -change : Math.Min(change, int.MaxValue - data.Pop[region]);
                data.Pop[region] += change;
                data.LastPopulationChange += change;
                return change;
            }
        }

        // Resolve the chosen approach, transfer the province and end any trade agreement with the former
        // independent market.
        private bool AdvanceIntegration(int days)
        {
            if (IntegrationTarget == -1) return false;
            data.IntegrationDaysLeft = Math.Max(0, IntegrationDaysLeft - days);
            if (WarbandCommitted && IntegrationDaysLeft <= ForceDuration - RivalFordResponseDays)
                ContestPassageFords(IntegrationTarget);
            if (MilitaryObjectiveCell >= 0)
                data.ObjectiveOccupiedDays = IndependentVillageOccupied ? ObjectiveOccupiedDays + days : 0;
            if (IntegrationDaysLeft > 0) return false;

            var target = IntegrationTarget;
            if (MilitaryObjectiveCell >= 0 && ObjectiveOccupiedDays < ObjectiveHoldDaysRequired)
            {
                data.OperationMissedObjective = true;
                ClearIntegration();
                return false;
            }
            if (PendingApproach == ExpansionApproach.Force)
            {
                var losses = IsDefensible(map.Provinces[target]) ? 18 : 8;
                LoseCapitalPopulation(HasIronWarband ? losses / 2 : losses);
                LosePopulation(target, 12);
                AddFood(38);
            }
            if (PendingApproach == ExpansionApproach.Diplomacy && NegotiatedIntegrations < int.MaxValue)
                data.NegotiatedIntegrations = NegotiatedIntegrations + 1;
            if (PendingApproach == ExpansionApproach.Force && SuccessfulOperations < int.MaxValue)
                data.SuccessfulOperations = SuccessfulOperations + 1;
            if (FirstExpansion == ExpansionApproach.None) data.FirstExpansion = PendingApproach;
            var remainingResidents = IndependentPopulation(target);
            var destination = IntegrationSettlementCell;
            if (destination < 0) destination = SettlementInRegion(target);
            if (destination >= 0)
            {
                var index = destination == CapitalCell ? 0 : data.SettlementCells.IndexOf(destination) + 1;
                if (index == 0 && destination != CapitalCell)
                {
                    data.SettlementCells.Add(destination);
                    var root = data.IntegrationProvinceRoot - 1;
                    data.SettlementProvinceRoots.Add(root >= 0 ? root : destination);
                    data.SettlementPopulations.Add(remainingResidents);
                    if (destination == IndependentVillageCell && IndependentFarmCell >= 0)
                        data.Construction.Add(new SettlementConstruction
                        {
                            Cell = destination, Buildings = (int)SettlementBuilding.Farm, FarmSite = data.IndependentFarmSite
                        });
                }
                else data.SettlementPopulations[index] += remainingResidents;
            }
            if (!UsesLocalIntegration || destination < 0) data.Owned[target] = true;
            if (IndependentVillageRegion == target)
            {
                data.IndependentVillageSite = data.IndependentVillageFoundedDay = data.IndependentFarmSite = 0;
                data.IndependentVillageGuarded = false;
            }
            InfluenceRevision++;
            CancelTradeAgreement(target);
            var releasedFords = data.RivalHeldFords.RemoveAll(
                ford => data.PassagePartners[data.PassageFords.IndexOf(ford)] == target + 1);
            ClearIntegration(releasedFords > 0);
            return true;
        }

        private void ContestPassageFords(int region)
        {
            var changed = false;
            for (var i = 0; i < data.PassageFords.Count; i++)
            {
                var ford = data.PassageFords[i];
                if (data.PassagePartners[i] != region + 1 || IsFordSecured(ford) || IsFordHeldByRival(ford)) continue;
                data.RivalHeldFords.Add(ford);
                changed = true;
            }
            if (changed) RefreshFordAccess();
        }

        private void ClearIntegration(bool fordControlChanged = false)
        {
            var passageSuspended = WarbandCommitted && data.PassagePartners.Contains(IntegrationTarget + 1);
            data.IntegrationTarget = -1;
            data.IntegrationDaysLeft = 0;
            data.ObjectiveOccupiedDays = 0;
            data.IntegrationSettlementSite = 0;
            data.IntegrationProvinceRoot = 0;
            data.PendingApproach = ExpansionApproach.None;
            if (passageSuspended || fordControlChanged) RefreshFordAccess();
        }

        private void AddFood(long amount) => data.Food = (int)Math.Min(ResourceLimit, Math.Max(0, Food + amount));

        private void LoseCapitalPopulation(int amount)
        {
            var loss = Math.Min(amount, Math.Max(0, data.SettlementPopulations[0] - 1));
            data.SettlementPopulations[0] -= loss;
            data.Pop[Capital] -= loss;
            if (loss > 0) InfluenceRevision++;
        }

        private void LosePopulation(int province, int amount)
        {
            var loss = Math.Min(amount, Math.Max(0, data.Pop[province] - 1));
            var civilians = IndependentPopulation(province);
            data.Pop[province] -= loss;
            if (loss > 0) InfluenceRevision++;
            loss = Math.Max(0, loss - civilians);
            for (var i = 0; i < data.SettlementPopulations.Count && loss > 0; i++)
            {
                if (SettlementProvince(SettlementCellAt(i)) != province) continue;
                var casualties = Math.Min(loss, data.SettlementPopulations[i]);
                data.SettlementPopulations[i] -= casualties;
                loss -= casualties;
            }
        }

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
