// Reads and writes the local JSON campaign save. CampaignState handles validation and migration after
// reading.
using System;
using System.IO;
using UnityEngine;

namespace GrandStrategy.WorldGeneration
{
    internal static class CampaignSaveFile
    {
        // Key the filename by map identity so a save cannot be selected accidentally from another map.
        public static string PathFor(string mapKey)
            => Path.Combine(Application.persistentDataPath, "Campaigns", mapKey + ".json");

        public static CampaignSave Read(string path)
            => JsonUtility.FromJson<CampaignSave>(File.ReadAllText(path));

        // Write a temporary file beside the destination, then replace the save so an interrupted write does
        // not truncate it.
        public static void Write(string path, CampaignSave campaign)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(campaign, true));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
