using System;
using System.Globalization;

namespace GrandStrategy.WorldGeneration
{
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
    }

    internal sealed class CampaignState
    {
        public const int StartingCoin = 100;
        public const int IntegrationCost = 25;
        public const int IntegrationDuration = 30;
        public const int MaxNameLength = 32;
        private const int SaveVersion = 1;

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
                IntegrationTarget = -1
            });
        }

        public static CampaignState Restore(ProvinceMap map, string mapKey, CampaignSave save, int colourCount)
        {
            if (save == null || save.Version != SaveVersion || save.MapKey != mapKey)
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
            if (target == -1 ? save.IntegrationDaysLeft != 0
                : save.IntegrationDaysLeft < 1 || save.IntegrationDaysLeft > IntegrationDuration
                    || !state.CanReachIndependentProvince(target))
                throw new ArgumentException("The save contains invalid integration progress.");
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

        public bool TryStartIntegration(int province)
        {
            if (IntegrationTarget != -1 || Coin < IntegrationCost || !CanReachIndependentProvince(province))
                return false;
            data.Coin -= IntegrationCost;
            data.IntegrationTarget = province;
            data.IntegrationDaysLeft = IntegrationDuration;
            return true;
        }

        // Returns true when a new province joins the tribe.
        public bool Advance(double seconds)
        {
            if (Paused || seconds <= 0d || double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
            var elapsed = Math.Min(fractionOfDay + seconds * Speed, int.MaxValue - Day);
            var days = (int)elapsed;
            fractionOfDay = elapsed - days;
            data.Day += days;

            if (IntegrationTarget == -1 || days == 0) return false;
            data.IntegrationDaysLeft = Math.Max(0, IntegrationDaysLeft - days);
            if (IntegrationDaysLeft > 0) return false;
            data.Owned[IntegrationTarget] = true;
            data.IntegrationTarget = -1;
            return true;
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
