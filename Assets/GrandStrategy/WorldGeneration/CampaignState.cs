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
    internal enum SettlementLaw { Customary, VillageCommons, TownCharters }

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
        public int Goodwill;
        public int LastGoodwillPeriod;
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
        public bool IronTransferringToDock;
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
        public int Target;
        public int LastShortfall;
        public int LastReserveUsed;
        public int GranarySite; // Cell index plus one.
        public int InTransit;
        public int DepartureDay, ArrivalDay, HoldDay;
        public int[] OverlandPath;
        public bool TransferringToDock;
        public bool WaitingToUnload;
    }

    [Serializable]
    internal struct FordGuarantee
    {
        public int Ford;
        public int Region;
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
        public List<long> SettlementProvinceNumbers;
        public long NextProvinceNumber;
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
        public SettlementLaw SettlementLaw;
        public int SettlementLawChangedDay;
        public WarbandSupplyPolicy WarbandSupply;
        public bool[] Owned;
        public int[] Pop;
        public int IntegrationTarget;
        public bool LocalIntegration;
        public int IntegrationSettlementSite;
        public int IndependentVillageSite;
        public int IndependentOutpostSite;
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
        public int WarbandHomeSite; // Cell index plus one. Older campaigns default to the capital.
        public int WarbandRations;
        public bool WarbandShortOnFood;
        public int WarbandRouteIndex;
        public int WarbandMarchCredit;
        public List<int> WarbandRoute;
        public List<int> SecuredFords;
        public List<int> ClosedFords;
        public List<int> BridgedFords;
        public List<SettlementRoad> Roads;
        public List<int> PassageFords;
        public List<int> RivalHeldFords;
        public List<int> RivalFordRegions;
        public List<FordGuarantee> FordGuarantees;
        public bool PreviouslySecuredFord;
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
        public const int MaxLocalFoodTarget = 30;
        public const int VillageGranaryFoodTarget = 60;
        public const int VillageGranaryWood = 20;
        public const int VillageGranaryCoin = 25;
        public const int RoadWood = 25;
        public const int RoadCoin = 20;
        public const int BridgeWood = 20;
        public const int BridgeCoin = 15;
        public const int GarrisonRecruits = 4;
        public const int GarrisonCoin = 10;
        public const float VillageRadius = .008f;
        public const float TownRadius = .012f;
        public const int MaxTradeGoodwill = 3;
        public const int TrustedPartnerDiscount = 5;
        public const int FordRetakeFood = 5;
        public const int FordGuaranteeCoin = 10;
        public const int FordPassageCoin = 15;
        public const int RegionalCouncilCoin = 60;
        public const int SettlementLawCoin = 15;
        public const int SettlementLawReviewDays = 30;
        private const int SettlementLawWoodShift = 5;
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
        public int SettlementWoodCost => SettlementWoodCosts(ActiveSettlementLaw).Village;
        public int TownWoodCost => SettlementWoodCosts(ActiveSettlementLaw).Town;
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
        public const int WarbandRationCapacity = 6;
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
        private const int IndependentGuardPopulation = 80;
        private const int IndependentGuardAge = FoodInterval * 2;

        private readonly ProvinceMap map;
        private readonly CampaignSave data;
        private double fractionOfDay;
        private HashSet<int> dockWaterways;
        private long remainingMarchCost;
        private readonly HashSet<long> roadEdges = new HashSet<long>();
        private readonly Dictionary<int, List<int>> ironWaterRoutes = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int[]> foodWaterRoutes = new Dictionary<int, int[]>();
        private readonly Dictionary<int, List<int>> ironSupplyRoutes = new Dictionary<int, List<int>>();
        internal Func<int, bool> LandContactResolver { get; set; }
        internal Func<int, int> IndependentVillageSiteResolver { get; set; }
        internal Func<int, int> IndependentOutpostSiteResolver { get; set; }
        internal Func<int, int> IndependentFarmSiteResolver { get; set; }
        internal Func<int, bool> IndependentTerritoryResolver { get; set; }
        internal Func<int, bool> MilitaryOccupationResolver { get; set; }
        internal Func<int, HashSet<int>> DetachedClaimResolver { get; set; }
        internal Func<int, int, bool> ProvinceBorderResolver { get; set; }
        internal Func<int, bool> ProvinceSeatConnectionResolver { get; set; }
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
            data.PreviouslySecuredFord |= data.SecuredFords.Count > 0;
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
            if (data.RivalFordRegions == null)
            {
                data.RivalFordRegions = new List<int>();
                foreach (var ford in data.RivalHeldFords)
                {
                    var index = data.PassageFords.IndexOf(ford);
                    data.RivalFordRegions.Add(index >= 0 ? data.PassagePartners[index] - 1 : -1);
                }
            }
            if (data.RivalFordRegions.Count != data.RivalHeldFords.Count)
                throw new ArgumentException("The save contains mismatched rival crossing claims.");
            var rivalFords = new HashSet<int>();
            for (var i = 0; i < data.RivalHeldFords.Count; i++)
            {
                var ford = data.RivalHeldFords[i];
                if (!map.IsFord(ford) || !IsLand(map, data.RivalFordRegions[i]) || IsFordSecured(ford) || !rivalFords.Add(ford))
                    throw new ArgumentException("The save contains invalid rival ford control.");
            }

            data.FordGuarantees ??= new List<FordGuarantee>();
            var guarantees = new HashSet<int>();
            foreach (var guarantee in data.FordGuarantees)
                if (!uniqueFords.Contains(guarantee.Ford) || !IsLand(map, guarantee.Region)
                    || !guarantees.Add(guarantee.Ford))
                    throw new ArgumentException("The save contains an invalid ford guarantee.");

            data.BridgedFords ??= new List<int>();
            var bridges = new HashSet<int>();
            foreach (var ford in data.BridgedFords)
                if (!map.IsFord(ford) || !bridges.Add(ford))
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
            if (data.SettlementProvinceNumbers == null)
            {
                // Older saves numbered seats in settlement order.
                data.NextProvinceNumber = 2;
                data.SettlementProvinceNumbers = new List<long>(data.SettlementCells.Count);
                for (var i = 0; i < data.SettlementCells.Count; i++)
                    data.SettlementProvinceNumbers.Add(
                        data.SettlementProvinceRoots[i] == data.SettlementCells[i] ? data.NextProvinceNumber++ : 0);
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

        public long ProvinceNumber(int seat)
        {
            if (seat == CapitalCell) return 1;
            var index = data.SettlementCells.IndexOf(seat);
            return index < 0 ? 0 : data.SettlementProvinceNumbers[index];
        }

        private void AppendSettlementProvince(int cell, int root)
        {
            data.SettlementCells.Add(cell);
            data.SettlementProvinceRoots.Add(root);
            data.SettlementProvinceNumbers.Add(cell == root ? data.NextProvinceNumber++ : 0);
        }

        public bool CanFoundSettlement(int cell)
        {
            var province = SettlementProvince(cell);
            return cell != CapitalCell && cell != IndependentVillageCell && cell != IndependentOutpostCell && IsLand(map, province) && province != IntegrationTarget
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
            AppendSettlementProvince(cell, provinceRoot == -1 ? cell : provinceRoot);
            AllocateSettlers(cell, party.Donor, party.People, Owns(SettlementProvince(cell)));
            InfluenceRevision++;
            return true;
        }

        public bool TryEstablishDetachedProvince(int seat)
        {
            var index = data.SettlementCells.IndexOf(seat);
            if (index < 0 || data.SettlementPopulations[index + 1] == 0) return false;
            var oldSeat = data.SettlementProvinceRoots[index];
            if (oldSeat == seat) return false;

            var fragment = DetachedClaimResolver?.Invoke(seat);
            if (fragment == null || !fragment.Contains(seat) || fragment.Contains(oldSeat)) return false;

            for (var i = 0; i < data.SettlementCells.Count; i++)
                if (data.SettlementProvinceRoots[i] == oldSeat && fragment.Contains(data.SettlementCells[i]))
                    data.SettlementProvinceRoots[i] = seat;

            data.SettlementProvinceNumbers[index] = data.NextProvinceNumber++;
            if (data.IntegrationProvinceRoot == oldSeat + 1 && fragment.Contains(IntegrationSettlementCell))
                data.IntegrationProvinceRoot = seat + 1;
            InfluenceRevision++;
            return true;
        }

        public bool TryMergeProvinces(int sourceSeat, int destinationSeat)
        {
            if (sourceSeat == CapitalCell || sourceSeat == destinationSeat || !IsProvinceSeat(sourceSeat)
                || !IsProvinceSeat(destinationSeat) || ProvinceBorderResolver?.Invoke(sourceSeat, destinationSeat) != true)
                return false;

            ReassignProvinceSeat(sourceSeat, destinationSeat);
            return true;
        }

        public bool CanMoveProvinceSeat(int site)
        {
            var index = data.SettlementCells.IndexOf(site);
            if (index < 0 || data.SettlementPopulations[index + 1] == 0) return false;
            var seat = data.SettlementProvinceRoots[index];
            return seat != CapitalCell && seat != site && ProvinceSeatConnectionResolver?.Invoke(site) == true;
        }

        public bool TryMoveProvinceSeat(int site)
        {
            if (!CanMoveProvinceSeat(site)) return false;
            ReassignProvinceSeat(data.SettlementProvinceRoots[data.SettlementCells.IndexOf(site)], site);
            return true;
        }

        private void ReassignProvinceSeat(int oldSeat, int newSeat)
        {
            var oldIndex = data.SettlementCells.IndexOf(oldSeat);
            var newIndex = data.SettlementCells.IndexOf(newSeat);
            if (newIndex >= 0 && data.SettlementProvinceNumbers[newIndex] == 0)
                data.SettlementProvinceNumbers[newIndex] = data.SettlementProvinceNumbers[oldIndex];
            data.SettlementProvinceNumbers[oldIndex] = 0;

            for (var i = 0; i < data.SettlementProvinceRoots.Count; i++)
                if (data.SettlementProvinceRoots[i] == oldSeat)
                    data.SettlementProvinceRoots[i] = newSeat;

            if (data.IntegrationProvinceRoot == oldSeat + 1)
                data.IntegrationProvinceRoot = newSeat + 1;
            InfluenceRevision++;
        }

        private void ReplaceVacantProvinceSeats()
        {
            for (var i = 0; i < data.SettlementCells.Count; i++)
            {
                var seat = data.SettlementCells[i];
                if (data.SettlementProvinceRoots[i] != seat || data.SettlementPopulations[i + 1] != 0) continue;

                var successor = -1;
                var largestPopulation = 0;
                for (var member = 0; member < data.SettlementCells.Count; member++)
                {
                    var population = data.SettlementPopulations[member + 1];
                    if (data.SettlementProvinceRoots[member] != seat || population <= largestPopulation) continue;
                    successor = data.SettlementCells[member];
                    largestPopulation = population;
                }
                if (successor >= 0) ReassignProvinceSeat(seat, successor);
            }
        }

        private bool IsProvinceSeat(int site)
        {
            var index = data.SettlementCells.IndexOf(site);
            return site == CapitalCell || (index >= 0 && data.SettlementProvinceRoots[index] == site);
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
        public int IndependentOutpostCell => data.IndependentOutpostSite - 1;
        public int IndependentVillageCell => data.IndependentVillageSite - 1;
        public bool IndependentVillageOccupied => IndependentVillageCell >= 0 && WarbandCommitted
            && !WarbandShortOnFood && MilitaryOccupationResolver?.Invoke(IndependentVillageCell) == true;
        public bool IndependentVillageGuarded => data.IndependentVillageGuarded;
        public int IndependentFarmCell => data.IndependentFarmSite - 1;
        public int IndependentVillageRegion => SettlementProvince(IndependentVillageCell);

        public int IntegrationCommunitySite(int region)
            => region >= 0 && region == IndependentVillageRegion ? IndependentVillageCell : SettlementInRegion(region);

        public int IntegrationSettlementCell => data.IntegrationSettlementSite - 1;
        public bool IntegrationNeedsSettlement => UsesLocalIntegration && IntegrationTarget >= 0 && IntegrationSettlementCell < 0;

        internal void UseSettlementClaims(Func<int, int> influenceRootAt = null)
        {
            data.LocalIntegration = true;
            if (!IntegrationNeedsSettlement) return;

            var site = IntegrationCommunitySite(IntegrationTarget);
            if (site < 0) site = IndependentVillageSiteResolver?.Invoke(IntegrationTarget) ?? -1;
            if (IsIntegrationSite(IntegrationTarget, site))
                TrySetIntegrationSettlement(site, influenceRootAt?.Invoke(site) ?? -1);
        }

        public bool TrySetIntegrationSettlement(int cell, int provinceRoot = -1)
            => IntegrationNeedsSettlement && SetIntegrationSite(IntegrationTarget, cell, provinceRoot);

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
            var granary = (cell == CapitalCell ? HasGranary : GranaryCellAt(cell) >= 0) ? 5 : 0;
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

        public int GranaryCellAt(int cell)
        {
            if (cell == CapitalCell) return GranaryCell;
            foreach (var reserve in data.SettlementFood)
                if (reserve.Cell == cell) return reserve.GranarySite - 1;
            return -1;
        }

        public int LocalFoodTargetLimit(int cell)
            => GranaryCellAt(cell) >= 0 && cell != CapitalCell ? VillageGranaryFoodTarget : MaxLocalFoodTarget;
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

        public IReadOnlyList<int> RivalHeldFords => data.RivalHeldFords;
        public bool IsFordHeldByRival(int cell) => data.RivalHeldFords.Contains(cell);

        public int RivalFordHolder(int cell)
        {
            var index = data.RivalHeldFords.IndexOf(cell);
            return index >= 0 ? data.RivalFordRegions[index] : -1;
        }

        private void SetRivalFord(int ford, int region)
        {
            data.RivalHeldFords.Add(ford);
            data.RivalFordRegions.Add(region);
        }

        private bool ClearRivalFord(int ford)
        {
            var index = data.RivalHeldFords.IndexOf(ford);
            if (index < 0) return false;
            data.RivalHeldFords.RemoveAt(index);
            data.RivalFordRegions.RemoveAt(index);
            return true;
        }
        public bool IsFordClosed(int cell)
            => data.ClosedFords.Contains(cell) || IsPassageSuspended(cell) || IsFordHeldByRival(cell);
        public bool HasFordPassage(int cell) => data.PassageFords.Contains(cell);
        public bool CanUseSupplyFord(int cell) => !IsFordClosed(cell) && (IsFordSecured(cell) || HasFordPassage(cell));

        public int FordGuaranteeHolder(int ford)
        {
            foreach (var pact in data.FordGuarantees)
                if (pact.Ford == ford) return pact.Region;
            return -1;
        }

        public int FordGuaranteePartner(int ford)
        {
            var region = IndependentVillageRegion;
            if (!IsFordSecured(ford) || FordGuaranteeHolder(ford) >= 0 || !IsTrustedTradePartner(region)
                || !CanReachIndependentProvince(region) || (WarbandCommitted && IntegrationTarget == region)
                || !RivalClaimsFordBank(ford)) return -1;
            return region;
        }

        public bool TryGuaranteeFord(int ford)
        {
            var partner = FordGuaranteePartner(ford);
            if (partner < 0 || Coin < FordGuaranteeCoin) return false;
            data.Coin -= FordGuaranteeCoin;
            data.FordGuarantees.Add(new FordGuarantee { Ford = ford, Region = partner });
            return true;
        }

        public int FordPassagePartner(int ford)
        {
            if (!map.IsFord(ford) || IsFordSecured(ford)) return -1;
            var holder = RivalFordHolder(ford);
            if (holder >= 0)
                return CanReachIndependentProvince(holder) && (!WarbandCommitted || IntegrationTarget != holder)
                    ? holder : -1;
            if (HasFordPassage(ford)) return -1;
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
            if (partner < 0 || Coin < FordPassageCostWith(partner)) return false;
            data.Coin -= FordPassageCostWith(partner);
            ClearRivalFord(ford);
            GrantFordPassage(ford, partner);
            RefreshFordAccess();
            return true;
        }

        private void GrantFordPassage(int ford, int partner)
        {
            var index = data.PassageFords.IndexOf(ford);
            if (index >= 0)
                data.PassagePartners[index] = partner + 1;
            else
            {
                data.PassageFords.Add(ford);
                data.PassagePartners.Add(partner + 1);
            }
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
            data.PreviouslySecuredFord = true;
            if (ClearRivalFord(cell)) data.Food -= FordRetakeFood;
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
                var blocked = WaitingAtFord(path, positionDay, shipment.IronDepartureDay, shipment.IronTravelEndDay) >= 0;

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

        private bool HasPotentialFordClosures => data.ClosedFords.Count > 0 || data.RivalHeldFords.Count > 0
            || (WarbandCommitted && data.PassageFords.Count > 0);

        private (int Ford, int ApproachDay) NextClosedFord(int[] path, int positionDay, int departureDay, int arrivalDay)
        {
            if (path == null || path.Length < 3 || positionDay >= arrivalDay || !HasPotentialFordClosures)
                return (-1, int.MaxValue);

            var legs = path.Length - 1;
            var duration = arrivalDay - departureDay;
            var elapsed = (long)(positionDay - departureDay) * legs;
            var first = (int)Math.Max(1, (elapsed + duration - 1) / duration);
            for (var step = first; step < legs; step++)
            {
                if (!IsFordClosed(path[step])) continue;

                // Stop at the last whole campaign day on or before the near bank.
                var approach = departureDay + (int)((long)(step - 1) * duration / legs);
                return (path[step], approach);
            }
            return (-1, int.MaxValue);
        }

        private int WaitingAtFord(int[] path, int positionDay, int departureDay, int arrivalDay)
        {
            var next = NextClosedFord(path, positionDay, departureDay, arrivalDay);
            return next.ApproachDay <= positionDay ? next.Ford : -1;
        }

        public int DaysUntilShipmentWait(int village, TradeResource resource)
        {
            if (resource == TradeResource.Food)
            {
                foreach (var reserve in data.SettlementFood)
                    if (reserve.Cell == village && reserve.InTransit > 0 && reserve.HoldDay == 0)
                        return DaysUntilShipmentWait(NextClosedFord(reserve.OverlandPath, Day, reserve.DepartureDay, reserve.ArrivalDay));
            }
            else if (resource == TradeResource.Iron)
            {
                foreach (var shipment in data.Construction)
                    if (shipment.Cell == village && shipment.IronInTransit > 0 && shipment.IronHoldDay == 0
                        && shipment.IronTransport == IronSupplyRoute.Overland)
                        return DaysUntilShipmentWait(NextClosedFord(shipment.IronTravelPath, Day,
                            shipment.IronDepartureDay, shipment.IronTravelEndDay));
            }
            return -1;
        }

        private int DaysUntilShipmentWait((int Ford, int ApproachDay) next)
            => next.Ford >= 0 ? Math.Max(0, next.ApproachDay - Day) : -1;

        public IEnumerable<(int Origin, int Destination, TradeResource Resource, int Amount, int DaysToFord, bool Held)>
            ShipmentsThreatenedByFord(int ford)
        {
            if (!map.IsFord(ford) || !IsFordClosed(ford)) yield break;

            foreach (var reserve in data.SettlementFood)
            {
                if (reserve.InTransit == 0 || reserve.OverlandPath == null) continue;
                var held = reserve.HoldDay > 0;
                var next = NextClosedFord(reserve.OverlandPath, held ? reserve.HoldDay : Day,
                    reserve.DepartureDay, reserve.ArrivalDay);
                if (next.Ford == ford)
                    yield return (CapitalCell, reserve.Cell, TradeResource.Food, reserve.InTransit,
                        held ? 0 : DaysUntilShipmentWait(next), held);
            }

            foreach (var shipment in data.Construction)
            {
                if (shipment.IronInTransit == 0 || shipment.IronTransport != IronSupplyRoute.Overland) continue;
                var held = shipment.IronHoldDay > 0;
                var next = NextClosedFord(shipment.IronTravelPath, held ? shipment.IronHoldDay : Day,
                    shipment.IronDepartureDay, shipment.IronTravelEndDay);
                if (next.Ford == ford)
                    yield return (shipment.Cell, CapitalCell, TradeResource.Iron, shipment.IronInTransit,
                        held ? 0 : DaysUntilShipmentWait(next), held);
            }
        }

        public IEnumerable<(int Origin, int Destination, TradeResource Resource, int Amount)> HeldShipmentsAtFord(int ford)
        {
            foreach (var shipment in ShipmentsThreatenedByFord(ford))
                if (shipment.Held) yield return (shipment.Origin, shipment.Destination, shipment.Resource, shipment.Amount);
        }

        public (int Shipments, long Cargo) SuppliesHeldAtFord(int ford) => CargoHeldAtFord(ford, TradeResource.Iron);
        public (int Shipments, long Cargo) FoodHeldAtFord(int ford) => CargoHeldAtFord(ford, TradeResource.Food);

        private (int Shipments, long Cargo) CargoHeldAtFord(int ford, TradeResource resource)
        {
            var count = 0;
            long cargo = 0;
            foreach (var shipment in HeldShipmentsAtFord(ford))
            {
                if (shipment.Resource != resource) continue;
                count++;
                cargo += shipment.Amount;
            }
            return (count, cargo);
        }

        private void UpdateHeldFoodShipments()
        {
            for (var i = 0; i < data.SettlementFood.Count; i++)
            {
                var reserve = data.SettlementFood[i];
                if (reserve.OverlandPath == null || (reserve.HoldDay == 0 && Day >= reserve.ArrivalDay)) continue;
                var positionDay = reserve.HoldDay > 0 ? reserve.HoldDay : Day;
                var blocked = WaitingAtFord(reserve.OverlandPath, positionDay, reserve.DepartureDay, reserve.ArrivalDay) >= 0;
                if (blocked && reserve.HoldDay == 0)
                    reserve.HoldDay = Day;
                else if (!blocked && reserve.HoldDay > 0)
                {
                    var delay = Day - reserve.HoldDay;
                    reserve.DepartureDay += delay;
                    reserve.ArrivalDay = (int)Math.Min(int.MaxValue, (long)reserve.ArrivalDay + delay);
                    reserve.HoldDay = 0;
                }
                data.SettlementFood[i] = reserve;
            }
        }

        private void RefreshFordAccess()
        {
            ironSupplyRoutes.Clear();
            InfluenceRevision++;
            FordAccessRevision++;
            UpdateSupplyWaits();
        }

        private void UpdateSupplyWaits()
        {
            UpdateHeldShipments();
            UpdateHeldFoodShipments();
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

        public float MilitaryPressure
        {
            get
            {
                if (!WarbandCommitted) return 0f;
                var supplyMultiplier = WarbandShortOnFood ? .40f : WarbandSupply == WarbandSupplyPolicy.Forage ? .75f : 1f;
                return (ForceDuration - IntegrationDaysLeft + 1f) / ForceDuration * supplyMultiplier;
            }
        }
        public bool WarbandAt(int province) => HasWarband && WarbandTravelDays == 0
            && province >= 0 && ProvinceAt(map, WarbandCell) == province;
        public int WarbandHomeCell => data.WarbandHomeSite > 0 ? data.WarbandHomeSite - 1 : CapitalCell;
        public int WarbandCell => !HasWarband ? -1
            : data.WarbandRoute == null ? WarbandHomeCell : data.WarbandRoute[data.WarbandRouteIndex];
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

        public int WarbandRations => data.WarbandRations;
        public bool WarbandShortOnFood => data.WarbandShortOnFood;

        private int WarbandSupplySettlement => !HasWarband || WarbandCommitted || WarbandTravelDays > 0
            || SettlementPopulation(WarbandCell) == 0 ? -1 : WarbandCell;

        public int PackableWarbandRations
        {
            get
            {
                var site = WarbandSupplySettlement;
                var surplus = site < 0 ? 0
                    : site == CapitalCell ? Math.Max(0, Food - FoodUpkeep - FoodReserve)
                    : Math.Max(0, LocalFoodStock(site) - LocalFoodUpkeep(site));
                return (int)Math.Min(WarbandRationCapacity - WarbandRations, surplus);
            }
        }

        public bool TryPackWarbandRations()
        {
            var amount = PackableWarbandRations;
            if (amount == 0) return false;
            DrawWarbandFood(WarbandSupplySettlement, amount);
            data.WarbandRations += amount;
            if (data.WarbandRations >= WarbandFoodCost) data.WarbandShortOnFood = false;
            return true;
        }

        private void SupplyWarband()
        {
            var need = WarbandFoodCost;
            if (need == 0)
            {
                if (WarbandShortOnFood && map.IsMarchCell(WarbandCell) && !map.IsFord(WarbandCell))
                    data.WarbandShortOnFood = false;
                return;
            }

            var site = WarbandSupplySettlement;
            if (site >= 0) need -= DrawWarbandFood(site, need);
            var carried = Math.Min(data.WarbandRations, need);
            data.WarbandRations -= carried;
            data.WarbandShortOnFood = carried < need;
            if (WarbandShortOnFood) data.ObjectiveOccupiedDays = 0;
        }

        private int DrawWarbandFood(int site, int need)
        {
            if (site == CapitalCell)
            {
                var drawn = Math.Min(Food, need);
                AddFood(-drawn);
                return drawn;
            }

            var index = data.SettlementFood.FindIndex(reserve => reserve.Cell == site);
            if (index < 0) return 0;
            var local = data.SettlementFood[index];
            var amount = Math.Min(need, Math.Max(0, local.Food - LocalFoodUpkeep(site)));
            local.Food -= amount;
            data.SettlementFood[index] = local;
            return amount;
        }

        private int WarbandCellsPerDay
        {
            get
            {
                var normal = Math.Max(1, map.Height / 128);
                if (HasTechnology(CampaignTechnology.MarchLogistics)) normal++;
                var effectiveSupply = WarbandShortOnFood ? WarbandSupplyPolicy.Forage : WarbandSupply;
                return effectiveSupply switch
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

        public int LocalFoodTarget(int cell)
        {
            foreach (var reserve in data.SettlementFood)
                if (reserve.Cell == cell) return reserve.Target;
            return 0;
        }

        public bool TrySetLocalFoodTarget(int cell, int target)
        {
            if (target < 0 || !data.SettlementCells.Contains(cell) || target > LocalFoodTargetLimit(cell)) return false;
            var index = data.SettlementFood.FindIndex(reserve => reserve.Cell == cell);
            var reserve = index >= 0 ? data.SettlementFood[index] : new SettlementFoodReserve { Cell = cell };
            reserve.Target = target;
            if (index >= 0) data.SettlementFood[index] = reserve;
            else if (target > 0) data.SettlementFood.Add(reserve);
            return true;
        }

        public (int Amount, int Days, bool Held) LocalFoodShipment(int cell)
        {
            foreach (var reserve in data.SettlementFood)
                if (reserve.Cell == cell && reserve.InTransit > 0)
                    return (reserve.InTransit, reserve.ArrivalDay - (reserve.HoldDay > 0 ? reserve.HoldDay : Day),
                        reserve.HoldDay > 0);
            return default;
        }

        public int LocalFoodShortfall(int cell)
        {
            foreach (var reserve in data.SettlementFood)
                if (reserve.Cell == cell) return reserve.LastShortfall;
            return 0;
        }

        public int LocalFoodUsedLastMeal(int cell)
        {
            foreach (var reserve in data.SettlementFood)
                if (reserve.Cell == cell) return reserve.LastReserveUsed;
            return 0;
        }

        private bool HasFoodForUpkeep(int cell)
        {
            var meal = SettlementMealOutlook(cell);
            return meal.Food >= meal.Need;
        }

        public (long Food, long Need, bool DrawsLocalReserve) SettlementMealOutlook(int cell)
        {
            if (IsFoodIsolated(cell)) return (LocalFoodStock(cell), LocalFoodUpkeep(cell), true);
            var plan = PlanFoodUpkeep();
            if (plan.ReserveMeals?.Contains(cell) != true) return (Food, plan.SharedNeed, false);
            var need = LocalFoodUpkeep(cell);
            return (Math.Min(need, LocalFoodStock(cell)), need, true);
        }

        public long FoodUpkeep
        {
            get
            {
                var demand = SharedFoodDemand();
                return FoodFor(demand.People, demand.Guards);
            }
        }

        private (long People, int Guards) SharedFoodDemand()
        {
            var people = OwnedPopulation;
            var guards = data.Garrisons.Count;
            foreach (var cell in SettlementCells)
            {
                if (!IsFoodIsolated(cell)) continue;
                people -= SettlementPopulation(cell);
                if (HasGarrison(cell)) guards--;
            }
            return (people, guards);
        }

        private static long FoodFor(long people, int guards) => (people + PeoplePerFood - 1) / PeoplePerFood + guards;

        private (long SharedNeed, HashSet<int> ReserveMeals) PlanFoodUpkeep()
        {
            var (people, guards) = SharedFoodDemand();
            var need = FoodFor(people, guards);
            HashSet<int> reserveMeals = null;
            foreach (var reserve in data.SettlementFood)
            {
                if (Food >= need) break;
                if (reserve.Food == 0 || IsFoodIsolated(reserve.Cell)) continue;
                var villageNeed = LocalFoodUpkeep(reserve.Cell);
                if (villageNeed == 0 || (long)reserve.Food * need <= (long)Food * villageNeed) continue;

                // Feed this village locally and leave its share of the pooled food for other settlements.
                (reserveMeals ??= new HashSet<int>()).Add(reserve.Cell);
                people -= SettlementPopulation(reserve.Cell);
                if (HasGarrison(reserve.Cell)) guards--;
                need = FoodFor(people, guards);
            }
            return (need, reserveMeals);
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
                var warbandDraw = WarbandSupplySettlement == CapitalCell ? WarbandFoodCost : 0;
                return Math.Max(0, meals * FoodUpkeep - (long)production * (WinterFoodOutput - warbandDraw));
            }
        }
        private int WinterFoodOutput => FarmFood - (HasGranary ? 0 : 2);
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
                var food = FarmFood;
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
        public bool HasEverSecuredFord => data.PreviouslySecuredFord;
        public bool TradeMilestoneComplete => CompletedTradeDeliveries >= TradeMilestoneDeliveries;
        public bool DiplomacyMilestoneComplete => NegotiatedIntegrations >= DiplomacyMilestoneIntegrations;
        public bool MilitaryMilestoneComplete => SuccessfulOperations >= MilitaryMilestoneOperations && HasEverSecuredFord;
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
        public SettlementLaw ActiveSettlementLaw => data.SettlementLaw;
        public int SettlementLawReviewIn => data.SettlementLawChangedDay == 0
            ? 0 : Math.Max(0, SettlementLawReviewDays - (Day - data.SettlementLawChangedDay));

        public (int Village, int Town) SettlementWoodCosts(SettlementLaw law)
        {
            var shift = law switch
            {
                SettlementLaw.VillageCommons => -SettlementLawWoodShift,
                SettlementLaw.TownCharters => SettlementLawWoodShift,
                _ => 0
            };
            var village = SettlementWood + shift - (HasPerk(CampaignPerk.Stewardship) ? 5 : 0);
            var town = TownWood - shift - (HasPerk(CampaignPerk.TownPlanning) ? 5 : 0);
            return (village, town);
        }

        public bool CanEnactSettlementLaw(SettlementLaw law)
            => HasRegionalCouncil && law != ActiveSettlementLaw && Enum.IsDefined(typeof(SettlementLaw), law)
                && SettlementLawReviewIn == 0 && Coin >= SettlementLawCoin;

        public bool TryEnactSettlementLaw(SettlementLaw law)
        {
            if (!CanEnactSettlementLaw(law)) return false;
            data.Coin -= SettlementLawCoin;
            data.SettlementLaw = law;
            data.SettlementLawChangedDay = Day;
            return true;
        }

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
        public bool CanRecruitWarband => CanRecruitWarbandAt(CapitalCell);

        public bool CanRecruitWarbandAt(int site)
            => !HasWarband && Wood >= WarbandRecruitWood && Coin >= WarbandRecruitCoin
                && SettlementPopulation(site) > WarbandRecruits && map.IsMarchCell(site) && !map.IsFord(site);

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

            if (!Enum.IsDefined(typeof(SettlementLaw), save.SettlementLaw)
                || save.SettlementLawChangedDay < 0 || save.SettlementLawChangedDay > save.Day
                || (save.SettlementLawChangedDay != 0 && !save.RegionalCouncil)
                || (save.SettlementLaw != SettlementLaw.Customary && save.SettlementLawChangedDay == 0))
                throw new ArgumentException("The save contains an invalid settlement law.");

            if (save.SettlementProvinceRoots != null)
            {
                if (save.SecondSettlementSite != 0 || save.SettlementProvinceRoots.Count != (save.SettlementCells?.Count ?? 0))
                    throw new ArgumentException("The save contains mismatched settlement provinces.");
                var roots = new HashSet<int> { save.CapitalCell };
                for (var i = 0; i < save.SettlementProvinceRoots.Count; i++)
                    if (save.SettlementProvinceRoots[i] == save.SettlementCells[i]) roots.Add(save.SettlementCells[i]);
                foreach (var root in save.SettlementProvinceRoots)
                    if (!roots.Contains(root))
                        throw new ArgumentException("The save contains an unknown settlement province.");
            }

            if (save.SettlementProvinceNumbers == null)
            {
                if (save.NextProvinceNumber != 0)
                    throw new ArgumentException("The save contains a province numbering counter without province numbers.");
            }
            else
            {
                if (save.NextProvinceNumber < 2 || save.SettlementProvinceNumbers.Count != save.SettlementProvinceRoots?.Count)
                    throw new ArgumentException("The save contains mismatched province numbers.");
                var numbers = new HashSet<long>();
                for (var i = 0; i < save.SettlementProvinceNumbers.Count; i++)
                {
                    var number = save.SettlementProvinceNumbers[i];
                    var isSeat = save.SettlementCells[i] == save.SettlementProvinceRoots[i];
                    if (isSeat ? number < 2 || number >= save.NextProvinceNumber || !numbers.Add(number) : number != 0)
                        throw new ArgumentException("The save contains an invalid province number.");
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

            if (save.WarbandRations < 0 || save.WarbandRations > WarbandRationCapacity
                || (!save.Warband && (save.WarbandRations != 0 || save.WarbandShortOnFood)))
                throw new ArgumentException("The save contains invalid warband rations.");

            if (save.WarbandHomeSite < 0 || (save.WarbandHomeSite > 0 && (!save.Warband
                || (save.WarbandHomeSite - 1 != save.CapitalCell && !settlementSites.Contains(save.WarbandHomeSite - 1)))))
                throw new ArgumentException("The save contains an invalid warband home settlement.");

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
                var granarySites = new HashSet<int> { save.GranarySite };
                foreach (var reserve in save.SettlementFood)
                {
                    var targetLimit = reserve.GranarySite == 0 ? MaxLocalFoodTarget : VillageGranaryFoodTarget;
                    if (!settlementSites.Contains(reserve.Cell) || !foodSites.Add(reserve.Cell)
                        || !ValidStock(reserve.Food) || !ValidStock(reserve.LastReserveUsed) || reserve.LastShortfall < 0
                        || reserve.Target < 0 || reserve.Target > targetLimit
                        || reserve.GranarySite < 0 || (reserve.GranarySite > 0
                            && (!granarySites.Add(reserve.GranarySite)
                                || !IsVillageGranaryGround(map, reserve.Cell, reserve.GranarySite - 1))))
                        throw new ArgumentException("The save contains an invalid local food reserve.");
                    var path = reserve.OverlandPath;
                    if (reserve.InTransit < 0 || reserve.InTransit > targetLimit
                        || (reserve.WaitingToUnload && (reserve.InTransit == 0 || path != null || reserve.HoldDay != 0))
                        || (reserve.InTransit == 0 && (path != null || reserve.DepartureDay != 0
                            || reserve.ArrivalDay != 0 || reserve.HoldDay != 0))
                        || (reserve.InTransit > 0 && (reserve.DepartureDay < 1 || reserve.DepartureDay > save.Day
                            || reserve.ArrivalDay <= reserve.DepartureDay
                            || (reserve.HoldDay == 0 ? reserve.ArrivalDay <= save.Day
                                : path == null || reserve.HoldDay < reserve.DepartureDay
                                    || reserve.HoldDay > save.Day || reserve.HoldDay >= reserve.ArrivalDay))))
                        throw new ArgumentException("The save contains an invalid food delivery.");
                    if (path == null) continue;
                    if (path.Length < 2 || (!reserve.TransferringToDock && path[path.Length - 1] != reserve.Cell))
                        throw new ArgumentException("The save contains an invalid food delivery route.");
                    for (var i = 1; i < path.Length; i++)
                        if (!map.CanMarchBetween(path[i - 1], path[i]))
                            throw new ArgumentException("The save contains an invalid food delivery segment.");
                }
            }

            if (save.IndependentVillageSite < 0 || (save.IndependentVillageSite > 0
                && (!IsLand(map, ProvinceAt(map, save.IndependentVillageSite - 1))
                    || save.IndependentVillageSite - 1 == save.CapitalCell
                    || settlementSites.Contains(save.IndependentVillageSite - 1)
                    || save.Owned[ProvinceAt(map, save.IndependentVillageSite - 1)])))
                throw new ArgumentException("The save contains an invalid independent village.");

            if (save.IndependentOutpostSite < 0 || (save.IndependentOutpostSite > 0
                && (save.IndependentVillageSite == 0 || save.IndependentOutpostSite == save.IndependentVillageSite
                    || save.IndependentOutpostSite - 1 == save.CapitalCell || settlementSites.Contains(save.IndependentOutpostSite - 1)
                    || ProvinceAt(map, save.IndependentOutpostSite - 1) != ProvinceAt(map, save.IndependentVillageSite - 1))))
                throw new ArgumentException("The save contains an invalid independent outpost.");

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
                        || market.Goodwill < 0 || market.Goodwill > MaxTradeGoodwill
                        || market.LastGoodwillPeriod < 0 || market.LastGoodwillPeriod > market.Period
                        || (market.Goodwill > 0 && (!market.Traded || market.LastGoodwillPeriod == 0))
                        || !ValidStock(market.Coin) || !ValidStock(market.Food)
                        || !ValidStock(market.Wood) || !ValidStock(market.Iron))
                        throw new ArgumentException("The save contains invalid neighbour stock.");
            }

            var state = new CampaignState(map, save);
            state.ValidateDockTransfers();
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
                if (!state.UsesLocalIntegration || target < 0 || !state.IsIntegrationSite(target, site)
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
            state.ReplaceVacantProvinceSeats();
            if (state.WarbandShortOnFood) save.ObjectiveOccupiedDays = 0;
            if (state.Day < int.MaxValue) state.UpdateSupplyWaits();
            return state;
        }

        private void ValidateDockTransfers()
        {
            foreach (var reserve in data.SettlementFood)
                if (reserve.TransferringToDock && (reserve.InTransit == 0 || !IsDockTransferPath(reserve.OverlandPath,
                    GetFoodWaterRoute(reserve.Cell))))
                    throw new ArgumentException("The save contains an invalid food transfer to a dock.");

            foreach (var site in data.Construction)
                if (site.IronTransferringToDock && (site.IronInTransit == 0 || site.IronTransport != IronSupplyRoute.Overland
                    || site.IronArrivalDay != site.IronTravelEndDay
                    || !IsDockTransferPath(site.IronTravelPath, GetWaterwayIronRoute(site.Cell))))
                    throw new ArgumentException("The save contains an invalid iron transfer to a dock.");
        }

        private bool IsDockTransferPath(int[] path, IReadOnlyList<int> waterRoute)
        {
            if (path == null || path.Length < 2 || waterRoute == null || path[path.Length - 1] != waterRoute[0])
                return false;
            for (var i = 1; i < path.Length; i++)
                if (!map.CanMarchBetween(path[i - 1], path[i])) return false;
            return true;
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

        private IReadOnlyList<int> GetFoodWaterRoute(int cell)
        {
            var route = GetWaterwayIronRoute(cell);
            if (route == null) return null;
            if (!foodWaterRoutes.TryGetValue(cell, out var reversed))
            {
                reversed = new List<int>(route).ToArray();
                Array.Reverse(reversed);
                foodWaterRoutes[cell] = reversed;
            }
            return reversed;
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

        public bool TryRerouteIronShipment(int cell, bool byWater = false)
        {
            if (Day == int.MaxValue) return false;
            var index = data.Construction.FindIndex(site => site.Cell == cell);
            if (index < 0) return false;
            var shipment = data.Construction[index];
            var path = shipment.IronTravelPath;
            if (shipment.IronInTransit == 0 || shipment.IronTransport != IronSupplyRoute.Overland) return false;

            var waterRoute = byWater ? GetWaterwayIronRoute(cell) : null;
            if (byWater && waterRoute == null) return false;
            var positionDay = shipment.IronHoldDay > 0 ? shipment.IronHoldDay : Day;
            var alternate = FindAlternateSupplyRoute(path, positionDay, shipment.IronDepartureDay,
                shipment.IronTravelEndDay, CapitalCell, waterRoute);
            if (alternate.Path == null) return false;

            SetIronJourney(ref shipment, alternate.Path, alternate.Transport);
            shipment.IronTransferringToDock = byWater && alternate.Transport == IronSupplyRoute.Overland;
            data.Construction[index] = shipment;
            return true;
        }

        public bool TryRerouteFoodShipment(int cell, bool byWater = false)
        {
            if (Day == int.MaxValue) return false;
            var index = data.SettlementFood.FindIndex(reserve => reserve.Cell == cell);
            if (index < 0) return false;
            var reserve = data.SettlementFood[index];
            if (reserve.InTransit == 0) return false;

            var waterRoute = byWater ? GetFoodWaterRoute(cell) : null;
            if (byWater && waterRoute == null) return false;
            var positionDay = reserve.HoldDay > 0 ? reserve.HoldDay : Day;
            var alternate = FindAlternateSupplyRoute(reserve.OverlandPath, positionDay,
                reserve.DepartureDay, reserve.ArrivalDay, cell, waterRoute);
            if (alternate.Path == null) return false;

            SetFoodJourney(ref reserve, alternate.Path, alternate.Transport);
            reserve.TransferringToDock = byWater && alternate.Transport == IronSupplyRoute.Overland;
            data.SettlementFood[index] = reserve;
            return true;
        }

        private (IReadOnlyList<int> Path, IronSupplyRoute Transport) FindAlternateSupplyRoute(int[] path, int positionDay,
            int departureDay, int arrivalDay, int destination, IReadOnlyList<int> waterRoute)
        {
            if (NextClosedFord(path, positionDay, departureDay, arrivalDay).Ford < 0) return default;
            var step = (int)((long)(positionDay - departureDay) * (path.Length - 1) / (arrivalDay - departureDay));
            if (step > 0 && IsFordClosed(path[step])) step--;
            var start = path[step];
            var goal = waterRoute == null ? destination : waterRoute[0];
            if (waterRoute != null && start == goal) return (waterRoute, IronSupplyRoute.Waterway);
            return (map.FindWarbandRoute(start, goal, CanUseSupplyFord, hasBridge: HasBridge, hasRoad: HasRoadEdge),
                IronSupplyRoute.Overland);
        }

        public bool IsShipmentTransferringToDock(int village, TradeResource resource)
        {
            if (resource == TradeResource.Food)
            {
                foreach (var reserve in data.SettlementFood)
                    if (reserve.Cell == village) return reserve.TransferringToDock;
            }
            else if (resource == TradeResource.Iron)
            {
                foreach (var site in data.Construction)
                    if (site.Cell == village) return site.IronTransferringToDock;
            }
            return false;
        }

        private void SetIronJourney(ref SettlementConstruction shipment, IReadOnlyList<int> route, IronSupplyRoute mode)
        {
            shipment.IronArrivalDay = SupplyArrivalDay(route, mode);
            shipment.IronDepartureDay = Day;
            shipment.IronTravelEndDay = shipment.IronArrivalDay;
            shipment.IronTransport = mode;
            shipment.IronTravelPath = new List<int>(route).ToArray();
            shipment.IronHoldDay = 0;
            shipment.IronTransferringToDock = false;
        }

        private int SupplyArrivalDay(IReadOnlyList<int> route, IronSupplyRoute mode)
        {
            var cellsPerDay = Math.Max(1, map.Height / (mode == IronSupplyRoute.Waterway ? 32 : 64));
            var travelDays = Math.Max(1, (route.Count - 2) / cellsPerDay + 1);
            return (int)Math.Min(int.MaxValue, (long)Day + travelDays);
        }

        public (IReadOnlyList<int> Path, IronSupplyRoute Transport, float Progress) IronShipmentJourney(int cell)
        {
            foreach (var site in data.Construction)
            {
                if (site.Cell != cell || site.IronInTransit == 0 || site.IronTravelPath == null
                    || site.IronTravelPath.Length < 2) continue;
                return (site.IronTravelPath, site.IronTransport,
                    ShipmentProgress(site.IronDepartureDay, site.IronTravelEndDay, site.IronHoldDay));
            }
            return (null, IronSupplyRoute.None, 0f);
        }

        public (IReadOnlyList<int> Path, IronSupplyRoute Transport, float Progress, bool Held) FoodShipmentJourney(int cell)
        {
            foreach (var reserve in data.SettlementFood)
            {
                if (reserve.Cell != cell || reserve.InTransit == 0 || reserve.WaitingToUnload) continue;
                var path = reserve.OverlandPath ?? GetFoodWaterRoute(cell);
                if (path == null || path.Count < 2) break;
                var mode = reserve.OverlandPath == null ? IronSupplyRoute.Waterway : IronSupplyRoute.Overland;
                return (path, mode, ShipmentProgress(reserve.DepartureDay, reserve.ArrivalDay, reserve.HoldDay),
                    reserve.HoldDay > 0);
            }
            return default;
        }

        private float ShipmentProgress(int departure, int arrival, int hold)
        {
            var elapsed = hold > 0 ? hold - departure : (Day - departure) + fractionOfDay;
            return (float)Math.Clamp(elapsed / (arrival - departure), 0d, 1d);
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

                if (site.IronTransferringToDock)
                {
                    SetIronJourney(ref site, GetWaterwayIronRoute(site.Cell), IronSupplyRoute.Waterway);
                    data.Construction[i] = site;
                    if (site.IronArrivalDay > Day) continue;
                }
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

        public bool CanBuildVillageGranary(int village)
            => data.SettlementCells.Contains(village) && GranaryCellAt(village) < 0
                && Wood >= VillageGranaryWood && Coin >= VillageGranaryCoin;

        public bool TryBuildVillageGranary(int village, int placement)
        {
            if (!CanBuildVillageGranary(village) || !IsVillageGranaryGround(map, village, placement)
                || placement == GranaryCell) return false;
            foreach (var stored in data.SettlementFood)
                if (stored.GranarySite == placement + 1) return false;

            var index = data.SettlementFood.FindIndex(reserve => reserve.Cell == village);
            var local = index >= 0 ? data.SettlementFood[index] : new SettlementFoodReserve { Cell = village };
            local.GranarySite = placement + 1;
            if (index >= 0) data.SettlementFood[index] = local;
            else data.SettlementFood.Add(local);
            data.Wood -= VillageGranaryWood;
            data.Coin -= VillageGranaryCoin;
            return true;
        }

        private static bool IsVillageGranaryGround(ProvinceMap map, int village, int placement)
            => placement != village && IsLand(map, ProvinceAt(map, placement))
                && (!map.HasMarchTerrain || (map.IsMarchCell(placement) && !map.IsFord(placement)));

        public bool TryBuildGranary(int cell = -1)
        {
            if (!CanBuildGranary || cell < -1 || cell >= map.ProvinceAt.Length
                || (cell >= 0 && !IsLand(map, ProvinceAt(map, cell)))) return false;
            foreach (var stored in data.SettlementFood)
                if (cell >= 0 && stored.GranarySite == cell + 1) return false;
            data.Wood -= GranaryWood;
            data.Coin -= GranaryCoin;
            data.Granary = true;
            data.GranarySite = cell + 1;
            return true;
        }

        public bool TryRecruitWarband() => TryRecruitWarbandAt(CapitalCell);

        public bool TryRecruitWarbandAt(int site)
        {
            if (!CanRecruitWarbandAt(site)) return false;
            data.Wood -= WarbandRecruitWood;
            data.Coin -= WarbandRecruitCoin;
            LoseSettlementPopulation(site, WarbandRecruits);
            data.Warband = true;
            data.WarbandHomeSite = site + 1;
            SetWarbandRoute(new[] { site });
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

        public int TradeGoodwill(int province) => province >= 0 && province < data.Markets.Length
            ? data.Markets[province].Goodwill : 0;
        public bool IsTrustedTradePartner(int province) => TradeGoodwill(province) >= MaxTradeGoodwill;
        public int NegotiationCostWith(int province)
            => NegotiationCoinCost - (IsTrustedTradePartner(province) ? TrustedPartnerDiscount : 0);
        public int FordPassageCostWith(int province)
            => FordPassageCoin - (IsTrustedTradePartner(province) ? TrustedPartnerDiscount : 0);

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
            if (market.LastGoodwillPeriod != market.Period)
            {
                market.Goodwill = Math.Min(MaxTradeGoodwill, market.Goodwill + 1);
                market.LastGoodwillPeriod = market.Period;
            }
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
                Goodwill = market.Goodwill,
                LastGoodwillPeriod = market.LastGoodwillPeriod,
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
                ExpansionApproach.Diplomacy => Coin >= NegotiationCostWith(province),
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
            if (UsesLocalIntegration && !SetIntegrationSite(province, settlementCell, provinceRoot)) return false;
            if (approach == ExpansionApproach.Force)
            {
                data.Food -= ForceFood;
                var partner = data.Markets[province];
                partner.Goodwill = 0;
                data.Markets[province] = partner;
                data.FordGuarantees.RemoveAll(pact => pact.Region == province);
            }
            else data.Coin -= approach == ExpansionApproach.Trade ? PactCost : NegotiationCostWith(province);
            data.IntegrationTarget = province;
            data.IntegrationDaysLeft = DaysFor(approach);
            data.PendingApproach = approach;
            if (WarbandCommitted) data.OperationMissedObjective = false;
            if (WarbandCommitted && data.PassagePartners.Contains(province + 1)) RefreshFordAccess();
            return true;
        }

        private bool SetIntegrationSite(int province, int cell, int provinceRoot)
        {
            var existing = IntegrationCommunitySite(province);
            if (existing >= 0)
            {
                cell = existing;
                if (existing != IndependentVillageCell) provinceRoot = -1;
            }
            if (!IsIntegrationSite(province, cell)
                || (provinceRoot != -1 && provinceRoot != CapitalCell && !data.SettlementProvinceRoots.Contains(provinceRoot)))
                return false;

            data.IntegrationSettlementSite = cell + 1;
            data.IntegrationProvinceRoot = provinceRoot + 1;
            return true;
        }

        private bool IsIntegrationSite(int province, int cell)
            => cell >= 0 && SettlementProvince(cell) == province
                && (!map.HasMarchTerrain || (map.IsMarchCell(cell) && !map.IsFord(cell)));

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
                var lawReview = SettlementLawReviewIn;
                if (lawReview > 0 && lawReview <= int.MaxValue - Day)
                    days = days == 0 ? lawReview : Math.Min(days, lawReview);
                if (IntegrationTarget < 0 || IntegrationNeedsSettlement) return days;
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
                if (WarbandFoodCost > 0 || WarbandShortOnFood)
                    span = Math.Min(span, ProductionInterval - Day % ProductionInterval);
                foreach (var site in data.Construction)
                {
                    if (site.Cell == CapitalCell || (site.Buildings & (int)SettlementBuilding.IronMine) == 0) continue;
                    span = Math.Min(span, ProductionInterval - Day % ProductionInterval);
                    if (site.IronInTransit > 0 && site.IronHoldDay == 0)
                    {
                        span = Math.Min(span, site.IronArrivalDay - Day);
                        if (site.IronTransport == IronSupplyRoute.Overland)
                            span = LimitToFordApproach(span, site.IronTravelPath, site.IronDepartureDay, site.IronTravelEndDay);
                    }
                }
                foreach (var reserve in data.SettlementFood)
                {
                    if (reserve.Target > reserve.Food && reserve.InTransit == 0)
                        span = Math.Min(span, ProductionInterval - Day % ProductionInterval);
                    if (reserve.InTransit > 0 && reserve.HoldDay == 0)
                        span = LimitToFordApproach(Math.Min(span, reserve.ArrivalDay - Day), reserve.OverlandPath,
                            reserve.DepartureDay, reserve.ArrivalDay);
                }
                if (IntegrationTarget >= 0 && !IntegrationNeedsSettlement)
                {
                    if (MilitaryObjectiveCell >= 0) span = Math.Min(span, 1);
                    if (WarbandCommitted && IntegrationDaysLeft > ForceDuration - RivalFordResponseDays
                        && data.PassagePartners.Contains(IntegrationTarget + 1))
                        span = Math.Min(span, IntegrationDaysLeft - (ForceDuration - RivalFordResponseDays));
                    if (IntegrationDaysLeft <= span) span = Math.Max(1, IntegrationDaysLeft - 1);
                }
                data.Day += span;
                AdvanceWarband(span);
                expanded |= AdvanceIntegration(span);
                if (HasPotentialFordClosures) UpdateSupplyWaits();
                ApplyEconomy(previousDay);
            }

            if (days != 0 && (Day == WinterStart || Day == WinterEnd)) Paused = true;
            return expanded;
        }

        private int LimitToFordApproach(int span, int[] path, int departureDay, int arrivalDay)
        {
            var next = NextClosedFord(path, Day, departureDay, arrivalDay);
            return next.Ford < 0 ? span : Math.Min(span, Math.Max(1, next.ApproachDay - Day));
        }

        // Apply production, deliveries, upkeep and taxes in that order. Taxes use the population remaining
        // after food upkeep.
        private void ApplyEconomy(int previousDay)
        {
            ReceiveIronShipments();
            ReceiveFoodShipments();
            var periods = Day / ProductionInterval - previousDay / ProductionInterval;
            var taxes = Day / TaxInterval - previousDay / TaxInterval;
            if (periods > 0)
            {
                var output = Day > WinterStart && Day <= WinterEnd ? WinterFoodOutput : WarmFoodOutput;
                if (Day == WinterStart) AddFood((long)(periods - 1) * output + FarmFood);
                else AddFood((long)periods * output);
                SupplyWarband();
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
                if (population >= IndependentGuardPopulation && Day - data.IndependentVillageFoundedDay >= IndependentGuardAge)
                    data.IndependentVillageGuarded = true;
                if (IndependentVillageGuarded) ContestUnguardedPlayerFord(region);
                if (IndependentOutpostCell < 0 && IndependentFarmCell >= 0 && population >= 150
                    && Day - data.IndependentVillageFoundedDay >= FoodInterval * 3 && IndependentOutpostSiteResolver != null)
                {
                    var outpost = IndependentOutpostSiteResolver(IndependentVillageCell);
                    if (outpost != IndependentVillageCell && outpost != CapitalCell && SettlementProvince(outpost) == region
                        && !data.SettlementCells.Contains(outpost))
                    {
                        data.IndependentOutpostSite = outpost + 1;
                        InfluenceRevision++;
                    }
                }
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
                    if (reserve.Food > reserve.Target)
                    {
                        var returned = Math.Min(ResourceLimit - Food, reserve.Food - reserve.Target);
                        AddFood(returned);
                        reserve.Food -= returned;
                    }
                    else if (reserve.Food < reserve.Target && reserve.InTransit == 0)
                        DispatchFoodShipment(ref reserve);
                    if (reserve.Food == 0 && reserve.Target == 0 && reserve.InTransit == 0
                        && reserve.LastShortfall == 0 && reserve.LastReserveUsed == 0 && reserve.GranarySite == 0)
                        data.SettlementFood.RemoveAt(index);
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

        private void DispatchFoodShipment(ref SettlementFoodReserve reserve)
        {
            // Reserve the next clan meal and the protected stock before committing a delivery.
            var surplus = Math.Max(0, Food - FoodUpkeep - FoodReserve);
            var amount = (int)Math.Min(surplus, reserve.Target - reserve.Food);
            if (amount == 0) return;

            var mode = GetIronSupplyRoute(reserve.Cell);
            if (!map.HasMarchTerrain && mode == IronSupplyRoute.Overland)
            {
                reserve.Food += amount;
                AddFood(-amount);
                return;
            }

            var route = mode == IronSupplyRoute.Waterway
                ? GetFoodWaterRoute(reserve.Cell) : GetOverlandIronRoute(reserve.Cell);
            if (route == null || Day == int.MaxValue) return;
            reserve.InTransit = amount;
            SetFoodJourney(ref reserve, route, mode, reverse: mode == IronSupplyRoute.Overland);
            AddFood(-amount);
        }

        private void SetFoodJourney(ref SettlementFoodReserve reserve, IReadOnlyList<int> route, IronSupplyRoute mode,
            bool reverse = false)
        {
            reserve.DepartureDay = Day;
            reserve.ArrivalDay = SupplyArrivalDay(route, mode);
            reserve.HoldDay = 0;
            reserve.TransferringToDock = false;
            reserve.OverlandPath = mode == IronSupplyRoute.Overland ? new List<int>(route).ToArray() : null;
            if (reverse) Array.Reverse(reserve.OverlandPath);
        }

        private void ReceiveFoodShipments()
        {
            for (var i = 0; i < data.SettlementFood.Count; i++)
            {
                var reserve = data.SettlementFood[i];
                if (reserve.InTransit == 0 || reserve.HoldDay > 0 || reserve.ArrivalDay > Day) continue;

                if (reserve.TransferringToDock)
                {
                    SetFoodJourney(ref reserve, GetFoodWaterRoute(reserve.Cell), IronSupplyRoute.Waterway);
                    data.SettlementFood[i] = reserve;
                    if (reserve.ArrivalDay > Day) continue;
                }
                var unloaded = Math.Min(ResourceLimit - reserve.Food, reserve.InTransit);
                reserve.Food += unloaded;
                reserve.InTransit -= unloaded;
                if (reserve.InTransit == 0 || Day == int.MaxValue)
                {
                    if (reserve.InTransit > 0) AddFood(reserve.InTransit);
                    reserve.InTransit = reserve.DepartureDay = reserve.ArrivalDay = 0;
                    reserve.OverlandPath = null;
                    reserve.WaitingToUnload = false;
                }
                else
                {
                    // The cargo is already at the village. Retry unloading without rechecking the crossing.
                    reserve.OverlandPath = null;
                    reserve.WaitingToUnload = true;
                    reserve.ArrivalDay = (int)Math.Min(int.MaxValue, (long)Day + ProductionInterval);
                }
                data.SettlementFood[i] = reserve;
            }
        }

        // Isolated villages always eat locally. Connected villages can draw on their reserves during shortages.
        private void FeedPopulation()
        {
            var (upkeep, reserveMeals) = PlanFoodUpkeep();
            var balance = Food - upkeep;
            var missedFood = Math.Max(0, -balance);
            AddFood(-upkeep);
            data.LastPopulationChange = 0;

            var settledRegions = new HashSet<int>();
            for (var i = 0; i < data.SettlementPopulations.Count; i++)
            {
                var site = SettlementCellAt(i);
                var region = SettlementProvince(site);
                settledRegions.Add(region);
                var siteUpkeep = upkeep;
                var siteBalance = balance;
                if (site != CapitalCell)
                {
                    var isolated = IsFoodIsolated(site);
                    var index = data.SettlementFood.FindIndex(reserve => reserve.Cell == site);
                    if (isolated || reserveMeals?.Contains(site) == true)
                    {
                        var local = index >= 0 ? data.SettlementFood[index] : new SettlementFoodReserve { Cell = site };
                        siteUpkeep = LocalFoodUpkeep(site);
                        siteBalance = local.Food - siteUpkeep;
                        local.LastReserveUsed = (int)Math.Min(local.Food, siteUpkeep);
                        local.Food -= local.LastReserveUsed;
                        local.LastShortfall = (int)Math.Max(0, -siteBalance);
                        missedFood += local.LastShortfall;
                        if (!isolated) siteBalance = Math.Min(0, siteBalance);
                        if (index >= 0) data.SettlementFood[index] = local;
                        else data.SettlementFood.Add(local);
                    }
                    else if (index >= 0)
                    {
                        var local = data.SettlementFood[index];
                        local.LastReserveUsed = 0;
                        local.LastShortfall = 0;
                        data.SettlementFood[index] = local;
                    }
                }

                data.SettlementPopulations[i] += ApplyPopulationChange(region, data.SettlementPopulations[i],
                    siteUpkeep, siteBalance, site);
            }

            for (var region = 0; region < data.Pop.Length; region++)
                if (Owns(region) && !settledRegions.Contains(region))
                    ApplyPopulationChange(region, data.Pop[region], upkeep, balance, -1);

            ReplaceVacantProvinceSeats();
            if (Day > WinterStart && Day <= WinterEnd)
                data.WinterShortfall = (int)Math.Min(int.MaxValue, data.WinterShortfall + missedFood);

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

        // Resolve the chosen approach and end any trade agreement with the former independent market.
        private bool AdvanceIntegration(int days)
        {
            if (IntegrationTarget == -1 || IntegrationNeedsSettlement) return false;
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
                LoseSettlementPopulation(WarbandHomeCell, HasIronWarband ? losses / 2 : losses);
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
                    var root = data.IntegrationProvinceRoot - 1;
                    var provinceRoot = root >= 0 ? root : destination;
                    AppendSettlementProvince(destination, provinceRoot);
                    var outpostResidents = destination == IndependentVillageCell && IndependentOutpostCell >= 0
                        ? remainingResidents / 3 : 0;
                    data.SettlementPopulations.Add(remainingResidents - outpostResidents);
                    if (destination == IndependentVillageCell && IndependentOutpostCell >= 0)
                    {
                        AppendSettlementProvince(IndependentOutpostCell, provinceRoot);
                        data.SettlementPopulations.Add(outpostResidents);
                    }
                    if (destination == IndependentVillageCell && IndependentFarmCell >= 0)
                        data.Construction.Add(new SettlementConstruction
                        {
                            Cell = destination, Buildings = (int)SettlementBuilding.Farm, FarmSite = data.IndependentFarmSite
                        });
                }
                else data.SettlementPopulations[index] += remainingResidents;
            }
            ReplaceVacantProvinceSeats();
            if (!UsesLocalIntegration) data.Owned[target] = true;
            if (IndependentVillageRegion == target)
            {
                data.IndependentVillageSite = data.IndependentVillageFoundedDay = data.IndependentFarmSite = 0;
                data.IndependentOutpostSite = 0;
                data.IndependentVillageGuarded = false;
            }
            InfluenceRevision++;
            CancelTradeAgreement(target);
            data.FordGuarantees.RemoveAll(pact => pact.Region == target);
            var releasedFords = false;
            for (var index = data.RivalHeldFords.Count - 1; index >= 0; index--)
            {
                if (data.RivalFordRegions[index] != target) continue;
                var ford = data.RivalHeldFords[index];
                ClearRivalFord(ford);
                GrantFordPassage(ford, target);
                releasedFords = true;
            }
            ClearIntegration(releasedFords);
            return true;
        }

        public (int Ford, int Region, int Days) FrontierFordThreat()
        {
            var region = IndependentVillageRegion;
            var age = Day - data.IndependentVillageFoundedDay;
            if (region < 0 || data.IndependentVillageFoundedDay == 0 || age < FoodInterval
                || IndependentPopulation(region) < IndependentGuardPopulation || IndependentVillageOccupied)
                return (-1, -1, 0);

            var index = FindExposedFord(region);
            if (index < 0) return (-1, -1, 0);
            var untilReview = FoodInterval - Day % FoodInterval;
            var earliest = Math.Max(untilReview, IndependentGuardAge - age);
            var days = untilReview + (earliest - untilReview + FoodInterval - 1) / FoodInterval * FoodInterval;
            return (data.SecuredFords[index], region, days);
        }

        private int FindExposedFord(int region)
        {
            if (IndependentTerritoryResolver == null || data.RivalFordRegions.Contains(region)) return -1;
            for (var i = 0; i < data.SecuredFords.Count; i++)
            {
                var ford = data.SecuredFords[i];
                if (FordGuaranteeHolder(ford) == region || HasGarrisonAtFord(ford) || (HasWarband && WarbandTravelDays == 0
                    && (WarbandCell == ford || map.CanMarchBetween(WarbandCell, ford)))) continue;
                if (RivalClaimsFordBank(ford)) return i;
            }
            return -1;
        }

        private bool RivalClaimsFordBank(int ford)
            => IndependentTerritoryResolver != null && map.TryGetFordBanks(ford, out var first, out var second)
                && (IndependentTerritoryResolver(first) || IndependentTerritoryResolver(second));

        private void ContestUnguardedPlayerFord(int region)
        {
            var index = FindExposedFord(region);
            if (index < 0) return;
            var ford = data.SecuredFords[index];
            data.SecuredFords.RemoveAt(index);
            data.ClosedFords.Remove(ford);
            data.FordGuarantees.RemoveAll(pact => pact.Ford == ford);
            SetRivalFord(ford, region);
            RefreshFordAccess();
        }

        private void ContestPassageFords(int region)
        {
            var changed = false;
            for (var i = 0; i < data.PassageFords.Count; i++)
            {
                var ford = data.PassageFords[i];
                if (data.PassagePartners[i] != region + 1 || IsFordSecured(ford) || IsFordHeldByRival(ford)) continue;
                SetRivalFord(ford, region);
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

        private void LoseSettlementPopulation(int site, int amount)
        {
            var index = site == CapitalCell ? 0 : data.SettlementCells.IndexOf(site) + 1;
            var loss = Math.Min(amount, Math.Max(0, data.SettlementPopulations[index] - 1));
            data.SettlementPopulations[index] -= loss;
            data.Pop[SettlementProvince(site)] -= loss;
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
